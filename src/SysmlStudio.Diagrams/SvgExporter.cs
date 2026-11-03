using System.Globalization;
using System.Text;
using SysmlStudio.Model;

namespace SysmlStudio.Diagrams;

/// <summary>
/// Writes a laid-out diagram as SVG: one file, no fonts to embed, no network,
/// and the same picture the canvas shows. Colour is the maturity keyword and
/// the arrowheads are UML's, because a diagram that is exported has to keep
/// saying what it said on screen.
/// </summary>
public static class SvgExporter
{
    private const double Margin = 24;

    private static readonly Dictionary<string, string> MaturityColours = new(StringComparer.Ordinal)
    {
        ["implemented"] = "#2E7D32",
        ["inProgress"] = "#F9A825",
        ["writtenAhead"] = "#0277BD",
        ["planned"] = "#757575",
    };

    public static void Write(Diagram diagram, string path)
        => File.WriteAllText(path, ToSvg(diagram), new UTF8Encoding(false));

    public static string ToSvg(Diagram diagram)
    {
        var frame = diagram.FrameBounds();
        var (left, top, right, bottom) = Extent(diagram, frame);
        var width = right - left + (Margin * 2);
        var height = bottom - top + (Margin * 2);

        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture,
            $"""<svg xmlns="http://www.w3.org/2000/svg" width="{N(width)}" height="{N(height)}" viewBox="0 0 {N(width)} {N(height)}" font-family="Segoe UI, Inter, sans-serif" font-size="12">""");
        svg.AppendLine();
        svg.AppendLine(Defs);
        svg.Append("""<rect width="100%" height="100%" fill="#ffffff"/>""");
        svg.AppendLine();
        svg.Append(CultureInfo.InvariantCulture, $"""<g transform="translate({N(Margin - left)},{N(Margin - top)})">""");
        svg.AppendLine();

        if (frame is { } bounds)
            svg.AppendLine(Frame(diagram.FrameLabel!, bounds));

        foreach (var edge in diagram.Edges)
            svg.AppendLine(Edge(edge));

        foreach (var node in diagram.Nodes)
            svg.AppendLine(Node(node));

