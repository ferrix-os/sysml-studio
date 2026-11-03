using System.Runtime.CompilerServices;
using SysmlStudio.Model;

namespace SysmlStudio.Diagrams;

/// <summary>
/// <para>
/// The seven structural diagrams of UML's taxonomy. The text stays the source
/// of truth and there is no UML model behind them: each one is a view of SysML
/// v2 elements, drawn in that diagram's notation.
/// </para>
/// <para>
/// A view with nothing to show is not offered: <see cref="StructuralKindsFor"/>
/// asks the same questions the builders do.
/// </para>
/// </summary>
public static partial class DiagramBuilder
{
    /// <summary>What the model says once, shared by every view of it, so that browsing a large model does not scan it per element.</summary>
    private static readonly ConditionalWeakTable<Element, StructuralIndex> Indexes = [];

    /// <summary>The structural UML diagrams that have something to show of <paramref name="element"/>.</summary>
    private static List<DiagramKind> StructuralKindsFor(Element element)
    {
        var kinds = new List<DiagramKind>();
        if (element.Kind is "attribute" or "ref" or "doc" or "dependency")
            return kinds;

        var index = IndexOf(element);
        if (IsPackage(element))
        {
            if (PackagesOnDiagram(element, index).Count >= 2)
                kinds.Add(DiagramKind.Package);
            return kinds;
        }

        return kinds;
    }

    private static StructuralIndex IndexOf(Element element) => Indexes.GetValue(RootOf(element), root => new StructuralIndex(root));

    // ----- package ------------------------------------------------------------

    /// <summary>A package, the packages inside it, and the packages it imports or is imported by.</summary>
    private static Diagram PackageDiagram(Element root)
    {
        var index = IndexOf(root);
        var diagram = new Diagram(DiagramKind.Package, root);
        foreach (var package in PackagesOnDiagram(root, index))
            diagram.Nodes.Add(PackageNode(package, index));

        foreach (var node in diagram.Nodes)
        {
            if (node.Element.Parent is { } owner && diagram.NodeFor(owner) is { } ownerNode)
                diagram.Edges.Add(new DiagramEdge(ownerNode, node, RelationKind.Containment));

            foreach (var import in index.ImportsOf(node.Element))
            {
                // A public import re-exports what it imports; UML says "import" for that and "access" for the rest.
                if (import.Target is { } target && diagram.NodeFor(target) is { } targetNode && !ReferenceEquals(targetNode, node))
                    diagram.Edges.Add(new DiagramEdge(node, targetNode, RelationKind.Import, import.IsPublic ? "«import»" : "«access»"));
            }
        }

        return diagram;
    }

    private static List<Element> PackagesOnDiagram(Element root, StructuralIndex index)
    {
        var inside = root.Descendants().Where(IsPackage).Prepend(root).ToList();
        var related = inside.SelectMany(p => index.ImportsOf(p).Select(i => i.Target).OfType<Element>())
            .Concat(inside.SelectMany(index.ImportersOf));
        return [.. inside.Concat(related).Distinct()];
    }

    private static DiagramNode PackageNode(Element package, StructuralIndex index)
    {
        var stereotype = package.Kind == "library package" ? "«library»" : null;
        var node = new DiagramNode(package, package.DisplayName, stereotype) { Shape = NodeShape.Folder };

        const int shown = 6;
        var members = package.Children.Where(c => c.Name is not null && !IsPackage(c)).ToList();
        node.Features.AddRange(members.Take(shown).Select(m => Glyph(m) + m.DisplayName));
        if (members.Count > shown)
            node.Features.Add($"… {members.Count - shown} more");

        // What it imports from outside the loaded model can only be named, not drawn.
        node.Features.AddRange(index.ImportsOf(package).Where(i => i.Target is null).Select(i => $"{(i.IsPublic ? "«import»" : "«access»")} {i.Name}"));
        return node;
    }

    /// <summary>UML's visibility mark. A member with no word is public.</summary>
    private static string Glyph(Element member) => member.Visibility switch
    {
        "private" => "- ",
        "protected" => "# ",
        _ => "+ ",
    };

    /// <summary>
    /// What several views ask of the whole model, read once per model: who
    /// imports what, what is allocated where, and where metadata is applied.
    /// </summary>
    private sealed class StructuralIndex
    {
        private readonly Dictionary<Element, List<PackageImport>> _imports = [];
        private readonly Dictionary<Element, List<Element>> _importers = [];

