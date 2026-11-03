using System.Text;

namespace SysmlStudio.Diagrams;

/// <summary>The colour a mark takes from the theme; the canvas and an SVG file each say what it is.</summary>
public enum Ink
{
    None,
    Line,
    Outline,
    Surface,
    Text,
    Muted,
    Faint,
}

/// <summary>
/// One thing to draw, in the node's own coordinates. The canvas and the SVG
/// exporter both draw these, so a notation is written once and an exported
/// diagram says what the screen said.
/// </summary>
public interface IMark;

public sealed record LineMark(double X1, double Y1, double X2, double Y2, Ink Stroke = Ink.Line, bool Dashed = false, double Thickness = 1.2) : IMark;

public sealed record RectMark(double X, double Y, double Width, double Height, Ink Stroke = Ink.Outline, Ink Fill = Ink.None, double Radius = 0) : IMark;

public sealed record EllipseMark(double CentreX, double CentreY, double RadiusX, double RadiusY, Ink Stroke = Ink.Outline, Ink Fill = Ink.None, double Thickness = 1.2) : IMark;

public sealed record PolygonMark(IReadOnlyList<(double X, double Y)> Points, Ink Stroke = Ink.Outline, Ink Fill = Ink.Surface) : IMark;

public enum TextAnchor
{
    Start,
    Middle,
}

/// <summary><paramref name="Y"/> is the middle of the line of text.</summary>
public sealed record TextMark(double X, double Y, string Text, double Size = 14, Ink Colour = Ink.Text, TextAnchor Anchor = TextAnchor.Middle, bool Strong = false) : IMark;

/// <summary>Sizes and draws every node that is not a box.</summary>
public static class NodeMarks
{
    private const double CharacterAt13 = 6.4;
    private const double TitleLine = 18;

    /// <summary>The height of a lifeline's head, which the sequence layout hangs its rows from.</summary>
    public const double LifelineHead = 46;

    /// <summary>How tall a frame's tag is.</summary>
    public const double FrameTag = 24;

    /// <summary>Space a boundary or a composite state keeps inside itself above and around what it holds.</summary>
    public static double HeaderOf(DiagramNode node)
    {
        if (node.Notation != NodeNotation.CompositeState)
            return 44;

        return node.Features.Count == 0 ? 36 : 46 + (node.Features.Count * 18);
    }

    /// <summary>An estimate of the width of a line of text, as the rest of the layout makes it.</summary>
    public static double TextWidth(string text, double size = 14) => text.Length * CharacterAt13 * size / 13;

    /// <summary>Sets the size of a node drawn from marks. Frames and rows are sized by their layout.</summary>
    public static void Measure(DiagramNode node)
    {
        switch (node.Notation)
        {
            case NodeNotation.Decision:
                node.Width = node.Height = 40;
                break;

            case NodeNotation.Bar:
                node.Width = Math.Clamp(node.Span * 90, 120, 480);
                node.Height = 7;
                break;

            case NodeNotation.Final:
                node.Width = node.Height = 26;
                break;

            case NodeNotation.Send or NodeNotation.Accept:
                node.Width = Math.Clamp(TextWidth(node.Label) + 56, 120, 360);
                node.Height = 48;
                break;

            case NodeNotation.Actor:
                {
                    var lines = Wrap(node.Label, 18);
                    node.Width = Math.Max(72, lines.Max(l => TextWidth(l)) + 12);
                    node.Height = 62 + (lines.Count * TitleLine);
                    break;
                }

            case NodeNotation.UseCase:
                {
                    var lines = Wrap(node.Label, 22);
                    node.Width = Math.Max(150, (lines.Max(l => TextWidth(l)) * 1.25) + 48);
                    node.Height = 40 + (lines.Count * TitleLine);
                    break;
                }

            case NodeNotation.InteractionUse:
                node.Width = Math.Clamp(TextWidth(node.Label) + 56, 170, 420);
                node.Height = 68;
                break;

            case NodeNotation.Lifeline:
                node.Width = Math.Clamp(Math.Max(TextWidth(node.Label, 14), TextWidth(node.Detail ?? string.Empty)) + 28, 110, 300);
                break;

            default:
                break;
        }
    }

