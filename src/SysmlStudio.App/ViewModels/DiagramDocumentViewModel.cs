using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysmlStudio.Diagrams;
using SysmlStudio.Model;

namespace SysmlStudio.App.ViewModels;

/// <summary>One box on the canvas.</summary>
public sealed partial class DiagramNodeViewModel(DiagramNode node) : ObservableObject
{
    private readonly DiagramNode _node = node;

    [ObservableProperty]
    public partial Point Location { get; set; } = new(node.X, node.Y);

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public Element Element => _node.Element;
    public string Title => _node.Label;
    public bool IsPseudoNode => _node.IsPseudo;

    /// <summary>The line over the title: "requirement · S.1", "part · 1..8".</summary>
    public string KindCaption
    {
        get
        {
            var e = _node.Element;
            var extra = e.ShortName is { Length: > 0 } s ? s : e.Multiplicity?.Trim('[', ']');
            return extra is { Length: > 0 } ? $"{e.Kind} · {extra}" : e.Kind;
        }
    }

    /// <summary>A requirement's text, under its title; other boxes list their features instead.</summary>
    public string? Documentation => _node.Element.Kind.Contains("requirement", StringComparison.Ordinal)
        ? _node.Element.Documentation
        : null;

    public bool HasDocumentation => !string.IsNullOrWhiteSpace(Documentation);
    public ObservableCollection<string> Features { get; } = new(node.Features);
    /// <summary>A requirement's only feature is its text, which the box already shows as its description.</summary>
    public bool HasFeatures => Features.Count > 0 && !HasDocumentation;
    public double Width => _node.Width;
    public double Height => _node.Height;

    public string Maturity => _node.Maturity ?? "none";

    public bool IsPseudo => _node.IsPseudo && !IsUml;
    public bool IsBox => !_node.IsPseudo && !IsUml;
    public bool IsStart => _node.Pseudo == "start";
    public bool IsDone => _node.Pseudo == "done";

    // ----- UML's shapes ---------------------------------------------------------

    public bool IsUml => _node.Shape != NodeShape.Box;
    public bool IsFolder => _node.Shape == NodeShape.Folder;
    public bool IsDevice => _node.Shape == NodeShape.Device;
    public bool IsComponent => _node.Shape == NodeShape.Component;
    public bool IsArtifact => _node.Shape == NodeShape.Artifact;

    /// <summary>What is shown above the name: "«stereotype»".</summary>
    public string? Stereotype => _node.Stereotype;
    public bool HasStereotype => !string.IsNullOrEmpty(_node.Stereotype);

    /// <summary>A class's operations, in a compartment of their own.</summary>
    public IReadOnlyList<string> Operations => _node.Operations;
    public bool HasOperations => _node.Operations.Count > 0;

    /// <summary>The first compartment under the name, in UML's shapes.</summary>
    public bool HasUmlFeatures => Features.Count > 0;

    /// <summary>An object's name is underlined, which is all that tells it from a class.</summary>
    public TextDecorationCollection? TitleDecorations => _node.Shape == NodeShape.Instance ? TextDecorations.Underline : null;

    /// <summary>Anything abstract has its name in italics.</summary>
    public FontStyle TitleStyle => _node.IsAbstract ? FontStyle.Italic : FontStyle.Normal;

    /// <summary>A reference has a dashed border.</summary>
    public bool IsReference => _node.IsReference;

    /// <summary>Where the body sits in the box, leaving room for a folder's tab or a node's depth.</summary>
    public Thickness BodyMargin => _node.Shape switch
    {
        NodeShape.Folder => new Thickness(0, DiagramLayout.FolderTab, 0, 0),
        NodeShape.Device => new Thickness(0, DiagramLayout.NodeDepth, DiagramLayout.NodeDepth, 0),
        _ => default,
    };

    /// <summary>Room inside the top and bottom border for the names of the ports.</summary>
    public Thickness PortBands => _node.Ports.Count > 0 ? new Thickness(0, DiagramLayout.PortBand, 0, DiagramLayout.PortBand) : default;

