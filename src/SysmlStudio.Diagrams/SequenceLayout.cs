namespace SysmlStudio.Diagrams;

/// <summary>
/// <para>
/// A sequence diagram's layout: time runs down, one row to a step, and every
/// message is level at its own row. Layered layout has no notion of this, and
/// a row is what the diagram says, so nothing may move a message off it.
/// </para>
/// <para>
/// A lifeline can be dragged sideways. Its place in time cannot be dragged, so
/// a route after a drag puts it back at the top and redraws its messages.
/// </para>
/// </summary>
internal static class SequenceLayout
{
    private const double RowHeight = 48;
    private const double LoopRowHeight = 68;
    private const double FrameMargin = 28;
    private const double HeadGap = 26;
    private const double MinimumGap = 30;
    private const double LoopWidth = 34;
    private const double LoopHeight = 22;
    private const double BarHalfWidth = 5;
    private const double BarOverhang = 14;
    private const double LabelHeight = 16;

    private static List<DiagramNode> Lifelines(Diagram diagram) => diagram.Nodes.FindAll(n => n.Notation == NodeNotation.Lifeline);

    public static void Apply(Diagram diagram, bool measure)
    {
        var lifelines = Lifelines(diagram);
        if (measure)
        {
            foreach (var node in lifelines)
                NodeMarks.Measure(node);
        }

        PlaceColumns(diagram, lifelines);
        Route(diagram);
    }

    /// <summary>
    /// Spaces the lifelines so every label fits over its arrow: a gap takes the
    /// labels of the messages that cross only it, and a wider message makes up
    /// what it still lacks in the last gap it crosses.
    /// </summary>
    private static void PlaceColumns(Diagram diagram, List<DiagramNode> lifelines)
    {
        var index = new Dictionary<DiagramNode, int>();
        for (var i = 0; i < lifelines.Count; i++)
            index[lifelines[i]] = i;

        var gaps = new double[Math.Max(0, lifelines.Count)];
        for (var i = 0; i + 1 < lifelines.Count; i++)
            gaps[i] = ((lifelines[i].Width + lifelines[i + 1].Width) / 2) + MinimumGap;

        var byReach = diagram.Edges.OrderBy(e => Math.Abs(index[e.Source] - index[e.Target]));
        foreach (var message in byReach)
        {
            var width = NodeMarks.TextWidth(message.Label ?? string.Empty) + 24;
            var (from, to) = (Math.Min(index[message.Source], index[message.Target]), Math.Max(index[message.Source], index[message.Target]));
            if (from == to)
            {
                gaps[from] = Math.Max(gaps[from], LoopWidth + width + (lifelines[from].Width / 2));
                continue;
            }

            var lacking = width - gaps[from..to].Sum();
            if (lacking > 0)
                gaps[to - 1] += lacking;
        }

        var x = FrameMargin;
        for (var i = 0; i < lifelines.Count; i++)
        {
            lifelines[i].X = x;
            if (i + 1 < lifelines.Count)
                x = lifelines[i].X + (lifelines[i].Width / 2) + gaps[i] - (lifelines[i + 1].Width / 2);
        }
    }

    /// <summary>Puts every lifeline back in its row and draws what hangs off it, wherever it now stands sideways.</summary>
    public static void Route(Diagram diagram)
    {
        var lifelines = Lifelines(diagram);
        var loops = diagram.Edges.Where(e => ReferenceEquals(e.Source, e.Target)).Select(e => e.Row).ToHashSet();

        const double top = NodeMarks.FrameTag + 14;
        var rows = new double[diagram.RowCount];
        var cursor = top + NodeMarks.LifelineHead + HeadGap;
        for (var row = 0; row < rows.Length; row++)
        {
            rows[row] = cursor;
            cursor += loops.Contains(row) ? LoopRowHeight : RowHeight;
        }

        foreach (var lifeline in lifelines)
        {
            lifeline.Y = top;
            lifeline.Height = cursor - top;
            lifeline.Activations.Clear();
            lifeline.Events.Clear();
        }

        foreach (var (node, first, last) in diagram.BarRows)
            node.Activations.Add(new Activation(rows[first] - top - BarOverhang, rows[last] - top + BarOverhang));

        foreach (var (node, row, label) in diagram.EventRows)
            node.Events.Add(new LifelineEvent(rows[row] - top, label));

        foreach (var message in diagram.Edges)
            DrawMessage(message, rows[message.Row]);

        FitFrame(diagram, lifelines, cursor);
    }

    private static void DrawMessage(DiagramEdge message, double y)
    {
        var from = Centre(message.Source);
        message.Waypoints.Clear();

        if (ReferenceEquals(message.Source, message.Target))
        {
            var near = from + BarSide(message.Source, y, 1);
            var far = near + LoopWidth;
            message.Waypoints.AddRange([(near, y - (LoopHeight / 2)), (far, y - (LoopHeight / 2)), (far, y + (LoopHeight / 2)), (near, y + (LoopHeight / 2))]);
            if (message.Label is { } text)
                message.LabelCentre = (far + (NodeMarks.TextWidth(text) / 2) + 8, y);
            return;
        }

        var to = Centre(message.Target);
        var direction = Math.Sign(to - from);
        var start = from + BarSide(message.Source, y, direction);
        var end = to - BarSide(message.Target, y, direction);
        message.Waypoints.AddRange([(start, y), (end, y)]);
        message.LabelCentre = message.Label is null ? null : ((start + end) / 2, y - (LabelHeight / 2) - 2);
    }

    /// <summary>How far from the lifeline's centre a message starts or ends: at the bar's edge, where there is a bar.</summary>
    private static double BarSide(DiagramNode lifeline, double y, int direction)
    {
        var local = y - lifeline.Y;
        return lifeline.Activations.Exists(bar => bar.Top <= local && local <= bar.Bottom) ? BarHalfWidth * direction : 0;
    }

    private static double Centre(DiagramNode lifeline) => lifeline.X + (lifeline.Width / 2);

    private static void FitFrame(Diagram diagram, List<DiagramNode> lifelines, double bottom)
    {
        if (diagram.Nodes.Find(n => n.Notation == NodeNotation.Frame) is not { } frame || lifelines.Count == 0)
            return;

        var right = lifelines.Max(l => l.X + l.Width);
        foreach (var message in diagram.Edges)
        {
            if (message.Waypoints.Count > 0)
                right = Math.Max(right, message.Waypoints.Max(p => p.X));
            if (message.LabelCentre is { } label)
                right = Math.Max(right, label.X + (NodeMarks.TextWidth(message.Label ?? string.Empty) / 2));
        }

        var eventRight = lifelines.Where(l => l.Events.Count > 0)
            .Select(l => Centre(l) + 12 + l.Events.Max(e => NodeMarks.TextWidth(e.Label, 12)))
            .DefaultIfEmpty(right)
            .Max();

        frame.X = lifelines.Min(l => l.X) - FrameMargin;
        frame.Y = 0;
        frame.Width = Math.Max(Math.Max(right, eventRight) + FrameMargin - frame.X, NodeMarks.TextWidth($"{frame.Tag} {frame.Label}", 12) + 48);
        frame.Height = bottom + (FrameMargin / 2);
    }
}