    /// <summary>What to draw for a node, in its own coordinates; nothing for a box.</summary>
    public static IReadOnlyList<IMark> For(DiagramNode node)
    {
        var (w, h) = (node.Width, node.Height);
        return node.Notation switch
        {
            NodeNotation.Decision => Decision(node, w, h),
            NodeNotation.Bar => Bar(node, w, h),
            NodeNotation.Final =>
            [
                new EllipseMark(w / 2, h / 2, 12, 12, Ink.Muted, Thickness: 1.5),
                new EllipseMark(w / 2, h / 2, 6, 6, Ink.None, Ink.Muted),
            ],
            NodeNotation.Send =>
            [
                new PolygonMark([(0, 0), (w - 18, 0), (w, h / 2), (w - 18, h), (0, h)]),
                Name(node, (w - 18) / 2, h / 2),
            ],
            NodeNotation.Accept =>
            [
                new PolygonMark([(0, 0), (w, 0), (w, h), (0, h), (18, h / 2)]),
                Name(node, (w + 18) / 2, h / 2),
            ],
            NodeNotation.Actor => Actor(node, w),
            NodeNotation.UseCase => UseCase(node, w, h),
            NodeNotation.Boundary =>
            [
                new RectMark(0, 0, w, h),
                new TextMark(w / 2, 22, node.Label, 14, Strong: true),
            ],
            NodeNotation.CompositeState => CompositeState(node, w, h),
            NodeNotation.Lifeline => Lifeline(node, w, h),
            NodeNotation.InteractionUse => InteractionUse(node, w, h),
            NodeNotation.Frame => FrameMarks(node, w, h),
            NodeNotation.TimeRow =>
            [
                new TextMark(0, h / 2, node.Label, 14, Anchor: TextAnchor.Start, Strong: true),
                new LineMark(node.Ticks.Count > 0 ? node.Ticks[0] : 0, h / 2, w, h / 2, Ink.Outline),
            ],
            NodeNotation.TimeBand => TimeBand(node, w, h),
            NodeNotation.TimePoint => TimePoint(node, h),
            NodeNotation.TimeAxis => TimeAxis(node, w),
            _ => [],
        };
    }

    private static TextMark Name(DiagramNode node, double x, double y) => new(x, y, node.Label);

    private static List<IMark> Decision(DiagramNode node, double w, double h)
    {
        var marks = new List<IMark> { new PolygonMark([(w / 2, 0), (w, h / 2), (w / 2, h), (0, h / 2)]) };

        // A name is optional on a diamond and goes beside it, where edges leave room.
        if (node.Element.Name is { Length: > 0 })
            marks.Add(new TextMark(w + 4, 6, node.Label, 12, Ink.Muted, TextAnchor.Start));
        return marks;
    }

    private static List<IMark> Bar(DiagramNode node, double w, double h)
    {
        var marks = new List<IMark> { new RectMark(0, 0, w, h, Ink.None, Ink.Muted, 1) };
        if (node.Element.Name is { Length: > 0 })
            marks.Add(new TextMark(w + 6, h / 2, node.Label, 12, Ink.Muted, TextAnchor.Start));
        return marks;
    }

    private static List<IMark> Actor(DiagramNode node, double w)
    {
        var x = w / 2;
        var marks = new List<IMark>
        {
            new EllipseMark(x, 11, 9, 9, Ink.Muted, Ink.Surface, 1.5),
            new LineMark(x, 20, x, 42, Ink.Muted, Thickness: 1.5),
            new LineMark(x - 15, 28, x + 15, 28, Ink.Muted, Thickness: 1.5),
            new LineMark(x, 42, x - 12, 58, Ink.Muted, Thickness: 1.5),
            new LineMark(x, 42, x + 12, 58, Ink.Muted, Thickness: 1.5),
        };

        var y = 62 + (TitleLine / 2);
        foreach (var line in Wrap(node.Label, 18))
        {
            marks.Add(new TextMark(x, y, line));
            y += TitleLine;
        }

        return marks;
    }

    private static List<IMark> UseCase(DiagramNode node, double w, double h)
    {
        var marks = new List<IMark> { new EllipseMark(w / 2, h / 2, w / 2, h / 2, Ink.Outline, Ink.Surface) };
        var lines = Wrap(node.Label, 22);
        var y = (h / 2) - ((lines.Count - 1) * TitleLine / 2);
        foreach (var line in lines)
        {
            marks.Add(new TextMark(w / 2, y, line));
            y += TitleLine;
        }

        return marks;
    }

    private static List<IMark> CompositeState(DiagramNode node, double w, double h)
    {
        // No fill: the edges between the states inside are drawn beneath the nodes.
        var marks = new List<IMark>
        {
            new RectMark(0, 0, w, h, Radius: 12),
            new TextMark(w / 2, 20, node.Label, 15),
        };

        var y = 44.0;
        foreach (var feature in node.Features)
        {
            marks.Add(new TextMark(14, y, feature, 12, Ink.Muted, TextAnchor.Start));
            y += 18;
        }

        marks.Add(new LineMark(0, HeaderOf(node), w, HeaderOf(node), Ink.Outline));
        foreach (var x in node.Ticks)
            marks.Add(new LineMark(x, HeaderOf(node), x, h, Ink.Muted, Dashed: true));
        return marks;
    }