    /// <summary>The corner icon sits under the band that holds the names of the ports.</summary>
    public Thickness IconMargin => new(0, _node.Ports.Count > 0 ? DiagramLayout.PortBand + 4 : 10, 10, 0);

    public double TabWidth => DiagramLayout.FolderTabWidth(Width);

    /// <summary>A node's depth: the top and right faces of the box, behind the front of it.</summary>
    public Geometry DepthFaces
    {
        get
        {
            const double d = DiagramLayout.NodeDepth;
            var geometry = new StreamGeometry();
            using var context = geometry.Open();
            context.BeginFigure(new Point(0, d), isFilled: true);
            context.LineTo(new Point(d, 0));
            context.LineTo(new Point(Width, 0));
            context.LineTo(new Point(Width, Height - d));
            context.LineTo(new Point(Width - d, Height));
            context.LineTo(new Point(Width - d, d));
            context.EndFigure(isClosed: true);
            context.BeginFigure(new Point(Width - d, d), isFilled: false);
            context.LineTo(new Point(Width, 0));
            context.EndFigure(isClosed: false);
            return geometry;
        }
    }

    /// <summary>The ports on the box's border.</summary>
    public IReadOnlyList<DiagramPortViewModel> Ports { get; } = node.Ports.ConvertAll(p => new DiagramPortViewModel(p));

    /// <summary>The layout moved the ports along the border.</summary>
    public void RefreshPorts()
    {
        foreach (var port in Ports)
            port.Refresh();
    }

    /// <summary>Where a connection aims: the node's centre.</summary>
    public Point Anchor => new(Location.X + (Width / 2), Location.Y + (Height / 2));

    /// <summary>Half the node's size; connections are cut back by it so heads land on the border.</summary>
    public Size HalfSize => new(Width / 2, Height / 2);

    partial void OnLocationChanged(Point value)
    {
        _node.X = value.X;
        _node.Y = value.Y;
        OnPropertyChanged(nameof(Anchor));
    }

    /// <summary>The box as drawn, where it differs from the estimate the layout started from.</summary>
    public void Resize(double width, double height)
    {
        _node.Width = width;
        _node.Height = height;
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
        OnPropertyChanged(nameof(DepthFaces));
        OnPropertyChanged(nameof(TabWidth));
        OnPropertyChanged(nameof(HalfSize));
        OnPropertyChanged(nameof(Anchor));
    }

    /// <summary>Takes the position the layout gave the node.</summary>
    public void SyncFromLayout() => Location = new Point(_node.X, _node.Y);

    /// <summary>The box's outline, for cutting a straight line back to its border.</summary>
    public Rect Bounds => new(Location.X, Location.Y, Width, Height);
}

/// <summary>A port on a box's border: a square, its ball or socket, and its name in the band inside the box.</summary>
public sealed class DiagramPortViewModel(DiagramPort port) : ObservableObject
{
    private const double HalfSquare = 5;
    private const double Stick = 7;
    private const double Radius = 5;

    public string Name => port.Name;
    public double SquareLeft => port.X - HalfSquare;
    public double SquareTop => port.Y - HalfSquare;
    public double LabelWidth => port.LabelWidth;
    public double LabelLeft => port.X - (port.LabelWidth / 2);
    public double LabelTop => port.OnTop ? port.Y + 7 : port.Y - 7 - 15;

