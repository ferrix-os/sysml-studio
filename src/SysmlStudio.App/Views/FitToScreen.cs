using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Nodify.Avalonia;
using SysmlStudio.App.ViewModels;

namespace SysmlStudio.App.Views;

/// <summary>
/// Fits a diagram to its canvas whenever the canvas shows a new one. The tab
/// control reuses one editor for every tab, so "loaded" alone is not enough:
/// switching tabs changes the data context and needs the same treatment.
/// </summary>
public static class FitToScreen
{
    public static readonly AttachedProperty<bool> OnShowProperty =
        AvaloniaProperty.RegisterAttached<NodifyEditor, bool>("OnShow", typeof(FitToScreen));

    static FitToScreen()
    {
        OnShowProperty.Changed.AddClassHandler<NodifyEditor>((editor, e) =>
        {
            if (e.NewValue is not true)
                return;

            editor.Loaded += (_, _) => Fit(editor);
            editor.DataContextChanged += (_, _) => Fit(editor);
        });
    }

    public static bool GetOnShow(NodifyEditor editor) => editor.GetValue(OnShowProperty);

    public static void SetOnShow(NodifyEditor editor, bool value) => editor.SetValue(OnShowProperty, value);

    /// <summary>
    /// Waits for the nodes to be measured, then frames all of them. Until the
    /// item containers have been laid out the editor's extent is empty and a
    /// fit would frame nothing, so this retries a few frames before giving up.
    /// </summary>
    public static void Fit(NodifyEditor editor) => TryFit(editor, attemptsLeft: 20);

    private static void TryFit(NodifyEditor editor, int attemptsLeft)
    {
        var extent = editor.ItemsExtent;
        if (extent.Width > 0 && extent.Height > 0 && editor.Bounds.Width > 0)
        {
            // A frame round the whole picture is part of what has to fit.
            if (editor.DataContext is DiagramDocumentViewModel { Diagram: var diagram } && diagram.FrameBounds() is { } frame)
                extent = extent.Union(new Rect(frame.X, frame.Y, frame.Width, frame.Height));

            // The side panels float over the canvas: the diagram is framed in
            // what they leave clear, padded so nothing lands under the floating
            // toolbar or the zoom figure either. Those are sizes on the screen,
            // so they are as many of the diagram's units as the zoom makes them.
            var insets = (TopLevel.GetTopLevel(editor)?.DataContext as ShellViewModel)?.CanvasInsets ?? default;
            var clear = new Thickness(56 + insets.Left, 48, 56 + insets.Right, 96);
            var zoom = Math.Clamp(Math.Min(
                (editor.Bounds.Width - clear.Left - clear.Right) / extent.Width,
                (editor.Bounds.Height - clear.Top - clear.Bottom) / extent.Height), 0.1, 1);
            editor.FitToScreen(extent.Inflate(new Thickness(clear.Left / zoom, clear.Top / zoom, clear.Right / zoom, clear.Bottom / zoom)));

            // A small diagram fitted to a big canvas would be drawn at 300 per cent.
            // Nothing is shown larger than life: it is centred at 100 instead.
            if (editor.ViewportZoom > 1)
            {
                editor.ViewportZoom = 1;
                editor.BringIntoView(extent.Center + new Vector((insets.Right - insets.Left) / 2, 0));
            }

            return;
        }

        if (attemptsLeft > 0)
            DispatcherTimer.RunOnce(() => TryFit(editor, attemptsLeft - 1), TimeSpan.FromMilliseconds(50));
    }
}
