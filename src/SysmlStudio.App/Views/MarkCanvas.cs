using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using SysmlStudio.Diagrams;

namespace SysmlStudio.App.Views;

/// <summary>
/// Draws a node's marks in the theme's colours. The marks come from
/// <see cref="NodeMarks"/>, the same list the SVG export writes, so a notation
/// has one description and an exported diagram cannot drift from the screen.
/// </summary>
public sealed class MarkCanvas : Control
{
    public static readonly StyledProperty<IReadOnlyList<IMark>?> MarksProperty =
        AvaloniaProperty.Register<MarkCanvas, IReadOnlyList<IMark>?>(nameof(Marks));

    static MarkCanvas() => AffectsRender<MarkCanvas>(MarksProperty);

    public MarkCanvas() => ActualThemeVariantChanged += (_, _) => InvalidateVisual();

    public IReadOnlyList<IMark>? Marks
    {
        get => GetValue(MarksProperty);
        set => SetValue(MarksProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        foreach (var mark in Marks ?? [])
        {
            switch (mark)
            {
                case LineMark line:
                    context.DrawLine(new Pen(Brush(line.Stroke), line.Thickness, line.Dashed ? new DashStyle([4, 3], 0) : null),
                        new Point(line.X1, line.Y1), new Point(line.X2, line.Y2));
                    break;

                case RectMark rect:
                    context.DrawRectangle(Brush(rect.Fill), Pen(rect.Stroke, 1.2), new RoundedRect(new Rect(rect.X, rect.Y, rect.Width, rect.Height), rect.Radius));
                    break;

                case EllipseMark ellipse:
                    context.DrawEllipse(Brush(ellipse.Fill), Pen(ellipse.Stroke, ellipse.Thickness), new Point(ellipse.CentreX, ellipse.CentreY), ellipse.RadiusX, ellipse.RadiusY);
                    break;

                case PolygonMark polygon:
                    DrawPolygon(context, polygon);
                    break;

                case TextMark text:
                    DrawText(context, text);
                    break;

                default:
                    break;
            }
        }
    }

    private void DrawPolygon(DrawingContext context, PolygonMark polygon)
    {
        var geometry = new StreamGeometry();
        using (var figure = geometry.Open())
        {
            figure.BeginFigure(new Point(polygon.Points[0].X, polygon.Points[0].Y), isFilled: polygon.Fill != Ink.None);
            foreach (var (x, y) in polygon.Points.Skip(1))
                figure.LineTo(new Point(x, y));
            figure.EndFigure(isClosed: true);
        }

        context.DrawGeometry(Brush(polygon.Fill), Pen(polygon.Stroke, 1.2), geometry);
    }

    private void DrawText(DrawingContext context, TextMark text)
    {
        var typeface = new Typeface(TextElement.GetFontFamily(this), weight: text.Strong ? FontWeight.SemiBold : FontWeight.Normal);
        var formatted = new FormattedText(text.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, text.Size, Brush(text.Colour));
        var x = text.Anchor == TextAnchor.Middle ? text.X - (formatted.Width / 2) : text.X;
        context.DrawText(formatted, new Point(x, text.Y - (formatted.Height / 2)));
    }

    private Pen? Pen(Ink ink, double thickness)
        => ink == Ink.None ? null : new Pen(Brush(ink), thickness);

    private IBrush? Brush(Ink ink)
    {
        var key = ink switch
        {
            Ink.Line => "EdgeBrush",
            Ink.Outline => "EdgeBrush",
            Ink.Surface => "SurfaceRaisedBrush",
            Ink.Text => "TextBrush",
            Ink.Muted => "TextMutedBrush",
            Ink.Faint => "TextFaintBrush",
            _ => null,
        };

        return key is not null && this.TryFindResource(key, ActualThemeVariant, out var found) ? found as IBrush : null;
    }
}