    /// <summary>A stick out of the square, ending in a ball where the port provides and a socket where it requires.</summary>
    public Geometry? Glyph
    {
        get
        {
            if (port.Flow == PortFlow.None)
                return null;

            var outward = port.OnTop ? -1 : 1;
            var from = new Point(port.X, port.Y + (outward * HalfSquare));
            var end = from + new Vector(0, outward * Stick);
            var group = new GeometryGroup();
            group.Children.Add(new LineGeometry { StartPoint = from, EndPoint = end });
            if (port.Flow == PortFlow.Provided)
            {
                var centre = end + new Vector(0, outward * Radius);
                group.Children.Add(new EllipseGeometry(new Rect(centre.X - Radius, centre.Y - Radius, Radius * 2, Radius * 2)));
                return group;
            }

            // The socket's cup opens away from the box.
            var cup = new StreamGeometry();
            using (var context = cup.Open())
            {
                var rim = end.Y + (outward * (Radius + 1));
                context.BeginFigure(new Point(port.X - Radius - 1, rim), isFilled: false);
                context.ArcTo(new Point(port.X + Radius + 1, rim), new Size(Radius + 1, Radius + 1), 0, false, port.OnTop ? SweepDirection.CounterClockwise : SweepDirection.Clockwise);
                context.EndFigure(isClosed: false);
            }

            group.Children.Add(cup);
            return group;
        }
    }

    public void Refresh() => OnPropertyChanged(string.Empty);
}

/// <summary>The frame around a whole diagram, and what it says in its corner.</summary>
public sealed partial class DiagramFrameViewModel(string label) : ObservableObject
{
    public string Label { get; } = label;

    [ObservableProperty]
    public partial Point Location { get; set; }

    /// <summary>Its size in the diagram's coordinates.</summary>
    public Size Size { get; private set; }

    /// <summary>The canvas places a decorator by the zoom but draws it at its own size, so the size is scaled here.</summary>
    [ObservableProperty]
    public partial double Width { get; set; }

    [ObservableProperty]
    public partial double Height { get; set; }

    public void Resize(Size size, double zoom)
    {
        Size = size;
        Width = size.Width * zoom;
        Height = size.Height * zoom;
    }
}

/// <summary>
/// One arrow on the canvas. It is drawn along the route MSAGL found around the
/// boxes; while one of its boxes is being dragged the route is out of date, so
/// it is drawn straight from border to border until the drop re-routes it.
/// </summary>
public sealed class DiagramConnectionViewModel : ObservableObject, IDisposable
{
    private readonly DiagramEdge _edge;
    private bool _routeIsCurrent = true;

    public DiagramConnectionViewModel(DiagramEdge edge, DiagramNodeViewModel source, DiagramNodeViewModel target)
    {
        _edge = edge;
        Source = source;
        Target = target;
        source.PropertyChanged += OnEndMoved;
        target.PropertyChanged += OnEndMoved;
    }

    public DiagramNodeViewModel Source { get; }
    public DiagramNodeViewModel Target { get; }
    public string? Label => _edge.Label;
    public bool HasLabel => !string.IsNullOrWhiteSpace(_edge.Label);

    /// <summary>Whether the label is drawn; the diagram turns labels off when it is crowded.</summary>
    public bool ShowsLabel { get; set; }

    public RelationKind Kind => _edge.Kind;

    /// <summary>The tooltip: the relation's name, or what it joins, "tick → irq.timer".</summary>
    public string Description => _edge.Name ?? (_edge.Kind is RelationKind.Connect or RelationKind.Flow or RelationKind.Interface
        ? $"{Source.Element.Name} → {Target.Element.Name}"
        : $"{Source.Title} {_edge.Label ?? _edge.Kind.ToString().ToLowerInvariant()} {Target.Title}");

    /// <summary>Trace relations are drawn dashed, the way UML draws a dependency.</summary>
    public bool IsDashed => Kind is RelationKind.Satisfy or RelationKind.Verify
        or RelationKind.Allocate or RelationKind.Dependency or RelationKind.Typing or RelationKind.Flow or RelationKind.Import;

    /// <summary>Specialization and redefinition get UML's hollow triangle.</summary>
    public bool IsGeneralization => Kind is RelationKind.Specialization or RelationKind.Redefinition;

    /// <summary>A stereotype's extension gets UML's filled triangle, as big as the hollow one.</summary>
    public bool IsExtension => Kind == RelationKind.Extension;

    /// <summary>Composition is marked with a filled diamond at the owner's end.</summary>
    public bool IsComposition => Kind == RelationKind.Composition;

