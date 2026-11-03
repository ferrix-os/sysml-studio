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
    /// <summary>The definitions a class diagram draws. Behaviour, requirements and metadata have diagrams of their own.</summary>
    private static readonly HashSet<string> ClassKinds =
    [
        "part def", "item def", "attribute def", "enumeration def", "occurrence def", "port def",
        "connection def", "interface def", "flow def", "allocation def", "individual def",
    ];

    /// <summary>Usages that hold or stand for a thing, and so can be an object.</summary>
    private static readonly HashSet<string> ObjectKinds = ["part", "item", "occurrence", "individual", "portion"];

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
            if (element.Children.Any(IsClassLike))
                kinds.Add(DiagramKind.Class);
            if (OutermostObjects(element).Any())
                kinds.Add(DiagramKind.ObjectDiagram);
            return kinds;
        }

        if (IsClassLike(element))
            kinds.Add(DiagramKind.Class);
        if (IsObject(element) || (element.IsDefinition && OutermostObjects(element).Any()))
            kinds.Add(DiagramKind.ObjectDiagram);
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

    // ----- class --------------------------------------------------------------

    /// <summary>
    /// For a package: its definitions. For one definition: it and what is one
    /// relation away. Each is a class with its attributes and operations in
    /// compartments, and what it owns is an edge where the type is on the
    /// diagram: a filled diamond for a part, an arrow for a reference.
    /// </summary>
    private static Diagram ClassDiagram(Element root)
    {
        var diagram = new Diagram(DiagramKind.Class, root);
        var onDiagram = (IsPackage(root) ? root.Children.Where(IsClassLike) : Neighbourhood(root).Where(IsClassLike)).Distinct().ToList();

        foreach (var element in onDiagram)
            diagram.Nodes.Add(ClassNode(element, onDiagram));

        foreach (var element in onDiagram)
        {
            foreach (var relation in element.Relations.Where(r => r.Kind == RelationKind.Specialization))
                Connect(diagram, element, relation.Target, RelationKind.Specialization);

            foreach (var feature in element.Children.Where(c => DrawnAsEdge(c, onDiagram, element)))
            {
                var isReference = feature.Kind == "ref";
                var type = feature.Relations.First(r => r.Kind == RelationKind.Typing).Target!;
                diagram.Edges.Add(new DiagramEdge(diagram.NodeFor(element)!, diagram.NodeFor(type)!,
                    isReference ? RelationKind.Association : RelationKind.Composition, feature.DisplayName + feature.Multiplicity)
                {
                    IsHeadless = !isReference,
                });
            }
        }

        foreach (var (from, to) in IndexOf(root).Dependencies)
            Connect(diagram, from, to, RelationKind.Dependency);

        return diagram;
    }

    /// <summary>UML shows a property as an edge or in the compartment, not both; ports and values stay in the compartment.</summary>
    private static bool DrawnAsEdge(Element feature, List<Element> onDiagram, Element owner)
        => feature.Name is not null
           && feature.Kind is "part" or "item" or "occurrence" or "ref"
           && feature.Relations.FirstOrDefault(r => r.Kind == RelationKind.Typing)?.Target is { } type
           && !ReferenceEquals(type, owner)
           && onDiagram.Contains(type);

    private static DiagramNode ClassNode(Element element, List<Element> onDiagram)
    {
        var node = new DiagramNode(element, element.DisplayName, Abstractly(element, ClassStereotype(element))) { Shape = NodeShape.Class };
        node.Features.AddRange(element.EnumeratedValues);

        node.Features.AddRange(element.Children
            .Where(c => c.Name is not null && (c.Kind is "attribute" || StructuralFeatures.Contains(c.Kind)) && !DrawnAsEdge(c, onDiagram, element))
            .Select(AttributeLine));

        foreach (var behaviour in element.Children.Where(c => c.Name is not null && (IsAction(c) || c.Kind == "calculation")))
            node.Operations.Add(OperationLine(behaviour));

        return node;
    }

    /// <summary>
    /// UML sets an abstract name in italics, and the canvas's fonts have none,
    /// so it also says so in the other way UML allows.
    /// </summary>
    private static string? Abstractly(Element element, string? stereotype)
    {
        if (!element.IsAbstract)
            return stereotype;
        return stereotype is null ? "{abstract}" : stereotype + " {abstract}";
    }

    /// <summary>A part definition is simply a class; the other definitions say what they are.</summary>
    private static string? ClassStereotype(Element element) => element.Kind switch
    {
        "part def" => null,
        "attribute def" => "«dataType»",
        "enumeration def" => "«enumeration»",
        "interface def" => "«interface»",
        _ => $"«{element.Kind}»",
    };

    /// <summary>"+ capacity : Real [1] = 5000", with "«port»" where it is one.</summary>
    private static string AttributeLine(Element feature)
    {
        var typing = feature.Relations.FirstOrDefault(r => r.Kind == RelationKind.Typing);
        var keyword = feature.Kind is "attribute" ? string.Empty : $"«{feature.Kind}» ";
        var conjugation = typing is { IsConjugated: true } ? "~" : string.Empty;
        var type = typing is null ? string.Empty : $" : {conjugation}{typing.TargetReference.Trim()}";
        var multiplicity = feature.Multiplicity is { Length: > 0 } m ? " " + m : string.Empty;
        var value = feature.Value is { Length: > 0 } v ? " = " + v : string.Empty;
        return $"{Glyph(feature)}{keyword}{feature.DisplayName}{type}{multiplicity}{value}";
    }

    /// <summary>"+ arm(in key : Key, out ok : Boolean)", its parameters being its directed features.</summary>
    private static string OperationLine(Element action)
    {
        var typing = action.Relations.FirstOrDefault(r => r.Kind == RelationKind.Typing);
        var signature = action.Children.Any(c => c.Direction is not null) ? action : typing?.Target ?? action;
        var parameters = signature.Children.Where(c => c.Direction is not null).Select(p =>
        {
            var type = p.Relations.FirstOrDefault(r => r.Kind == RelationKind.Typing);
            return type is null ? $"{p.Direction} {p.DisplayName}" : $"{p.Direction} {p.DisplayName} : {type.TargetReference.Trim()}";
        });
        return $"{Glyph(action)}{action.DisplayName}({string.Join(", ", parameters)})";
    }

    // ----- object -------------------------------------------------------------

    /// <summary>
    /// Individuals and usages with values bound, each an underlined "name :
    /// Type" with its values in slots; what it holds is an object of its own,
    /// joined by a composition link, and its connections are links.
    /// </summary>
    private static Diagram ObjectDiagram(Element root)
    {
        var diagram = new Diagram(DiagramKind.ObjectDiagram, root);
        foreach (var element in IsObject(root) ? [root] : OutermostObjects(root))
            AddObject(diagram, element);

        foreach (var node in diagram.Nodes.ToList())
        {
            foreach (var connector in node.Element.Children.Where(IsConnector))
            {
                foreach (var relation in connector.Relations)
                {
                    var source = ObjectEnd(diagram, node.Element, relation.OriginReference);
                    var target = ObjectEnd(diagram, node.Element, relation.TargetReference);
                    if (source is not null && target is not null && !ReferenceEquals(source, target))
                        diagram.Edges.Add(new DiagramEdge(source, target, RelationKind.Link, connector.Name));
                }
            }

            // "ref pilot = alice": a slot whose value is another object on the diagram is a link.
            foreach (var slot in node.Element.Children.Where(c => c.Kind == "ref" && c.Value is { Length: > 0 }))
            {
                var target = diagram.Nodes.Find(n => n.Element.Name == slot.Value);
                if (target is null || ReferenceEquals(target, node))
                    continue;

                // UML shows it as a link or a slot, not both.
                node.Features.Remove($"{SlotName(slot)} = {slot.Value}");
                diagram.Edges.Add(new DiagramEdge(node, target, RelationKind.Link, SlotName(slot)));
            }
        }

        return diagram;
    }

    private static DiagramNode AddObject(Diagram diagram, Element element)
    {
        if (diagram.NodeFor(element) is { } existing)
            return existing;

        var node = new DiagramNode(element, InstanceLabel(element), element.IsIndividual ? "«individual»" : null) { Shape = NodeShape.Instance };
        foreach (var slot in element.Children.Where(HasBoundValue))
            node.Features.Add($"{SlotName(slot)} = {slot.Value}");
        diagram.Nodes.Add(node);

        foreach (var held in element.Children.Where(IsHeldObject))
        {
            var child = AddObject(diagram, held);
            diagram.Edges.Add(new DiagramEdge(node, child, RelationKind.Composition) { IsHeadless = true });
        }

        return node;
    }

    /// <summary>"unit7 : Drone", or ": Drone" for a usage with no name, or what it redefines.</summary>
    private static string InstanceLabel(Element element)
    {
        var type = element.Relations.FirstOrDefault(r => r.Kind == RelationKind.Typing)
            ?? element.Relations.FirstOrDefault(r => r.Kind == RelationKind.Specialization);
        var name = element.Name ?? element.Relations.FirstOrDefault(r => r.Kind == RelationKind.Redefinition)?.TargetReference.Trim() ?? string.Empty;
        var label = type is null ? name : $"{name} : {type.TargetReference.Trim()}";
        return label + element.Multiplicity;
    }

    private static string SlotName(Element slot)
        => slot.Name ?? slot.Relations.FirstOrDefault(r => r.Kind == RelationKind.Redefinition)?.TargetReference.Trim() ?? slot.DisplayName;

    private static bool HasBoundValue(Element element) => element.Kind is "attribute" or "ref" && element.Value is { Length: > 0 };

    /// <summary>A usage that stands for one thing: it says it is an individual, or says what its values are.</summary>
    private static bool IsObject(Element element)
        => element.IsIndividual || (ObjectKinds.Contains(element.Kind) && !element.IsDefinition && element.Children.Any(HasBoundValue));

    /// <summary>What an object holds is an object too, written or not with values of its own.</summary>
    private static bool IsHeldObject(Element element)
        => ObjectKinds.Contains(element.Kind) && (element.Name is not null || element.Relations.Any(r => r.Kind == RelationKind.Redefinition));

    /// <summary>The objects under an element, without those another object already holds.</summary>
    private static IEnumerable<Element> OutermostObjects(Element root)
    {
        foreach (var child in root.Children)
        {
            if (IsObject(child))
            {
                yield return child;
                continue;
            }

            foreach (var inner in OutermostObjects(child))
                yield return inner;
        }
    }

    private static DiagramNode? ObjectEnd(Diagram diagram, Element owner, string? reference)
    {
        var head = reference?.Trim().Split('.')[0].Split("::")[^1].Trim();
        return head is null ? null : diagram.Nodes.Find(n => ReferenceEquals(n.Element.Parent, owner) && SlotName(n.Element) == head);
    }

    private static bool IsClassLike(Element element) => element.Name is not null && ClassKinds.Contains(element.Kind);

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
