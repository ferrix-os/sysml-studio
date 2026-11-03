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

    /// <summary>Use cases, their actors and subject, and what they include.</summary>
    UseCase,

    /// <summary>An action's steps and control nodes, in UML's activity notation.</summary>
    Activity,

    /// <summary>Lifelines and messages in time order, top to bottom.</summary>
    Sequence,

    /// <summary>The same messages, numbered, on the links between participants.</summary>
    Communication,

    /// <summary>An activity whose steps are interactions.</summary>
    InteractionOverview,

    /// <summary>Lifelines over an axis of what comes after what.</summary>
    Timing,
}

/// <summary>What a box looks like, where UML has a shape for what it stands for.</summary>
public enum NodeShape
{
    /// <summary>The studio's own box: caption, title, features.</summary>
    Box,

    /// <summary>UML's class: name above, then a compartment each for attributes and operations.</summary>
    Class,

    /// <summary>UML's package: a folder with a tab.</summary>
    Folder,

    /// <summary>UML's instance: an underlined "name : Type", with value slots.</summary>
    Instance,

    /// <summary>UML's component: a box with the component icon, and its ports on the border.</summary>
    Component,

    /// <summary>UML's node: a box with depth.</summary>
    Device,

    /// <summary>UML's artifact: a box with the document icon.</summary>
    Artifact,
}

/// <summary>What a port offers the outside, which UML draws as a ball or a socket.</summary>
public enum PortFlow
{
    /// <summary>A plain square: the model does not say.</summary>
    None,

    /// <summary>The ball: what flows through it flows out.</summary>
    Provided,

    /// <summary>The socket: what flows through it flows in.</summary>
    Required,
}

/// <summary>
/// A port on a box's border. The layout decides where along the border it sits,
/// from where its connectors arrive; <see cref="X"/> and <see cref="Y"/> are the
/// centre of its square, relative to the box.
/// </summary>
public sealed class DiagramPort(string name, PortFlow flow = PortFlow.None)
{
    public string Name { get; } = name;
    public PortFlow Flow { get; } = flow;

    /// <summary>On the box's top border; otherwise on its bottom one.</summary>
    public bool OnTop { get; set; }

    public double X { get; set; }
    public double Y { get; set; }

    /// <summary>How wide the name is drawn, centred under the square.</summary>
    public double LabelWidth => (Name.Length * 6.7) + 6;

    public override string ToString() => Name;
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

    /// <summary>The shape UML gives it; <see cref="NodeShape.Box"/> unless a UML diagram says otherwise.</summary>
    public NodeShape Shape { get; init; }

    /// <summary>A class's operations, in a compartment of their own under <see cref="Features"/>.</summary>
    public List<string> Operations { get; } = [];

    /// <summary>The ports drawn on its border.</summary>
    public List<DiagramPort> Ports { get; } = [];

    /// <summary>Whether the name is set in italics, as UML sets anything abstract.</summary>
    public bool IsAbstract => Pseudo is null && Element.IsAbstract;

    /// <summary>Whether it stands for a reference, which UML draws with a dashed border.</summary>
    public bool IsReference { get; init; }

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

    /// <summary>Drawn with no arrowhead: a composition whose diamond already says which way it reads.</summary>
    public bool IsHeadless { get; init; }

    /// <summary>The port it leaves from, where the model names one.</summary>
    public DiagramPort? SourcePort { get; init; }

    /// <summary>The port it arrives at, where the model names one.</summary>
    public DiagramPort? TargetPort { get; init; }

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

    /// <summary>What the frame around the whole picture says; null for no frame.</summary>
    public string? FrameLabel { get; set; }

    /// <summary>
    /// The frame's rectangle, around every box and edge. It follows the
    /// picture, so it is worked out when asked for, never stored.
    /// </summary>
    public (double X, double Y, double Width, double Height)? FrameBounds()
    {
        if (FrameLabel is null || Nodes.Count == 0)
            return null;

        const double side = 36, top = 50, bottom = 36;
        var points = Edges.SelectMany(e => e.Waypoints).ToList();
        var left = Math.Min(Nodes.Min(n => n.X), points.Count == 0 ? double.MaxValue : points.Min(p => p.X)) - side;
        var right = Math.Max(Nodes.Max(n => n.X + n.Width), points.Count == 0 ? double.MinValue : points.Max(p => p.X)) + side;
        var up = Math.Min(Nodes.Min(n => n.Y), points.Count == 0 ? double.MaxValue : points.Min(p => p.Y)) - top;
        var down = Math.Max(Nodes.Max(n => n.Y + n.Height), points.Count == 0 ? double.MinValue : points.Max(p => p.Y)) + bottom;
        return (left, up, right - left, down - up);
    }
}
