using SysmlStudio.Model;

namespace SysmlStudio.Diagrams;

/// <summary>The kinds of picture the model can be read as.</summary>
public enum DiagramKind
{
    /// <summary>Definitions with their specializations, features and typings.</summary>
    Definition,

    /// <summary>The parts inside one definition, with their ports and connections.</summary>
    Interconnection,

    /// <summary>Requirements with the satisfy, verify and allocate edges that trace to them.</summary>
    Requirements,

    /// <summary>An action's sub-actions in succession order.</summary>
    ActionFlow,

    /// <summary>A state definition's states and transitions.</summary>
    StateMachine,

    /// <summary>UML package diagram: packages, what they nest and what they import.</summary>
    Package,

    /// <summary>UML class diagram: definitions as classes, with specialization, composition and association.</summary>
    Class,

    /// <summary>UML object diagram: individuals and usages with bound values, and the links between them.</summary>
    ObjectDiagram,

    /// <summary>UML component diagram: part definitions with their ports and interfaces.</summary>
    Component,

    /// <summary>UML composite structure diagram: one definition's parts, ports and connectors.</summary>
    CompositeStructure,

    /// <summary>UML deployment diagram: what is allocated onto which parts.</summary>
    Deployment,

    /// <summary>UML profile diagram: metadata definitions as stereotypes, and what they apply to.</summary>
    Profile,

}

/// <summary>One box. <see cref="Element"/> is what it stands for in the model.</summary>
public sealed partial class DiagramNode(Element element, string label, string? stereotype = null, string? pseudo = null)
{
    public Element Element { get; } = element;
    public string Label { get; } = label;

    /// <summary>
    /// "start" or "done" for an action flow's initial and final nodes. They are
    /// written in the model ("first start", "then done") but are not elements
    /// of it, so a pseudo node stands on the diagram's own root.
    /// </summary>
    public string? Pseudo { get; } = pseudo;

    public bool IsPseudo => Pseudo is not null;

    /// <summary>What the node is stored under in the sidecar file.</summary>
    public string Key => Pseudo is null ? Element.QualifiedName : $"{Element.QualifiedName}#{Pseudo}";

    /// <summary>What is shown above the name: "«part def»".</summary>
    public string? Stereotype { get; } = stereotype;

    /// <summary>The maturity keyword the element carries, which is what colour says.</summary>
    public string? Maturity => Element.Maturity;

    /// <summary>The feature lines shown in the node's lower compartment.</summary>
    public List<string> Features { get; } = [];

    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    public override string ToString() => Label;
}

/// <summary>One arrow, drawn the way UML draws that relation.</summary>
public sealed partial class DiagramEdge(DiagramNode source, DiagramNode target, RelationKind kind, string? label = null)
{
    public DiagramNode Source { get; } = source;
    public DiagramNode Target { get; } = target;
    public RelationKind Kind { get; } = kind;
    public string? Label { get; set; } = label;

    /// <summary>The name of the relation itself, when it has one and the label says something else.</summary>
    public string? Name { get; init; }

    /// <summary>The route found around the boxes and, where cheap, around other edges, in the same coordinates as the nodes.</summary>
    public List<(double X, double Y)> Waypoints { get; } = [];

    /// <summary>Where MSAGL put the label's centre, clear of boxes and other labels; null without a route.</summary>
    public (double X, double Y)? LabelCentre { get; set; }

    public override string ToString() => $"{Source} -{Kind}-> {Target}";
}

/// <summary>
/// A picture of part of the model: which elements are in it, which relations
/// between them are drawn, and where everything sits. Positions come from the
/// layout engine and may be overridden per diagram by the sidecar file; the
/// .sysml files never carry coordinates.
/// </summary>
public sealed partial class Diagram(DiagramKind kind, Element root)
{
    public DiagramKind Kind { get; } = kind;

    /// <summary>The element the diagram is "of": a package, a part def, an action, a state def.</summary>
    public Element Root { get; } = root;

    public string Title => $"{Root.DisplayName} · {Kind}";

    public List<DiagramNode> Nodes { get; } = [];
    public List<DiagramEdge> Edges { get; } = [];

    public DiagramNode? NodeFor(Element element) => Nodes.Find(n => !n.IsPseudo && ReferenceEquals(n.Element, element));

    public DiagramNode? PseudoNode(string pseudo) => Nodes.Find(n => n.Pseudo == pseudo);
}