        svg.AppendLine("</g>");
        svg.AppendLine("</svg>");
        return svg.ToString();
    }

    /// <summary>The frame round a whole diagram, with its label in a corner tab, as UML frames a diagram.</summary>
    private static string Frame(string label, (double X, double Y, double Width, double Height) frame)
    {
        var tabWidth = (label.Length * 6.6) + 22;
        const double tabHeight = 22;
        return string.Create(CultureInfo.InvariantCulture,
            $"""<g><rect x="{N(frame.X)}" y="{N(frame.Y)}" width="{N(frame.Width)}" height="{N(frame.Height)}" fill="none" stroke="#90A4AE"/><path d="M {N(frame.X)} {N(frame.Y + tabHeight)} H {N(frame.X + tabWidth - 8)} L {N(frame.X + tabWidth)} {N(frame.Y + tabHeight - 8)} V {N(frame.Y)}" fill="none" stroke="#90A4AE"/><text x="{N(frame.X + 9)}" y="{N(frame.Y + 15)}" fill="#546E7A">{Escape(label)}</text></g>""");
    }

    /// <summary>What the picture covers: every node, what a mark draws past its node's edge, and the routes that leave them.</summary>
    private static (double Left, double Top, double Right, double Bottom) Extent(Diagram diagram, (double X, double Y, double Width, double Height)? frame)
    {
        if (diagram.Nodes.Count == 0)
            return (0, 0, 200 - (Margin * 2), 100 - (Margin * 2));

        var left = Math.Min(frame?.X ?? 0, diagram.Nodes.Min(n => n.X));
        var top = Math.Min(frame?.Y ?? 0, diagram.Nodes.Min(n => n.Y));
        var right = diagram.Nodes.Max(n => n.X + n.Width);
        var bottom = diagram.Nodes.Max(n => n.Y + n.Height);

        if (frame is { } f)
        {
            right = Math.Max(right, f.X + f.Width);
            bottom = Math.Max(bottom, f.Y + f.Height);
        }

        foreach (var node in diagram.Nodes.Where(n => n.IsDrawn))
        {
            foreach (var text in NodeMarks.For(node).OfType<TextMark>().Where(t => t.Anchor == TextAnchor.Start))
                right = Math.Max(right, node.X + text.X + NodeMarks.TextWidth(text.Text, text.Size));
        }

        foreach (var (x, y) in diagram.Edges.SelectMany(e => e.Waypoints))
        {
            left = Math.Min(left, x);
            right = Math.Max(right, x);
            top = Math.Min(top, y);
            bottom = Math.Max(bottom, y);
        }

        foreach (var edge in diagram.Edges.Where(e => e.LabelCentre is not null && e.Label is not null))
            right = Math.Max(right, edge.LabelCentre!.Value.X + (edge.Label!.Split('\n').Max(l => l.Length) * 3.4));

        return (left, top, right, bottom);
    }

    /// <summary>The three arrowheads UML asks for: hollow triangle, filled, open.</summary>
    private const string Defs =
        """
        <defs>
          <marker id="triangle" viewBox="0 0 10 10" refX="10" refY="5" markerWidth="10" markerHeight="10" orient="auto-start-reverse">
            <path d="M 0 0 L 10 5 L 0 10 z" fill="#ffffff" stroke="#455A64"/>
          </marker>
          <marker id="arrow" viewBox="0 0 10 10" refX="10" refY="5" markerWidth="8" markerHeight="8" orient="auto-start-reverse">
            <path d="M 0 0 L 10 5 L 0 10 z" fill="#455A64"/>
          </marker>
          <marker id="open" viewBox="0 0 10 10" refX="10" refY="5" markerWidth="9" markerHeight="9" orient="auto-start-reverse">
            <path d="M 0 0 L 10 5 L 0 10" fill="none" stroke="#455A64"/>
          </marker>
          <marker id="diamond" viewBox="0 0 14 8" refX="0" refY="4" markerWidth="14" markerHeight="8" orient="auto">
            <path d="M 0 4 L 7 0 L 14 4 L 7 8 z" fill="#455A64"/>
          </marker>
          <marker id="extension" viewBox="0 0 10 10" refX="10" refY="5" markerWidth="10" markerHeight="10" orient="auto-start-reverse">
            <path d="M 0 0 L 10 5 L 0 10 z" fill="#455A64" stroke="#455A64"/>
          </marker>
          <marker id="containment" viewBox="0 0 14 14" refX="0" refY="7" markerWidth="12" markerHeight="12" orient="auto">
            <circle cx="7" cy="7" r="6.4" fill="#ffffff" stroke="#455A64"/>
            <path d="M 0.6 7 H 13.4 M 7 0.6 V 13.4" stroke="#455A64"/>
          </marker>
        </defs>
        """;

    private static string Node(DiagramNode node)
    {
        if (node.Shape != NodeShape.Box)
            return UmlNode(node);

        if (node.IsDrawn)
            return Marks(node);

        if (node.IsPseudo)
        {
            var cx = N(node.X + (node.Width / 2));
            var cy = N(node.Y + (node.Height / 2));
            return node.Pseudo == "start"
                ? string.Create(CultureInfo.InvariantCulture, $"""<circle cx="{cx}" cy="{cy}" r="9" fill="#455A64"/>""")
                : string.Create(CultureInfo.InvariantCulture, $"""<g><circle cx="{cx}" cy="{cy}" r="11" fill="none" stroke="#455A64" stroke-width="1.5"/><circle cx="{cx}" cy="{cy}" r="6" fill="#455A64"/></g>""");
        }

        var colour = Colour(node.Maturity);
        var radius = node.Notation == NodeNotation.Rounded ? "12" : "4";
        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture,
            $"""<g><rect x="{N(node.X)}" y="{N(node.Y)}" width="{N(node.Width)}" height="{N(node.Height)}" rx="{radius}" fill="#ffffff" stroke="{colour}" stroke-width="2"/>""");

        var line = node.Y + 16;
        if (node.Stereotype is { Length: > 0 } stereotype)
        {
            svg.Append(CultureInfo.InvariantCulture,
                $"""<text x="{N(node.X + 8)}" y="{N(line)}" fill="#607D8B" font-size="10">{Escape(stereotype)}</text>""");
            line += 16;
        }

        svg.Append(CultureInfo.InvariantCulture,
            $"""<text x="{N(node.X + 8)}" y="{N(line)}" fill="#212121" font-weight="600">{Escape(node.Label)}</text>""");

        foreach (var feature in node.Features)
        {
            line += 16;
            svg.Append(CultureInfo.InvariantCulture,
                $"""<text x="{N(node.X + 8)}" y="{N(line)}" fill="#424242" font-size="10">{Escape(feature)}</text>""");
        }

        svg.Append("</g>");
        return svg.ToString();
    }

    /// <summary>A box in UML's shapes: what the canvas draws, with its stereotype, compartments, icon and ports.</summary>
    private static string UmlNode(DiagramNode node)
    {
        const string line = "#455A64";
        var colour = Colour(node.Maturity);
        var svg = new StringBuilder("<g>");
        var (x, y, width, height) = (node.X, node.Y, node.Width, node.Height);

        switch (node.Shape)
        {
            case NodeShape.Folder:
                svg.Append(CultureInfo.InvariantCulture,
                    $"""<rect x="{N(x)}" y="{N(y)}" width="{N(DiagramLayout.FolderTabWidth(width))}" height="{N(DiagramLayout.FolderTab + 1)}" fill="#ffffff" stroke="{colour}" stroke-width="1.5"/>""");
                y += DiagramLayout.FolderTab;
                height -= DiagramLayout.FolderTab;
                break;
            case NodeShape.Device:
                const double depth = DiagramLayout.NodeDepth;
                svg.Append(CultureInfo.InvariantCulture,
                    $"""<path d="M {N(x)} {N(y + depth)} L {N(x + depth)} {N(y)} H {N(x + width)} V {N(y + height - depth)} L {N(x + width - depth)} {N(y + height)} V {N(y + depth)} z M {N(x + width - depth)} {N(y + depth)} L {N(x + width)} {N(y)}" fill="#ffffff" stroke="{colour}" stroke-width="1.5"/>""");
                y += depth;
                height -= depth;
                width -= depth;
                break;
        }

        var dash = node.IsReference ? """ stroke-dasharray="4 3" """ : " ";
        svg.Append(CultureInfo.InvariantCulture,
            $"""<rect x="{N(x)}" y="{N(y)}" width="{N(width)}" height="{N(height)}" fill="#ffffff" stroke="{colour}" stroke-width="1.5"{dash}/>""");

        var centre = N(x + (width / 2));
        var cursor = y + 22 + (node.Ports.Count > 0 ? DiagramLayout.PortBand : 0);
        if (node.Stereotype is { Length: > 0 } stereotype)
        {
            svg.Append(CultureInfo.InvariantCulture,
                $"""<text x="{centre}" y="{N(cursor)}" fill="#607D8B" font-size="10" text-anchor="middle">{Escape(stereotype)}</text>""");
            cursor += 16;
        }

        var italic = node.IsAbstract ? """ font-style="italic" """ : " ";
        var underline = node.Shape == NodeShape.Instance ? """ text-decoration="underline" """ : " ";
        svg.Append(CultureInfo.InvariantCulture,
            $"""<text x="{centre}" y="{N(cursor)}" fill="#212121" font-weight="600" text-anchor="middle"{italic}{underline}>{Escape(node.Label)}</text>""");
        cursor += 10;

        foreach (var compartment in new[] { node.Features, node.Operations }.Where(c => c.Count > 0))
        {
            svg.Append(CultureInfo.InvariantCulture, $"""<path d="M {N(x)} {N(cursor)} H {N(x + width)}" stroke="{line}" stroke-width="0.6"/>""");
            cursor += 4;
            foreach (var feature in compartment)
            {
                cursor += 15;
                svg.Append(CultureInfo.InvariantCulture,
                    $"""<text x="{N(x + 10)}" y="{N(cursor)}" fill="#424242" font-size="10">{Escape(feature)}</text>""");
            }

            cursor += 8;
        }

        var (iconX, iconY) = (x + width - 27, y + (node.Ports.Count > 0 ? DiagramLayout.PortBand + 4 : 10));
        if (node.Shape == NodeShape.Component)
        {
            svg.Append(CultureInfo.InvariantCulture,
                $"""<path transform="translate({N(iconX)},{N(iconY)})" d="M 3 0 H 17 V 14 H 3 z M 0 3 H 6 V 6 H 0 z M 0 8 H 6 V 11 H 0 z" fill="#ffffff" stroke="{line}"/>""");
        }
        else if (node.Shape == NodeShape.Artifact)
        {
            svg.Append(CultureInfo.InvariantCulture,
                $"""<path transform="translate({N(iconX + 4)},{N(iconY)})" d="M 0 0 H 9 L 13 4 V 16 H 0 z M 9 0 V 4 H 13" fill="#ffffff" stroke="{line}"/>""");
        }

        foreach (var port in node.Ports)
            svg.Append(Port(node, port));

        svg.Append("</g>");
        return svg.ToString();
    }

    /// <summary>A node drawn from the same marks the canvas draws it from.</summary>
    private static string Marks(DiagramNode node)
    {
        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture, $"""<g transform="translate({N(node.X)},{N(node.Y)})">""");
        foreach (var mark in NodeMarks.For(node))
            svg.Append(Mark(mark, node));
        svg.Append("</g>");
        return svg.ToString();
    }

    /// <summary>A port: its square on the border, its ball or socket outside, its name inside.</summary>
    private static string Port(DiagramNode node, DiagramPort port)
    {
        const string line = "#455A64";
        var (cx, cy) = (node.X + port.X, node.Y + port.Y);
        var outward = port.OnTop ? -1 : 1;
        var svg = new StringBuilder();
        var stickEnd = cy + (outward * 12);
        if (port.Flow != PortFlow.None)
            svg.Append(CultureInfo.InvariantCulture, $"""<path d="M {N(cx)} {N(cy + (outward * 5))} V {N(stickEnd)}" stroke="{line}"/>""");
        if (port.Flow == PortFlow.Provided)
        {
            svg.Append(CultureInfo.InvariantCulture, $"""<circle cx="{N(cx)}" cy="{N(stickEnd + (outward * 5))}" r="5" fill="#ffffff" stroke="{line}"/>""");
        }
        else if (port.Flow == PortFlow.Required)
        {
            var rim = N(stickEnd + (outward * 6));
            svg.Append(CultureInfo.InvariantCulture,
                $"""<path d="M {N(cx - 6)} {rim} A 6 6 0 0 {(port.OnTop ? 0 : 1)} {N(cx + 6)} {rim}" fill="none" stroke="{line}"/>""");
        }

        svg.Append(CultureInfo.InvariantCulture,
            $"""<rect x="{N(cx - 5)}" y="{N(cy - 5)}" width="10" height="10" fill="#ffffff" stroke="{line}"/>""");
        svg.Append(CultureInfo.InvariantCulture,
            $"""<text x="{N(cx)}" y="{N(port.OnTop ? cy + 19 : cy - 11)}" fill="#546E7A" font-size="10" text-anchor="middle">{Escape(port.Name)}</text>""");
        return svg.ToString();
    }

    private static string Mark(IMark mark, DiagramNode node) => mark switch
    {
        LineMark line => string.Create(CultureInfo.InvariantCulture,
            $"""<line x1="{N(line.X1)}" y1="{N(line.Y1)}" x2="{N(line.X2)}" y2="{N(line.Y2)}" stroke="{Paint(line.Stroke, node)}" stroke-width="{N(line.Thickness)}"{(line.Dashed ? " stroke-dasharray=\"6 4\"" : string.Empty)}/>"""),

        RectMark rect => string.Create(CultureInfo.InvariantCulture,
            $"""<rect x="{N(rect.X)}" y="{N(rect.Y)}" width="{N(rect.Width)}" height="{N(rect.Height)}" rx="{N(rect.Radius)}" fill="{Paint(rect.Fill, node)}" stroke="{Paint(rect.Stroke, node)}" stroke-width="1.5"/>"""),

        EllipseMark ellipse => string.Create(CultureInfo.InvariantCulture,
            $"""<ellipse cx="{N(ellipse.CentreX)}" cy="{N(ellipse.CentreY)}" rx="{N(ellipse.RadiusX)}" ry="{N(ellipse.RadiusY)}" fill="{Paint(ellipse.Fill, node)}" stroke="{Paint(ellipse.Stroke, node)}" stroke-width="{N(ellipse.Thickness)}"/>"""),

        PolygonMark polygon => string.Create(CultureInfo.InvariantCulture,
            $"""<polygon points="{string.Join(" ", polygon.Points.Select(p => $"{N(p.X)},{N(p.Y)}"))}" fill="{Paint(polygon.Fill, node)}" stroke="{Paint(polygon.Stroke, node)}" stroke-width="1.5"/>"""),

        TextMark text => string.Create(CultureInfo.InvariantCulture,
            $"""<text x="{N(text.X)}" y="{N(text.Y + (text.Size * 0.35))}" font-size="{N(text.Size)}" fill="{Paint(text.Colour, node)}"{(text.Anchor == TextAnchor.Middle ? " text-anchor=\"middle\"" : string.Empty)}{(text.Strong ? " font-weight=\"600\"" : string.Empty)}>{Escape(text.Text)}</text>"""),

        _ => string.Empty,
    };

    private static string Paint(Ink ink, DiagramNode node) => ink switch
    {
        Ink.Line => "#455A64",
        Ink.Outline => Colour(node.Maturity),
        Ink.Surface => "#ffffff",
        Ink.Text => "#212121",
        Ink.Muted => "#546E7A",
        Ink.Faint => "#90A4AE",
        _ => "none",
    };

    private static string Edge(DiagramEdge edge)
    {
        var points = edge.Waypoints.Count >= 2
            ? edge.Waypoints
            : [Centre(edge.Source), Centre(edge.Target)];

        var path = string.Join(" ", points.Select((p, i) =>
            string.Create(CultureInfo.InvariantCulture, $"{(i == 0 ? "M" : "L")} {N(p.X)} {N(p.Y)}")));

        var dash = IsDashed(edge.Kind) ? """ stroke-dasharray="6 4" """ : " ";
        var (X, Y) = edge.LabelCentre is { } placed ? (placed.X, placed.Y + 4) : points[points.Count / 2];
        var label = edge.Label is { Length: > 0 } text ? Label(text, X, Y) : string.Empty;

        var start = edge.Kind switch
        {
            RelationKind.Composition => """ marker-start="url(#diamond)" """,
            RelationKind.Containment => """ marker-start="url(#containment)" """,
            _ => " ",
        };
        var end = Marker(edge) is { } marker ? $"marker-end=\"url(#{marker})\"" : string.Empty;
        return string.Create(CultureInfo.InvariantCulture,
            $"""<path d="{path}" fill="none" stroke="#455A64"{dash}{start}{end}/>{label}""");
    }

    /// <summary>An edge's label, one text element to a line: a communication link lists its messages.</summary>
    private static string Label(string text, double x, double y)
    {
        var lines = text.Split('\n');
        var first = y - 4 - ((lines.Length - 1) * 7);
        return string.Concat(lines.Select((line, i) => string.Create(CultureInfo.InvariantCulture,
            $"""<text x="{N(x)}" y="{N(first + (i * 14))}" fill="#546E7A" font-size="10" text-anchor="middle" paint-order="stroke" stroke="#ffffff" stroke-width="3">{Escape(Shorten(line))}</text>""")));
    }

    /// <summary>A hollow triangle for specialization, an open arrow for a trace, a control flow and a message nobody waits for, filled otherwise, none on an association.</summary>
    private static string? Marker(DiagramEdge edge)
    {
        // A connection joins two ends as equals, and the canvas draws no head on it either.
        if (edge.Notation == EdgeNotation.Association || edge.IsHeadless
            || edge.Kind is RelationKind.Containment or RelationKind.Link or RelationKind.Connect or RelationKind.Interface)
        {
            return null;
        }

        if (edge.HasOpenHead)
            return "open";

        return edge.Notation == EdgeNotation.ByKind
            ? edge.Kind switch
            {
                RelationKind.Specialization or RelationKind.Redefinition => "triangle",
                RelationKind.Satisfy or RelationKind.Verify or RelationKind.Allocate
                    or RelationKind.Dependency or RelationKind.Typing or RelationKind.Import or RelationKind.Association => "open",
                RelationKind.Extension => "extension",
                _ => "arrow",
            }
            : "arrow";
    }

    private static bool IsDashed(RelationKind kind) => kind
        is RelationKind.Satisfy or RelationKind.Verify or RelationKind.Allocate
        or RelationKind.Dependency or RelationKind.Typing or RelationKind.Import;

    private static (double X, double Y) Centre(DiagramNode node)
        => (node.X + (node.Width / 2), node.Y + (node.Height / 2));

    private static string Colour(string? maturity)
        => maturity is not null && MaturityColours.TryGetValue(maturity, out var colour) ? colour : "#455A64";

    /// <summary>An edge label is a hint, not the model: long ones are cut.</summary>
    private static string Shorten(string text, int length = 34)
        => text.Length <= length ? text : text[..length].TrimEnd() + "…";

    private static string N(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Escape(string text) => text
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal);
}
