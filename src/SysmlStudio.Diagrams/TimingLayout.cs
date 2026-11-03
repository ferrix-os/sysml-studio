namespace SysmlStudio.Diagrams;

/// <summary>
/// A timing diagram's layout: one row to a life, one column to a place in the
/// order the model states. Every column is the same width, because the model
/// gives no durations for a width to stand for. Nothing here can be dragged:
/// where a mark stands is what it says.
/// </summary>
internal static class TimingLayout
{
    private const double FrameMargin = 28;
    private const double RowHeight = 62;
    private const double MarkHeight = 46;
    private const double AxisHeight = 56;
    private const double MinimumColumn = 130;
    private const double MaximumColumn = 300;
    private const double LabelGap = 26;

    public static void Apply(Diagram diagram, bool measure)
    {
        _ = measure; // every size here follows from the layout's own grid

        var rows = diagram.Nodes.FindAll(n => n.Notation == NodeNotation.TimeRow);
        if (rows.Count == 0)
            return;

        var marks = diagram.TimeMarks;
        var lastRank = marks.Count == 0 ? 0 : marks.Max(m => m.Rank);
        var nameColumn = rows.Max(r => NodeMarks.TextWidth(r.Label, 14)) + LabelGap;
        var column = Math.Clamp(marks.Count == 0 ? 0 : marks.Max(m => Math.Max(NodeMarks.TextWidth(m.Node.Label), NodeMarks.TextWidth(m.Node.Detail ?? string.Empty, 11)) + 36), MinimumColumn, MaximumColumn);
        var left = FrameMargin + nameColumn;
        var width = nameColumn + ((lastRank + 1) * column) + 16;

        var y = NodeMarks.FrameTag + 16;
        foreach (var row in rows)
        {
            row.X = FrameMargin;
            row.Y = y;
            row.Width = width;
            row.Height = RowHeight;
            row.Ticks.Clear();
            row.Ticks.Add(nameColumn);

            var inRow = marks.FindAll(m => ReferenceEquals(m.Row, row));
            for (var i = 0; i < inRow.Count; i++)
            {
                var start = left + (inRow[i].Rank * column);
                var end = i + 1 < inRow.Count ? left + (inRow[i + 1].Rank * column) : start + column;
                Place(inRow[i].Node, start, end, y);
            }

            y += RowHeight;
        }

        var axis = diagram.Nodes.Find(n => n.Notation == NodeNotation.TimeAxis);
        if (axis is not null)
        {
            axis.X = FrameMargin;
            axis.Y = y + 6;
            axis.Width = width;
            axis.Height = AxisHeight;
            axis.Ticks.Clear();
            axis.Ticks.Add(nameColumn);
            for (var rank = 1; rank <= lastRank; rank++)
                axis.Ticks.Add(nameColumn + (rank * column));
        }

        foreach (var edge in diagram.Edges)
            DrawOrder(edge);

        if (diagram.Nodes.Find(n => n.Notation == NodeNotation.Frame) is { } frame)
        {
            var caption = axis is null ? 0 : Math.Max(NodeMarks.TextWidth(axis.Label, 12), NodeMarks.TextWidth(axis.Detail ?? string.Empty, 12));
            frame.X = 0;
            frame.Y = 0;
            frame.Width = Math.Max(width, caption) + (FrameMargin * 2);
            frame.Height = y + AxisHeight + 16;
        }
    }

    private static void Place(DiagramNode mark, double start, double end, double rowTop)
    {
        mark.X = start;
        mark.Width = end - start;
        mark.Y = rowTop + ((RowHeight - MarkHeight) / 2);
        mark.Height = MarkHeight;
    }

    /// <summary>A stated order between two lives, from the earlier mark's end to the later one's start.</summary>
    private static void DrawOrder(DiagramEdge edge)
    {
        var to = edge.Target.X;
        var from = Math.Min(edge.Source.Notation == NodeNotation.TimePoint ? edge.Source.X : edge.Source.X + edge.Source.Width, to);

        var down = edge.Target.Y > edge.Source.Y;
        var fromY = edge.Source.Y + (down ? edge.Source.Height : 0);
        var toY = edge.Target.Y + (down ? 0 : edge.Target.Height);

        edge.Waypoints.Clear();
        edge.Waypoints.AddRange([(from, fromY), (to, toY)]);
        edge.LabelCentre = edge.Label is null ? null : (((from + to) / 2) + 6 + (NodeMarks.TextWidth(edge.Label) / 2), (fromY + toY) / 2);
    }
}
