namespace SysmlStudio.Diagrams;

/// <summary>
/// <para>
/// Puts each port on its box's border and brings its connectors to it. A layered
/// layout has edges arriving from above and below, so ports sit on the top and
/// bottom borders, with their names in a band inside the box, where nothing can
/// run over them.
/// </para>
/// <para>
/// The routers aim at a box's centre, so a port goes where its connectors
/// arrive anyway, spread apart until the names fit, and each connector is then
/// bent the last stretch to reach it: straight on where it came from that side,
/// around the box where it came from another.
/// </para>
/// </summary>
internal static class PortPlacement
{
    /// <summary>Half the side of a port's square; connectors end on its outer edge.</summary>
    public const double HalfSquare = 5;

    /// <summary>How far a connector leaves a port straight on, past the ball or socket.</summary>
    private const double Stub = 18;

    /// <summary>How much of a connector is bent to bring its end to the port.</summary>
    private const double BendReach = 80;

    /// <summary>How far clear of a box a connector goes around it.</summary>
    private const double Around = 14;

    public static void Apply(Diagram diagram)
    {
        foreach (var node in diagram.Nodes.Where(n => n.Ports.Count > 0))
            Place(diagram, node);

        foreach (var edge in diagram.Edges.Where(e => e.Waypoints.Count >= 2))
        {
            if (edge.SourcePort is { } source)
                Attach(edge, edge.Source, source, atStart: true);
            if (edge.TargetPort is { } target)
                Attach(edge, edge.Target, target, atStart: false);

            edge.Waypoints[0] = PastTheTab(edge.Source, edge.Waypoints[0]);
            edge.Waypoints[^1] = PastTheTab(edge.Target, edge.Waypoints[^1]);
        }
    }

    /// <summary>A folder's box is its tab's height taller than its body, so an edge ending beside the tab reaches on down to the body.</summary>
    private static (double X, double Y) PastTheTab(DiagramNode node, (double X, double Y) end)
        => node.Shape == NodeShape.Folder && end.X > node.X + DiagramLayout.FolderTabWidth(node.Width) && Math.Abs(end.Y - node.Y) < 3
            ? (end.X, node.Y + DiagramLayout.FolderTab)
            : end;

    private static void Place(Diagram diagram, DiagramNode node)
    {
        var arrivals = node.Ports.ToDictionary(p => p, _ => new List<(double X, double Y)>());
        foreach (var edge in diagram.Edges.Where(e => e.Waypoints.Count >= 2))
        {
            if (ReferenceEquals(edge.Source, node) && edge.SourcePort is { } source && arrivals.TryGetValue(source, out var leaving))
                leaving.Add(edge.Waypoints[0]);
            if (ReferenceEquals(edge.Target, node) && edge.TargetPort is { } target && arrivals.TryGetValue(target, out var arriving))
                arriving.Add(edge.Waypoints[^1]);
        }

        var middle = node.Y + (node.Height / 2);
        var top = new List<DiagramPort>();
        var bottom = new List<DiagramPort>();
        foreach (var port in node.Ports.Where(p => arrivals[p].Count > 0))
        {
            var points = arrivals[port];
            (points.Average(p => p.Y) < middle ? top : bottom).Add(port);
            port.X = points.Average(p => p.X) - node.X;
        }

        // A port nothing is connected to takes the emptier side, spaced along it.
        var free = node.Ports.Where(p => arrivals[p].Count == 0).ToList();
        for (var i = 0; i < free.Count; i++)
        {
            free[i].X = node.Width * (i + 1) / (free.Count + 1);
            (top.Count < bottom.Count ? top : bottom).Add(free[i]);
        }

        Spread(top, node, onTop: true);
        Spread(bottom, node, onTop: false);
    }

    /// <summary>Moves one side's ports apart, as little as will let their names fit, and keeps them on the box.</summary>
    private static void Spread(List<DiagramPort> ports, DiagramNode node, bool onTop)
    {
        var ordered = ports.OrderBy(p => p.X).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var port = ordered[i];
            var earliest = i == 0
                ? Margin(port)
                : ordered[i - 1].X + Gap(ordered[i - 1], port);
            port.X = Math.Max(port.X, earliest);
        }

        for (var i = ordered.Count - 1; i >= 0; i--)
        {
            var port = ordered[i];
            var latest = i == ordered.Count - 1
                ? node.Width - Margin(port)
                : ordered[i + 1].X - Gap(port, ordered[i + 1]);
            port.X = Math.Min(port.X, latest);
        }

        foreach (var port in ordered)
        {
            port.OnTop = onTop;
            port.Y = onTop ? 0 : node.Height;
        }

        static double Margin(DiagramPort port) => (port.LabelWidth / 2) + 6;
        static double Gap(DiagramPort a, DiagramPort b) => ((a.LabelWidth + b.LabelWidth) / 2) + 10;
    }

    /// <summary>Brings one end of a connector to its port.</summary>
    private static void Attach(DiagramEdge edge, DiagramNode node, DiagramPort port, bool atStart)
    {
        var points = edge.Waypoints;
        if (atStart)
            points.Reverse();

        var outward = port.OnTop ? -1 : 1;
        var (px, py) = (node.X + port.X, node.Y + port.Y + (outward * HalfSquare));
        var stub = (X: px, Y: py + (outward * Stub));
        var (X, Y) = points[^1];

        var left = node.X;
        var right = node.X + node.Width;
        var fromOtherSide = port.OnTop ? Y > node.Y + node.Height - 3 : Y < node.Y + 3;

        // Arriving at a side from the port's own side of the box, the end only has to bend over the corner.
        var onSide = Math.Abs(X - left) < 3 || Math.Abs(X - right) < 3;
        var earlier = points[Math.Max(0, points.Count - 8)];
        var comesOver = port.OnTop ? earlier.Y < node.Y : earlier.Y > node.Y + node.Height;

        if (fromOtherSide || (onSide && !comesOver))
        {
            // Around the box, by the side nearer where it arrived.
            var x = X - left < right - X ? left - Around : right + Around;
            if (fromOtherSide)
                points.Add((X, Y - (outward * Around)));
            points.Add((x, points[^1].Y));
            points.Add((x, stub.Y));
        }
        else
        {
            Bend(points, stub);
        }

        points.Add(stub);
        points.Add((px, py));

        if (atStart)
            points.Reverse();
    }

    /// <summary>Moves the last point to <paramref name="end"/> and the points before it by less and less, so the line stays smooth.</summary>
    private static void Bend(List<(double X, double Y)> points, (double X, double Y) end)
    {
        var (dx, dy) = (end.X - points[^1].X, end.Y - points[^1].Y);
        var travelled = 0.0;
        for (var i = points.Count - 1; i >= 1 && travelled < BendReach; i--)
        {
            var step = Math.Sqrt(Math.Pow(points[i].X - points[i - 1].X, 2) + Math.Pow(points[i].Y - points[i - 1].Y, 2));
            var fade = 1 - (travelled / BendReach);
            points[i] = (points[i].X + (dx * fade * fade), points[i].Y + (dy * fade * fade));
            travelled += step;
        }
    }
}