        public StructuralIndex(Element root)
        {
            var packages = new Dictionary<string, Element>(StringComparer.Ordinal);
            var byName = new Dictionary<string, List<Element>>(StringComparer.Ordinal);
            var elements = root.Descendants().ToList();
            foreach (var element in elements)
            {
                if (IsPackage(element) && element.Name is not null)
                    packages.TryAdd(element.QualifiedName, element);
                if (element.Name is { } name)
                {
                    if (!byName.TryGetValue(name, out var named))
                        byName[name] = named = [];
                    named.Add(element);
                }
            }

            foreach (var element in elements)
            {
                if (IsPackage(element))
                    ReadImports(element, packages, byName);

                foreach (var relation in element.Relations)
                {
                    if (relation.Kind == RelationKind.Allocate && relation.Target is { } to && relation.Origin is { } from)
                        Allocations.Add(new Allocation(from, to, element, element.Name));
                    else if (relation.Kind == RelationKind.Dependency && relation.Target is { } supplier && relation.Origin is { } client)
                        Dependencies.Add((client, supplier));
                }

                if (element.Kind is "connection" or "interface")
                    Connectors.Add(element);

                foreach (var use in element.MetadataUses)
                {
                    if (!byName.TryGetValue(use.TypeName.Split("::")[^1].Trim(), out var named) || named.Find(n => n.Kind == "metadata def") is not { } definition)
                        continue;

                    if (use.About.Count == 0)
                        Applications.Add((definition, element));

                    foreach (var about in use.About)
                    {
                        if (byName.TryGetValue(about.Split("::")[^1].Trim(), out var targets))
                            Applications.Add((definition, targets[0]));
                    }
                }
            }
        }

        public List<Allocation> Allocations { get; } = [];
        public List<(Element From, Element To)> Dependencies { get; } = [];
        public List<Element> Connectors { get; } = [];
        public List<(Element Definition, Element Target)> Applications { get; } = [];

        public List<PackageImport> ImportsOf(Element package) => _imports.TryGetValue(package, out var imports) ? imports : [];

        public List<Element> ImportersOf(Element package) => _importers.TryGetValue(package, out var importers) ? importers : [];

        private void ReadImports(Element package, Dictionary<string, Element> packages, Dictionary<string, List<Element>> byName)
        {
            foreach (var relation in package.Relations.Where(r => r.Kind == RelationKind.Import))
            {
                var name = relation.TargetReference.Trim();
                if (name.EndsWith("::**", StringComparison.Ordinal))
                    name = name[..^4];
                else if (name.EndsWith("::*", StringComparison.Ordinal))
                    name = name[..^3];

                var target = ResolveImport(name, package, packages, byName);
                if (!_imports.TryGetValue(package, out var imports))
                    _imports[package] = imports = [];
                if (imports.Exists(i => i.Name == name))
                    continue;

                imports.Add(new PackageImport(name, target, relation.Label == "public"));
                if (target is null)
                    continue;

                if (!_importers.TryGetValue(target, out var importers))
                    _importers[target] = importers = [];
                importers.Add(package);
            }
        }

        /// <summary>The package an import names, looked for from the inside out; importing one member draws its package.</summary>
        private static Element? ResolveImport(string name, Element from, Dictionary<string, Element> packages, Dictionary<string, List<Element>> byName)
        {
            for (var scope = from; scope is not null; scope = scope.Parent)
            {
                var qualified = scope.QualifiedName.Length == 0 ? name : $"{scope.QualifiedName}::{name}";
                if (packages.TryGetValue(qualified, out var found))
                    return found;
            }

            if (!name.Contains("::", StringComparison.Ordinal))
                return null;

            var member = byName.TryGetValue(name.Split("::")[^1], out var named) ? named.Find(n => n.QualifiedName.EndsWith(name, StringComparison.Ordinal)) : null;
            for (var owner = member?.Parent; owner is not null; owner = owner.Parent)
            {
                if (IsPackage(owner) && owner.Name is not null)
                    return owner;
            }

            return null;
        }
    }

    /// <summary>One import of a package: by what name, what that resolved to, and whether it re-exports.</summary>
    private sealed record PackageImport(string Name, Element? Target, bool IsPublic);

    /// <summary>One end-to-end allocation, and the element that declares it.</summary>
    private sealed record Allocation(Element From, Element To, Element Declaring, string? Name);
}