    /// <summary>An open arrowhead: the trace relations.</summary>
    public bool IsOpenHead => IsDashed || Kind == RelationKind.Association;

    /// <summary>A filled arrowhead: everything else.</summary>
    public bool IsFilledHead => !IsGeneralization && !IsOpenHead;

    /// <summary>A connection joins two ends as equals: no arrowhead, a small circle at each end instead.</summary>
    public bool IsConnector => Kind is RelationKind.Connect or RelationKind.Interface;

    /// <summary>The points the line passes through, in canvas coordinates.</summary>
    private IReadOnlyList<Point> Points
    {
        get
        {
            if (_routeIsCurrent && _edge.Waypoints.Count >= 2)
                return _edge.Waypoints.ConvertAll(p => new Point(p.X, p.Y));

            var from = Source.Anchor;
            var to = Target.Anchor;
            return [Clip(Source.Bounds, to, from), Clip(Target.Bounds, from, to)];
        }
    }

    public Geometry Line
    {
        get
        {
            var points = Points;
            var geometry = new StreamGeometry();
            using var context = geometry.Open();
            context.BeginFigure(points[0], isFilled: false);
            for (var i = 1; i < points.Count; i++)
                context.LineTo(points[i]);
            context.EndFigure(isClosed: false);
            return geometry;
        }
    }

    /// <summary>The arrowhead at the target: a triangle, hollow or filled, or an open V.</summary>
    public Geometry? Head
    {
        get
        {
            if (IsConnector || _edge.IsHeadless || Kind is RelationKind.Containment or RelationKind.Link)
                return null;

            var points = Points;
            var tip = points[^1];
            var direction = Direction(points[points.Count - 2], tip);
            var size = IsGeneralization || IsExtension ? 13.0 : 10.0;
            var width = IsGeneralization || IsExtension ? 7.0 : 5.0;
            var back = tip - (direction * size);
            var normal = new Vector(-direction.Y, direction.X) * width;

            var geometry = new StreamGeometry();
            using var context = geometry.Open();
            context.BeginFigure(back + normal, isFilled: !IsOpenHead);
            context.LineTo(tip);
            context.LineTo(back - normal);
            context.EndFigure(isClosed: !IsOpenHead);
            return geometry;
        }
    }

    /// <summary>The composition diamond at the owner's end; empty for every other relation.</summary>
    public Geometry? Tail
    {
        get
        {
            if (!IsComposition)
                return null;

            var points = Points;
            var start = points[0];
            var direction = Direction(points[1], start);
            var normal = new Vector(-direction.Y, direction.X) * 5;
            var middle = start - (direction * 8);

            var geometry = new StreamGeometry();
            using var context = geometry.Open();
            context.BeginFigure(start, isFilled: true);
            context.LineTo(middle + normal);
            context.LineTo(start - (direction * 16));
            context.LineTo(middle - normal);
            context.EndFigure(isClosed: true);
            return geometry;
        }
    }

    /// <summary>The crossed circle at the owner's end of a package's nesting; empty for every other relation.</summary>
    public Geometry? Anchor
    {
        get
        {
            if (Kind != RelationKind.Containment)
                return null;

            const double radius = 6;
            var points = Points;
            var direction = Direction(points[1], points[0]);
            var centre = points[0] - (direction * radius);
            var normal = new Vector(-direction.Y, direction.X) * radius;
            var along = direction * radius;

            var group = new GeometryGroup();
            group.Children.Add(new EllipseGeometry(new Rect(centre.X - radius, centre.Y - radius, radius * 2, radius * 2)));
            group.Children.Add(new LineGeometry { StartPoint = centre - normal, EndPoint = centre + normal });
            group.Children.Add(new LineGeometry { StartPoint = centre - along, EndPoint = centre + along });
            return group;
        }
    }

