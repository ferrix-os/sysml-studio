namespace SysmlStudio.Diagrams;

/// <summary>How a node is drawn. Everything but a box is drawn from <see cref="NodeMarks"/>.</summary>
public enum NodeNotation
{
    /// <summary>The canvas's own box: caption, title, compartment.</summary>
    Box,

    /// <summary>A box with UML's round corners: an action or a state.</summary>
    Rounded,

    /// <summary>A diamond: a decision or a merge.</summary>
    Decision,

    /// <summary>A thick bar: a fork or a join.</summary>
    Bar,

    /// <summary>A bullseye drawn for "terminate", which ends the whole activity.</summary>
    Final,

    /// <summary>A pentagon pointing onward: a send action.</summary>
    Send,

    /// <summary>A pentagon with a notch: an accept action.</summary>
    Accept,

    /// <summary>A stick figure.</summary>
    Actor,

    /// <summary>An ellipse with the name inside.</summary>
    UseCase,

    /// <summary>The rectangle a use case's subject draws around it.</summary>
    Boundary,

    /// <summary>A state with states inside it.</summary>
    CompositeState,

    /// <summary>A head and a dashed line down, with activation bars on the line.</summary>
    Lifeline,

    /// <summary>A frame tagged "ref": an interaction used as a step.</summary>
    InteractionUse,

    /// <summary>The whole diagram's frame, tagged "sd" or "timing".</summary>
    Frame,

    /// <summary>A name and a line along the time axis.</summary>
    TimeRow,

    /// <summary>A hexagon over the stretch of the axis a timeslice takes.</summary>
    TimeBand,

    /// <summary>A tick on the line at one place on the axis: a snapshot.</summary>
    TimePoint,

    /// <summary>The axis itself, which says what it does not know.</summary>
    TimeAxis,
}

/// <summary>How an edge is drawn, where the relation's kind alone does not say.</summary>
public enum EdgeNotation
{
    /// <summary>As its <see cref="DiagramEdge.Kind"/> is drawn everywhere.</summary>
    ByKind,

    /// <summary>A plain line: an actor's association, a communication link.</summary>
    Association,

    /// <summary>A solid line and an open head: a message nobody waits for.</summary>
    AsyncMessage,

    /// <summary>A solid line and a filled head: a succession flow, whose target waits for the transfer.</summary>
    SyncMessage,
}

/// <summary>One message on a communication link, which says which way it goes.</summary>
public sealed record LinkMessage(int Number, string Text, DiagramNode From);

/// <summary>A stretch of a lifeline an action of its takes up, in the node's own coordinates.</summary>
public readonly record struct Activation(double Top, double Bottom);

/// <summary>An event on a lifeline that no message starts or ends, in the node's own coordinates.</summary>
public readonly record struct LifelineEvent(double Y, string Label);

public sealed partial class DiagramNode
{
    /// <summary>How the node is drawn.</summary>
    public NodeNotation Notation { get; init; }

    /// <summary>The node this one is drawn inside: the composite state, the system boundary. Moves with it.</summary>
    public DiagramNode? Container { get; set; }

    /// <summary>Whether a drag may move it. Where a node stands on a time axis is what it says.</summary>
    public bool IsFixed { get; init; }

    /// <summary>Whether it is drawn from <see cref="NodeMarks"/> rather than as a box.</summary>
    public bool IsDrawn => Notation is not (NodeNotation.Box or NodeNotation.Rounded);

    /// <summary>The second line of a band or of a lifeline's head: "charge = 100".</summary>
    public string? Detail { get; init; }

    /// <summary>What a frame's tag says: "sd", "ref", "timing".</summary>
    public string? Tag { get; init; }

    /// <summary>The most edges meeting a bar at one side, which it has to be wide enough for.</summary>
    public int Span { get; set; } = 1;

    /// <summary>Where a lifeline's activation bars are.</summary>
    public List<Activation> Activations { get; } = [];

    /// <summary>The events on a lifeline no message starts or ends.</summary>
    public List<LifelineEvent> Events { get; } = [];

    /// <summary>Where a parallel state's regions meet, and where a time axis has its ticks; local x.</summary>
    public List<double> Ticks { get; } = [];
}

public sealed partial class DiagramEdge
{
    /// <summary>How the edge is drawn.</summary>
    public EdgeNotation Notation { get; init; }

    /// <summary>Which step of a sequence a message is, counted from the top.</summary>
    public int Row { get; init; }

    /// <summary>What a communication link carries; its label is written from these once it is known which way each one points.</summary>
    public List<LinkMessage> Messages { get; } = [];

    /// <summary>A UML control flow and a transition have an open head, as a message does.</summary>
    public bool HasOpenHead => Notation == EdgeNotation.AsyncMessage
        || (Notation == EdgeNotation.ByKind && Kind is SysmlStudio.Model.RelationKind.Succession or SysmlStudio.Model.RelationKind.Transition);
}

public sealed partial class Diagram
{
    /// <summary>Set on the temporary diagram of one nesting level, which is laid out as a plain one.</summary>
    internal bool IsScope { get; init; }

    /// <summary>How many steps a sequence has, top to bottom.</summary>
    internal int RowCount { get; set; }

    /// <summary>Events on a lifeline no message starts or ends, by step.</summary>
    internal List<(DiagramNode Node, int Row, string Label)> EventRows { get; } = [];

    /// <summary>Activation bars, by the steps they cover.</summary>
    internal List<(DiagramNode Node, int First, int Last)> BarRows { get; } = [];

    /// <summary>The portions on a timing diagram, in order within each row.</summary>
    internal List<TimeMark> TimeMarks { get; } = [];
}

/// <summary>A portion of a life and where it goes on a timing diagram.</summary>
internal sealed class TimeMark(DiagramNode node, DiagramNode row, SysmlStudio.Model.Element step)
{
    public DiagramNode Node { get; } = node;
    public DiagramNode Row { get; } = row;
    public SysmlStudio.Model.Element Step { get; } = step;
    public int Rank { get; set; }
}
