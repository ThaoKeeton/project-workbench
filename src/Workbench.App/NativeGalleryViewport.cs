using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Workbench.App;

internal sealed partial class MainWindow
{
    private static ScrollViewer? GalleryFocusScroller(FrameworkElement page, FrameworkElement focus)
    {
        if (!page.IsLoaded || !focus.IsLoaded || page.XamlRoot is null
            || !ReferenceEquals(focus.XamlRoot, page.XamlRoot)) return null;
        ScrollViewer? owner = null;
        for (DependencyObject? parent = focus; parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is UIElement { Visibility: not Visibility.Visible }) return null;
            if (owner is null && parent is ScrollViewer viewer) owner = viewer;
            if (ReferenceEquals(parent, page)) return owner is { IsLoaded: true } ? owner : null;
        }
        return null; // A shared XamlRoot alone does not prove page/scroll ownership.
    }

    private static bool GalleryTryFocusBounds(ScrollViewer scroller, FrameworkElement focus, out Rect bounds)
    {
        bounds = default;
        if (!double.IsFinite(scroller.ViewportWidth) || !double.IsFinite(scroller.ViewportHeight)
            || scroller.ViewportWidth <= 0 || scroller.ViewportHeight <= 0
            || !double.IsFinite(focus.ActualWidth) || !double.IsFinite(focus.ActualHeight)
            || focus.ActualWidth <= 0 || focus.ActualHeight <= 0) return false;
        bounds = focus.TransformToVisual(scroller).TransformBounds(new Rect(0, 0, focus.ActualWidth, focus.ActualHeight));
        return double.IsFinite(bounds.X) && double.IsFinite(bounds.Y)
            && double.IsFinite(bounds.Width) && double.IsFinite(bounds.Height) && bounds.Width > 0 && bounds.Height > 0;
    }

    private static bool GalleryFocusInViewport(ScrollViewer scroller, FrameworkElement focus) =>
        GalleryTryFocusBounds(scroller, focus, out var bounds) && bounds.Right > 0 && bounds.X < scroller.ViewportWidth
        && bounds.Bottom > 0 && bounds.Y < scroller.ViewportHeight;

    private static async Task<bool> GalleryChangeViewAsync(ScrollViewer scroller, double target,
        Func<bool> live, Func<bool> settled)
    {
        const double tolerance = 0.5;
        if (!live() || !double.IsFinite(target) || target < 0 || target > scroller.ScrollableHeight) return false;
        bool AtTarget() => Math.Abs(scroller.VerticalOffset - target) <= tolerance;
        if (AtTarget()) return settled() && live(); // No movement needs an event.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool accepted = false, finalView = false, layoutObserved = false, intermediate = false;
        void Observe()
        {
            try
            {
                if (!live()) completion.TrySetResult(false);
                else if (accepted && !intermediate && (finalView || layoutObserved) && AtTarget() && settled())
                    completion.TrySetResult(true);
            }
            catch (Exception) { completion.TrySetResult(false); }
        }
        void ViewChanged(object? sender, ScrollViewerViewChangedEventArgs args)
        {
            intermediate = args.IsIntermediate;
            finalView = !args.IsIntermediate;
            Observe();
        }
        void LayoutUpdated(object? sender, object args)
        {
            // This is a whole-tree signal (sender is null). Only measured state
            // of our same live target can complete the request.
            layoutObserved = true;
            Observe();
        }
        try
        {
            scroller.ViewChanged += ViewChanged;
            scroller.LayoutUpdated += LayoutUpdated;
            if (!live()) return false;
            accepted = scroller.ChangeView(null, target, null, true);
            if (!accepted) return false;
            scroller.UpdateLayout();
            Observe();
            var observed = await completion.Task.WaitAsync(deadline.Token);
            return observed && !deadline.IsCancellationRequested && live() && AtTarget() && settled();
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested) { return false; }
        finally
        {
            scroller.ViewChanged -= ViewChanged;
            scroller.LayoutUpdated -= LayoutUpdated;
            completion.TrySetResult(false); // No observer or unfinished owned work survives.
        }
    }
}
