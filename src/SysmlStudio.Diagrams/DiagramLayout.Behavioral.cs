using Microsoft.Msagl.Core.Geometry;
using Microsoft.Msagl.Core.Geometry.Curves;
using SysmlStudio.Model;

namespace SysmlStudio.Diagrams;

/// <summary>
/// The kinds a plain layered layout does not fit. A state holds states and a
/// boundary holds use cases, so each nesting level is laid out by itself as a
/// plain diagram and its container is made to fit. A sequence and a timing
/// diagram say something with where things stand, and have layouts of their own.
/// </summary>
public static partial class DiagramLayout
{
    private const double ContainerPadding = 26;
    private const double ActorGap = 110;
    private const double GroupGap = 44;
    private const double LoopReach = 30;
    private const double LoopHeight = 26;

    private static bool ApplyNotation(Diagram diagram, bool measure)
    {
        if (diagram.IsScope)
            return false;

        switch (diagram.Kind)
        {
            case DiagramKind.Sequence:
                SequenceLayout.Apply(diagram, measure);
                return true;
            case DiagramKind.Timing:
                TimingLayout.Apply(diagram, measure);
                return true;
            case DiagramKind.UseCase:
                ApplyUseCase(diagram, measure);
                return true;
            case DiagramKind.StateMachine:
                ApplyStates(diagram, measure);
                return true;
            default:
                return false;
        }
    }