    /// <summary>The circles where a connection or flow meets its parts; empty for every other relation, and where a port is drawn instead.</summary>
    public Geometry? Ports
    {
        get
        {
            if (!IsConnector && Kind != RelationKind.Flow)
                return null;

            var points = Points;
            var group = new GeometryGroup();
            if (_edge.SourcePort is null)
                group.Children.Add(new EllipseGeometry(new Rect(points[0].X - 3.5, points[0].Y - 3.5, 7, 7)));
            if (IsConnector && _edge.TargetPort is null)
                group.Children.Add(new EllipseGeometry(new Rect(points[^1].X - 3.5, points[^1].Y - 3.5, 7, 7)));
            return group.Children.Count == 0 ? null : group;
        }
    }

    /// <summary>The label's top left: where MSAGL placed it, or beside the middle of the line.</summary>
    public Point LabelLocation
    {
        get
        {
            var width = ((_edge.Label?.Length ?? 0) * 6.7) + 8;
            if (_routeIsCurrent && _edge.LabelCentre is { } centre)
                return new Point(centre.X - (width / 2), centre.Y - 8);

            var points = Points;
            var middle = points[points.Count / 2];
            return new Point(middle.X + 6, middle.Y - 16);
        }
    }

    /// <summary>The route was recomputed: draw along it again.</summary>
    public void Rerouted()
    {
        _routeIsCurrent = true;
        Refresh();
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(Line));
        OnPropertyChanged(nameof(Head));
        OnPropertyChanged(nameof(Tail));
        OnPropertyChanged(nameof(Anchor));
        OnPropertyChanged(nameof(Ports));
        OnPropertyChanged(nameof(LabelLocation));
    }

    public void Dispose()
    {
        Source.PropertyChanged -= OnEndMoved;
        Target.PropertyChanged -= OnEndMoved;
    }

    private void OnEndMoved(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(DiagramNodeViewModel.Anchor))
            return;

        _routeIsCurrent = false;
        Refresh();
    }

    private static Vector Direction(Point from, Point to)
    {
        var vector = to - from;
        var length = Math.Sqrt((vector.X * vector.X) + (vector.Y * vector.Y));
        return length < 0.001 ? new Vector(1, 0) : vector / length;
    }

    /// <summary>Where the line from <paramref name="outside"/> to the box's centre crosses its border.</summary>
    private static Point Clip(Rect box, Point outside, Point centre)
    {
        var dx = outside.X - centre.X;
        var dy = outside.Y - centre.Y;
        if (Math.Abs(dx) < 0.001 && Math.Abs(dy) < 0.001)
            return centre;

        var scaleX = Math.Abs(dx) < 0.001 ? double.MaxValue : box.Width / 2 / Math.Abs(dx);
        var scaleY = Math.Abs(dy) < 0.001 ? double.MaxValue : box.Height / 2 / Math.Abs(dy);
        var scale = Math.Min(Math.Min(scaleX, scaleY), 1);
        return new Point(centre.X + (dx * scale), centre.Y + (dy * scale));
    }
}