    private static List<IMark> Lifeline(DiagramNode node, double w, double h)
    {
        var x = w / 2;
        var marks = new List<IMark>
        {
            new LineMark(x, LifelineHead, x, h, Ink.Muted, Dashed: true),
            new RectMark(0, 0, w, LifelineHead, Ink.Outline, Ink.Surface),
        };

        if (node.Detail is { } type)
        {
            marks.Add(new TextMark(x, (LifelineHead / 2) - 9, node.Label, 14));
            marks.Add(new TextMark(x, (LifelineHead / 2) + 9, ": " + type, 12, Ink.Muted));
        }
        else
        {
            marks.Add(new TextMark(x, LifelineHead / 2, node.Label, 14));
        }

        foreach (var bar in node.Activations)
            marks.Add(new RectMark(x - 5, bar.Top, 10, bar.Bottom - bar.Top, Ink.Muted, Ink.Surface));

        foreach (var occurrence in node.Events)
        {
            marks.Add(new EllipseMark(x, occurrence.Y, 3.5, 3.5, Ink.None, Ink.Muted));
            marks.Add(new TextMark(x + 9, occurrence.Y, occurrence.Label, 12, Ink.Muted, TextAnchor.Start));
        }

        return marks;
    }

    private static List<IMark> InteractionUse(DiagramNode node, double w, double h) =>
    [
        new RectMark(0, 0, w, h, Ink.Outline, Ink.Surface),
        .. Tag("ref", TextWidth("ref", 12)),
        new TextMark(w / 2, (h / 2) + 7, node.Label, 14),
    ];

    private static List<IMark> FrameMarks(DiagramNode node, double w, double h)
    {
        var tag = $"{node.Tag} {node.Label}";
        return
        [
            new RectMark(0, 0, w, h),
            .. Tag(tag, TextWidth(tag, 12)),
        ];
    }

    /// <summary>The pentagon in a frame's corner that says what kind of frame it is.</summary>
    private static List<IMark> Tag(string text, double width) =>
    [
        new PolygonMark([(0, 0), (width + 20, 0), (width + 20, FrameTag - 8), (width + 12, FrameTag), (0, FrameTag)]),
        new TextMark(8, FrameTag / 2, text, 12, Ink.Muted, TextAnchor.Start),
    ];

    private static List<IMark> TimeBand(DiagramNode node, double w, double h)
    {
        var marks = new List<IMark>
        {
            new PolygonMark([(0, h / 2), (8, 0), (w - 8, 0), (w, h / 2), (w - 8, h), (8, h)]),
        };

        if (node.Detail is { } detail)
        {
            marks.Add(new TextMark(w / 2, (h / 2) - 8, node.Label));
            marks.Add(new TextMark(w / 2, (h / 2) + 9, detail, 11, Ink.Muted));
        }
        else
        {
            marks.Add(new TextMark(w / 2, h / 2, node.Label));
        }

        return marks;
    }

    private static List<IMark> TimePoint(DiagramNode node, double h)
    {
        var marks = new List<IMark>
        {
            new LineMark(0, 4, 0, h - 4, Ink.Muted, Thickness: 1.5),
            new EllipseMark(0, h / 2, 4, 4, Ink.None, Ink.Muted),
            new TextMark(8, (h / 2) - 11, node.Label, Anchor: TextAnchor.Start),
        };

        if (node.Detail is { } detail)
            marks.Add(new TextMark(8, (h / 2) + 11, detail, 11, Ink.Muted, TextAnchor.Start));
        return marks;
    }

    private static List<IMark> TimeAxis(DiagramNode node, double w)
    {
        var start = node.Ticks.Count > 0 ? node.Ticks[0] : 0;
        var marks = new List<IMark>
        {
            new LineMark(start, 8, w, 8, Ink.Muted),
            new PolygonMark([(w, 8), (w - 9, 4), (w - 9, 12)], Ink.None, Ink.Muted),
        };

        foreach (var x in node.Ticks)
            marks.Add(new LineMark(x, 3, x, 13, Ink.Muted));

        marks.Add(new TextMark(start, 28, node.Label, 12, Ink.Muted, TextAnchor.Start));
        if (node.Detail is { } detail)
            marks.Add(new TextMark(start, 46, detail, 12, Ink.Faint, TextAnchor.Start));
        return marks;
    }

    /// <summary>A label cut into lines at its spaces and camel humps, each no longer than it has to be.</summary>
    public static List<string> Wrap(string text, int width)
    {
        if (text.Length <= width)
            return [text];

        var lines = new List<string>();
        var line = new StringBuilder();
        foreach (var word in Words(text))
        {
            if (line.Length > 0 && line.Length + word.Length > width)
            {
                lines.Add(line.ToString().TrimEnd());
                line.Clear();
            }

            line.Append(word);
        }

        if (line.Length > 0)
            lines.Add(line.ToString().TrimEnd());
        return lines;
    }

    private static IEnumerable<string> Words(string text)
    {
        var start = 0;
        for (var i = 1; i < text.Length; i++)
        {
            if (text[i - 1] == ' ' || (char.IsLower(text[i - 1]) && char.IsUpper(text[i])))
            {
                yield return text[start..i];
                start = i;
            }
        }

        yield return text[start..];
    }
}