    private static bool RouteNotation(Diagram diagram)
    {
        if (diagram.IsScope)
            return false;

        switch (diagram.Kind)
        {
            case DiagramKind.Sequence:
                SequenceLayout.Route(diagram);
                return true;
            case DiagramKind.Timing:
                TimingLayout.Apply(diagram, measure: false);
                return true;
            case DiagramKind.UseCase:
                RouteUseCase(diagram);
                return true;
            case DiagramKind.StateMachine:
                RouteStates(diagram);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Where nodes stand is the picture itself, or they sit on purpose inside one another.</summary>
    private static bool KeepsItsOwnPlaces(Diagram diagram)
        => !diagram.IsScope && (diagram.Kind is DiagramKind.Sequence or DiagramKind.Timing or DiagramKind.UseCase
                                || diagram.Nodes.Exists(n => n.Container is not null));

    /// <summary>The curve a node's edges end on, so an arrow meets the shape that is drawn and not its bounding box.</summary>
    private static ICurve Outline(DiagramNode node, Point centre) => node.Notation switch
    {
        NodeNotation.UseCase => CurveFactory.CreateEllipse(node.Width / 2, node.Height / 2, centre),
        NodeNotation.Decision => CurveFactory.CreateDiamond(node.Width / 2, node.Height / 2, centre),
        _ => CurveFactory.CreateRectangle(node.Width, node.Height, centre),
    };

    /// <summary>What every route gets at the very end: loops, straight lines across levels, and labels that point the right way.</summary>
    private static void FinishEdges(Diagram diagram)
    {
        if (diagram.IsScope)
            return;

        foreach (var edge in diagram.Edges)
        {
            if (ReferenceEquals(edge.Source, edge.Target))
            {
                DrawLoop(edge);
            }
            else if (diagram.Kind == DiagramKind.StateMachine && !ReferenceEquals(edge.Source.Container, edge.Target.Container))
            {
                DrawAcrossLevels(edge);
            }

            if (edge.Messages.Count > 0)
            {
                edge.Label = LinkLabel(edge);
                PutLabelBeside(diagram, edge);
            }
        }
    }

    /// <summary>
    /// A link's list of messages reads as belonging to the link only right
    /// beside it, and the routers leave a gap sized for one line. It goes
    /// against the middle of the link, unless that would put it on a box.
    /// </summary>
    private static void PutLabelBeside(Diagram diagram, DiagramEdge link)
    {
        if (ReferenceEquals(link.Source, link.Target) || link.Waypoints.Count < 2 || link.Label is not { } text)
            return;

        var (middleX, middleY) = PointAt(link.Waypoints, Lengths(link.Waypoints), Lengths(link.Waypoints)[^1] / 2);
        var mostlyDown = Math.Abs(link.Waypoints[^1].Y - link.Waypoints[0].Y) > Math.Abs(link.Waypoints[^1].X - link.Waypoints[0].X);
        var beside = mostlyDown
            ? (middleX + (LabelWidth(text) / 2) + 2, middleY)
            : (middleX, middleY - (LabelHeightOf(text) / 2) - 4);

        var box = LabelBox(text, beside);
        if (!diagram.Nodes.Exists(n => box.Overlaps(new Box(n.X, n.Y, n.X + n.Width, n.Y + n.Height))))
            link.LabelCentre = beside;
    }

    /// <summary>An edge from a node to itself: out of the right side and back.</summary>
    private static void DrawLoop(DiagramEdge edge)
    {
        var node = edge.Source;
        var right = node.X + node.Width;
        var top = node.Y + (node.Height / 2) - (LoopHeight / 2);

        edge.Waypoints.Clear();
        edge.Waypoints.AddRange([(right, top), (right + LoopReach, top), (right + LoopReach, top + LoopHeight), (right, top + LoopHeight)]);
        edge.LabelCentre = edge.Label is { Length: > 0 } text ? (right + LoopReach + (LabelWidth(text) / 2) + 2, top + (LoopHeight / 2)) : null;
    }

    /// <summary>
    /// A transition that leaves its level, from inside a composite state to
    /// outside it, is drawn straight. Levels are routed apart, so nothing has
    /// found a way round for this one.
    /// </summary>
    private static void DrawAcrossLevels(DiagramEdge edge)
    {
        var (sx, sy) = (edge.Source.X + (edge.Source.Width / 2), edge.Source.Y + (edge.Source.Height / 2));
        var (tx, ty) = (edge.Target.X + (edge.Target.Width / 2), edge.Target.Y + (edge.Target.Height / 2));

        edge.Waypoints.Clear();
        edge.Waypoints.AddRange([ToBorder(edge.Source, tx, ty), ToBorder(edge.Target, sx, sy)]);
        edge.LabelCentre = edge.Label is null ? null : ((edge.Waypoints[0].X + edge.Waypoints[1].X) / 2, ((edge.Waypoints[0].Y + edge.Waypoints[1].Y) / 2) - 9);
    }

    private static (double X, double Y) ToBorder(DiagramNode node, double towardsX, double towardsY)
    {
        var (cx, cy) = (node.X + (node.Width / 2), node.Y + (node.Height / 2));
        var (dx, dy) = (towardsX - cx, towardsY - cy);
        if (Math.Abs(dx) < 0.001 && Math.Abs(dy) < 0.001)
            return (cx, cy);

        var scale = Math.Min(Math.Abs(dx) < 0.001 ? double.MaxValue : node.Width / 2 / Math.Abs(dx),
            Math.Abs(dy) < 0.001 ? double.MaxValue : node.Height / 2 / Math.Abs(dy));
        return (cx + (dx * scale), cy + (dy * scale));
    }

    /// <summary>A link's messages, one to a line, each with an arrow for the way it goes on the picture as it stands.</summary>
    internal static string LinkLabel(DiagramEdge link)
        => string.Join("\n", link.Messages.Select(m => $"{m.Number}: {m.Text} {Pointing(link, m)}"));

    private static string Pointing(DiagramEdge link, LinkMessage message)
    {
        if (ReferenceEquals(link.Source, link.Target))
            return "↺";

        var from = ReferenceEquals(message.From, link.Source) ? link.Source : link.Target;
        var to = ReferenceEquals(from, link.Source) ? link.Target : link.Source;
        var dx = to.X + (to.Width / 2) - from.X - (from.Width / 2);
        var dy = to.Y + (to.Height / 2) - from.Y - (from.Height / 2);
        if (Math.Abs(dx) >= Math.Abs(dy))
            return dx >= 0 ? "→" : "←";
        return dy >= 0 ? "↓" : "↑";
    }

    // ----- nesting ------------------------------------------------------------

    private static bool IsInside(DiagramNode node, DiagramNode container)
    {
        for (var c = node.Container; c is not null; c = c.Container)
        {
            if (ReferenceEquals(c, container))
                return true;
        }

        return false;
    }

    /// <summary>Moves everything inside a container, and the routes between what is inside it.</summary>
    private static void ShiftInside(Diagram diagram, DiagramNode container, double dx, double dy)
    {
        foreach (var node in diagram.Nodes.Where(n => IsInside(n, container)))
        {
            node.X += dx;
            node.Y += dy;
        }

        foreach (var edge in diagram.Edges.Where(e => IsInside(e.Source, container) && IsInside(e.Target, container)))
        {
            for (var i = 0; i < edge.Waypoints.Count; i++)
                edge.Waypoints[i] = (edge.Waypoints[i].X + dx, edge.Waypoints[i].Y + dy);
            if (edge.LabelCentre is { } label)
                edge.LabelCentre = (label.X + dx, label.Y + dy);
        }
    }

    private static Diagram ScopeOf(Diagram diagram, List<DiagramNode> level)
    {
        var members = level.ToHashSet();
        var scope = new Diagram(diagram.Kind, diagram.Root) { IsScope = true };
        scope.Nodes.AddRange(level);
        scope.Edges.AddRange(diagram.Edges.Where(e => !ReferenceEquals(e.Source, e.Target) && members.Contains(e.Source) && members.Contains(e.Target)));
        return scope;
    }

    /// <summary>Grows a container around what was dragged out of it. A bottom level first, so an outer one sees the inner one's new size.</summary>
    private static void FitContainers(Diagram diagram)
    {
        var containers = diagram.Nodes.Where(n => diagram.Nodes.Exists(c => ReferenceEquals(c.Container, n))).ToList();
        foreach (var container in containers.OrderByDescending(Depth))
        {
            var inside = diagram.Nodes.Where(n => ReferenceEquals(n.Container, container)).ToList();
            var top = inside.Min(n => n.Y) - (container.Notation == NodeNotation.Boundary ? NodeMarks.HeaderOf(container) : NodeMarks.HeaderOf(container) + 6);
            var left = Math.Min(container.X, inside.Min(n => n.X) - ContainerPadding);
            var right = Math.Max(container.X + container.Width, inside.Max(n => n.X + n.Width) + ContainerPadding);
            var upper = Math.Min(container.Y, top);
            var bottom = Math.Max(container.Y + container.Height, inside.Max(n => n.Y + n.Height) + ContainerPadding);
            (container.X, container.Y, container.Width, container.Height) = (left, upper, right - left, bottom - upper);
        }
    }

    private static int Depth(DiagramNode node)
    {
        var depth = 0;
        for (var c = node.Container; c is not null; c = c.Container)
            depth++;
        return depth;
    }

    // ----- states -------------------------------------------------------------

    private static void ApplyStates(Diagram diagram, bool measure)
    {
        if (measure)
        {
            foreach (var node in diagram.Nodes)
                Measure(node);
        }

        LayOutLevel(diagram, container: null);
        FinishEdges(diagram);
    }

    /// <summary>
    /// Lays out one level at the origin, its inner levels first: a composite
    /// state's size is what its states need. Each level's coordinates are its
    /// container's until the container itself has been placed.
    /// </summary>
    private static void LayOutLevel(Diagram diagram, DiagramNode? container)
    {
        var level = diagram.Nodes.Where(n => ReferenceEquals(n.Container, container)).ToList();
        foreach (var composite in level.Where(n => n.Notation == NodeNotation.CompositeState))
            LayOutLevel(diagram, composite);

        if (container?.Element.IsParallel == true)
            PlaceRegions(level);
        else
            Apply(ScopeOf(diagram, level), measure: false);

        foreach (var composite in level.Where(n => n.Notation == NodeNotation.CompositeState))
            ShiftInside(diagram, composite, composite.X, composite.Y);

        if (container is not null && level.Count > 0)
            FitAround(diagram, container, level);
    }

    /// <summary>A parallel state's substates run at once, so they stand side by side, each a region of its own.</summary>
    private static void PlaceRegions(List<DiagramNode> regions)
    {
        var x = 0.0;
        foreach (var region in regions)
        {
            region.X = x;
            region.Y = 0;
            x += region.Width + (ContainerPadding * 2);
        }
    }

    private static void FitAround(Diagram diagram, DiagramNode container, List<DiagramNode> level)
    {
        var minX = level.Min(n => n.X);
        var minY = level.Min(n => n.Y);
        var header = NodeMarks.HeaderOf(container) + 6;
        var dx = ContainerPadding - minX;
        var dy = header - minY;

        foreach (var node in level)
        {
            node.X += dx;
            node.Y += dy;
        }

        foreach (var node in level.Where(n => n.Notation == NodeNotation.CompositeState))
            ShiftInside(diagram, node, dx, dy);

        foreach (var edge in diagram.Edges.Where(e => level.Contains(e.Source) && level.Contains(e.Target)))
        {
            for (var i = 0; i < edge.Waypoints.Count; i++)
                edge.Waypoints[i] = (edge.Waypoints[i].X + dx, edge.Waypoints[i].Y + dy);
            if (edge.LabelCentre is { } label)
                edge.LabelCentre = (label.X + dx, label.Y + dy);
        }

        var titleWidth = Math.Max(NodeMarks.TextWidth(container.Label, 15), container.Features.Count == 0 ? 0 : container.Features.Max(f => NodeMarks.TextWidth(f, 12)));
        container.Width = Math.Max(titleWidth + 40, level.Max(n => n.X + n.Width) + ContainerPadding);
        container.Height = level.Max(n => n.Y + n.Height) + ContainerPadding;

        container.Ticks.Clear();
        if (container.Element.IsParallel)
        {
            foreach (var region in level.OrderBy(n => n.X).Skip(1))
                container.Ticks.Add(region.X - ContainerPadding);
        }
    }

    private static void RouteStates(Diagram diagram)
    {
        FitContainers(diagram);
        Route(ScopeOf(diagram, [.. diagram.Nodes.Where(n => n.Container is null)]));
        foreach (var composite in diagram.Nodes.Where(n => n.Notation == NodeNotation.CompositeState))
            Route(ScopeOf(diagram, [.. diagram.Nodes.Where(n => ReferenceEquals(n.Container, composite))]));

        FinishEdges(diagram);
    }

    // ----- use cases ----------------------------------------------------------

    /// <summary>
    /// Use cases grouped by their boundary, each group laid out by what it
    /// includes, the groups stacked; actors in a column to the left, each as
    /// near as it can be to the use cases it is linked to.
    /// </summary>
    private static void ApplyUseCase(Diagram diagram, bool measure)
    {
        if (measure)
        {
            foreach (var node in diagram.Nodes)
                Measure(node);
        }

        var actors = diagram.Nodes.Where(n => n.Notation == NodeNotation.Actor).ToList();
        var column = actors.Count == 0 ? 0 : actors.Max(a => a.Width) + ActorGap;

        var y = 0.0;
        foreach (var group in diagram.Nodes.Where(n => n.Notation == NodeNotation.UseCase).GroupBy(n => n.Container).ToList())
        {
            var cases = group.ToList();
            Apply(ScopeOf(diagram, cases), measure: false);

            var boundary = group.Key;
            var pad = boundary is null ? 0 : ContainerPadding;
            var top = boundary is null ? 0 : NodeMarks.HeaderOf(boundary);
            var minX = cases.Min(n => n.X);
            var minY = cases.Min(n => n.Y);
            foreach (var node in cases)
            {
                node.X += column + pad - minX;
                node.Y += y + top - minY;
            }

            var bottom = cases.Max(n => n.Y + n.Height);
            if (boundary is not null)
            {
                boundary.X = column;
                boundary.Y = y;
                boundary.Width = Math.Max(cases.Max(n => n.X + n.Width) + pad - column, NodeMarks.TextWidth(boundary.Label, 14) + 40);
                boundary.Height = bottom + pad - y;
                bottom = boundary.Y + boundary.Height;
            }

            y = bottom + GroupGap;
        }

        PlaceActors(diagram, actors);
        RouteUseCase(diagram);
    }

    private static void PlaceActors(Diagram diagram, List<DiagramNode> actors)
    {
        if (actors.Count == 0)
            return;

        var column = actors.Max(a => a.Width);
        var wanted = actors.ToDictionary(a => a, a =>
        {
            var linked = diagram.Edges.Where(e => ReferenceEquals(e.Source, a)).Select(e => e.Target).ToList();
            return linked.Count == 0 ? 0 : linked.Average(c => c.Y + (c.Height / 2));
        });

        var y = 0.0;
        foreach (var actor in actors.OrderBy(a => wanted[a]))
        {
            actor.X = (column - actor.Width) / 2;
            actor.Y = Math.Max(y, wanted[actor] - (actor.Height / 2));
            y = actor.Y + actor.Height + 28;
        }
    }

    private static void RouteUseCase(Diagram diagram)
    {
        FitContainers(diagram);

        // A boundary is only ink: associations cross it to get to what is inside.
        var scope = new Diagram(diagram.Kind, diagram.Root) { IsScope = true };
        scope.Nodes.AddRange(diagram.Nodes.Where(n => n.Notation != NodeNotation.Boundary));
        scope.Edges.AddRange(diagram.Edges.Where(e => !ReferenceEquals(e.Source, e.Target)));
        Route(scope);
        FinishEdges(diagram);
    }
}