/// <summary>A diagram open as a document tab.</summary>
public sealed partial class DiagramDocumentViewModel : DocumentViewModel
{
    public DiagramDocumentViewModel(Diagram diagram, bool laidOut = false)
    {
        Diagram = diagram;
        _restored = laidOut;
        if (!laidOut)
        {
            DiagramLayout.Apply(diagram);
        }
        else
        {
            // Positions came from the sidecar: route around them where they are.
            DiagramLayout.RemoveOverlaps(diagram);
            DiagramLayout.Route(diagram);
        }

        Id = $"diagram:{diagram.Kind}:{diagram.Root.QualifiedName}";
        Title = diagram.Root.DisplayName;
        ToolTip = $"{diagram.Root.QualifiedName} · {KindName(diagram.Kind)}";

        var nodes = diagram.Nodes.ConvertAll(n => new DiagramNodeViewModel(n));
        Nodes = new ObservableCollection<DiagramNodeViewModel>(nodes);

        var byNode = new Dictionary<DiagramNode, DiagramNodeViewModel>();
        for (var i = 0; i < diagram.Nodes.Count; i++)
            byNode[diagram.Nodes[i]] = nodes[i];

        Connections = new ObservableCollection<DiagramConnectionViewModel>(
            diagram.Edges.ConvertAll(e => new DiagramConnectionViewModel(e, byNode[e.Source], byNode[e.Target])));

        foreach (var node in nodes)
        {
            node.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DiagramNodeViewModel.IsSelected) && node.IsSelected && !node.IsPseudoNode)
                    ElementClicked?.Invoke(node.Element);
                else if (e.PropertyName == nameof(DiagramNodeViewModel.Anchor))
                    RefreshFrame();
            };
        }

        if (diagram.FrameLabel is { } frameLabel)
            Frames.Add(new DiagramFrameViewModel(frameLabel));
        RefreshFrame();

        // Past two dozen edges the labels crowd each other; the tooltip still names each one.
        var labelled = Connections.Count <= 24;
        foreach (var connection in Connections)
            connection.ShowsLabel = labelled && connection.HasLabel;
    }

    public Diagram Diagram { get; }

    /// <summary>Told when a box on the canvas is selected; the window selects its element everywhere.</summary>
    public Action<Element>? ElementClicked { get; init; }

    public ObservableCollection<DiagramNodeViewModel> Nodes { get; }
    public ObservableCollection<DiagramConnectionViewModel> Connections { get; }

    /// <summary>What is drawn over the canvas: the frame round the picture, where the kind has one.</summary>
    public ObservableCollection<DiagramFrameViewModel> Frames { get; } = [];

    private void RefreshFrame()
    {
        if (Frames.Count == 0 || Diagram.FrameBounds() is not { } bounds)
            return;

        var frame = Frames[0];
        frame.Location = new Point(bounds.X, bounds.Y);
        frame.Resize(new Size(bounds.Width, bounds.Height), Zoom);
    }

    /// <summary>The routes changed: draw the arrows, the ports they meet and the frame around them again.</summary>
    private void Redraw()
    {
        foreach (var node in Nodes)
            node.RefreshPorts();
        foreach (var connection in Connections)
            connection.Rerouted();
        RefreshFrame();
    }

    /// <summary>After a drag, every edge is routed again around the boxes where they now stand.</summary>
    [RelayCommand]
    private void DragCompleted()
    {
        PushPositions();
        DiagramLayout.Route(Diagram);
        Redraw();
    }

    private readonly bool _restored;
    private bool _measured;

    /// <summary>
    /// The view has drawn the boxes and knows their real sizes. Where they are
    /// bigger than the layout's estimate the diagram is laid out again with the
    /// real sizes, or, for positions the user left in the sidecar, pushed apart
    /// just enough; either way the edges are routed again. Runs once.
    /// </summary>
    public bool ApplyMeasuredSizes(IReadOnlyDictionary<DiagramNodeViewModel, Size> sizes)
    {
        if (_measured)
            return false;
        _measured = true;

        var changed = false;
        foreach (var (node, size) in sizes)
        {
            if (node.IsPseudo || (Math.Abs(size.Height - node.Height) < 3 && Math.Abs(size.Width - node.Width) < 3))
                continue;
            node.Resize(Math.Max(size.Width, node.Width), Math.Max(size.Height, node.Height));
            changed = true;
        }

        if (!changed)
            return false;

        PushPositions();
        if (_restored)
        {
            DiagramLayout.RemoveOverlaps(Diagram);
            DiagramLayout.Route(Diagram);
        }
        else
        {
            DiagramLayout.Apply(Diagram, measure: false);
        }

        foreach (var node in Nodes)
            node.SyncFromLayout();
        Redraw();
        return true;
    }

    /// <summary>What the floating toolbar adds to, or draws on, this kind of diagram.</summary>
    public IReadOnlyList<ToolboxItem> Toolbox => _toolbox ??= Diagram.Kind switch
    {
        DiagramKind.Interconnection => [new("Part", "part"), new("Port", "port"), new("Connect", relation: "Connect"), new("Flow", relation: "Flow")],
        DiagramKind.Requirements => [new("Requirement", "requirement"), new("Satisfy", relation: "Satisfy"), new("Depends", relation: "Dependency"), new("Allocate", relation: "Allocate")],
        DiagramKind.ActionFlow => [new("Action", "action"), new("Succession", relation: "Succession")],
        DiagramKind.StateMachine => [new("State", "state"), new("Transition", relation: "Transition")],
        DiagramKind.Package => [new("Package", "package")],
        DiagramKind.Class => [new("Part def", "part def"), new("Attribute def", "attribute def"), new("Specializes", relation: "Specialization"), new("Owns", relation: "Composition")],
        DiagramKind.ObjectDiagram => [],
        DiagramKind.Component => [new("Part def", "part def")],
        DiagramKind.CompositeStructure => [new("Part", "part"), new("Port", "port"), new("Connect", relation: "Connect"), new("Flow", relation: "Flow")],
        DiagramKind.Deployment => [new("Allocate", relation: "Allocate")],
        DiagramKind.Profile => [],
        _ => [new("Part def", "part def"), new("Part", "part"), new("Port def", "port def"), new("Specializes", relation: "Specialization"), new("Owns", relation: "Composition")],
    };

    private IReadOnlyList<ToolboxItem>? _toolbox;

    /// <summary>True while no relation is being drawn: the pointer is the active tool.</summary>
    [ObservableProperty]
    public partial bool IsPointer { get; set; } = true;

    /// <summary>Marks the toolbar button of the relation being drawn, or the pointer when none is.</summary>
    public void ShowActiveTool(string? relation)
    {
        foreach (var item in Toolbox)
            item.IsActive = relation is not null && item.Relation == relation;
        IsPointer = relation is null;
    }

    [ObservableProperty]
    public partial double Zoom { get; set; } = 1;

    public string ZoomText => $"{Math.Round(Zoom * 100)}%";

    /// <summary>Set by the view: frames every node.</summary>
    public Action? FitRequested { get; set; }

    /// <summary>Set by the view: renders the canvas to a PNG file.</summary>
    public Action<string>? RenderPng { get; set; }

    partial void OnZoomChanged(double value)
    {
        OnPropertyChanged(nameof(ZoomText));
        RefreshFrame();
    }

    [RelayCommand]
    private void ZoomIn() => Zoom = Math.Min(4, Zoom * 1.2);

    [RelayCommand]
    private void ZoomOut() => Zoom = Math.Max(0.1, Zoom / 1.2);

    [RelayCommand]
    public void Fit() => FitRequested?.Invoke();

    /// <summary>Copies wherever the nodes were dragged to back into the diagram.</summary>
    public void PushPositions()
    {
        foreach (var node in Nodes)
        {
            if (!node.IsPseudoNode && Diagram.NodeFor(node.Element) is { } target)
            {
                target.X = node.Location.X;
                target.Y = node.Location.Y;
            }
        }
    }

    /// <summary>Lays the diagram out again, discarding wherever nodes were dragged to.</summary>
    [RelayCommand]
    public void Relayout()
    {
        DiagramLayout.Apply(Diagram, measure: !_measured);
        foreach (var node in Nodes)
            node.SyncFromLayout();
        Redraw();

        Fit();
    }

    public static string KindName(DiagramKind kind) => DiagramKindCatalog.For(kind).Caption;
}

/// <summary>A toolbar entry: an element kind to add, or a relation to draw between two boxes.</summary>
public sealed partial class ToolboxItem(string label, string? kind = null, string? relation = null) : ObservableObject
{
    public string Label { get; } = label;
    public string? Kind { get; } = kind;
    public string? Relation { get; } = relation;
    public bool IsRelation => Relation is not null;

    /// <summary>The relation this entry draws is waiting for its clicks.</summary>
    [ObservableProperty]
    public partial bool IsActive { get; set; }

    public string Tip => IsRelation
        ? $"Draw {Label.ToLowerInvariant()}: click one box, then the other"
        : $"Add a {Kind} to what this diagram shows";
}
