using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Workbench.App;

internal sealed partial class MainWindow
{
    private sealed class GalleryFocusTrace
    {
        internal NativeGalleryFocusCheck Check;
        internal bool? SawPage, SawRoot;
        internal int Depth = -1, PageDepth = -1, RootDepth = -1, NearestOwnerDepth = -1, HiddenDepth = -1;
        internal bool DepthCapped;

        internal NativeGalleryFocusObservation Snapshot(NativeGalleryFocusPhase phase, NativeGalleryFocusSite site) =>
            new(phase, site, Check, SawPage, SawRoot, PageDepth, RootDepth, NearestOwnerDepth, HiddenDepth, DepthCapped);

        internal void Visit()
        {
            if (Depth < 255) Depth++;
            else DepthCapped = true; // Only this diagnostic counter saturates; the real walk continues.
        }
    }

    private static ScrollViewer? GalleryFocusScroller(FrameworkElement root, FrameworkElement page, FrameworkElement focus,
        GalleryFocusTrace? trace = null)
    {
        // Preserve the original guard order and short-circuiting, including each XamlRoot read.
        if (!root.IsLoaded) return GalleryFocusObserved(trace, null, NativeGalleryFocusCheck.RootUnloaded);
        if (!page.IsLoaded) return GalleryFocusObserved(trace, null, NativeGalleryFocusCheck.PageUnloaded);
        if (!focus.IsLoaded) return GalleryFocusObserved(trace, null, NativeGalleryFocusCheck.FocusUnloaded);
        if (root.XamlRoot is null) return GalleryFocusObserved(trace, null, NativeGalleryFocusCheck.RootXamlRootNull);
        if (!ReferenceEquals(page.XamlRoot, root.XamlRoot)) return GalleryFocusObserved(trace, null, NativeGalleryFocusCheck.PageXamlRootMismatch);
        if (!ReferenceEquals(focus.XamlRoot, root.XamlRoot)) return GalleryFocusObserved(trace, null, NativeGalleryFocusCheck.FocusXamlRootMismatch);
        ScrollViewer? owner = null;
        bool containsPage = false;
        for (DependencyObject? parent = focus; parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            trace?.Visit();
            if (parent is UIElement { Visibility: not Visibility.Visible })
            {
                if (trace is not null) trace.HiddenDepth = trace.Depth;
                return GalleryFocusObserved(trace, null, NativeGalleryFocusCheck.AncestorHidden);
            }
            if (owner is null && parent is ScrollViewer viewer)
            {
                owner = viewer;
                if (trace is not null) trace.NearestOwnerDepth = trace.Depth;
            }
            var isPage = ReferenceEquals(parent, page);
            if (trace is not null) trace.SawPage = trace.SawPage == true || isPage;
            if (isPage)
            {
                containsPage = true;
                if (trace is not null) trace.PageDepth = trace.Depth;
            }
            // A plugin Grid can use a host ancestor rather than own a scroller.
            // Prove the complete chain before returning its nearest actual owner.
            var isRoot = ReferenceEquals(parent, root);
            if (trace is not null) trace.SawRoot = trace.SawRoot == true || isRoot;
            if (isRoot)
            {
                if (trace is not null) trace.RootDepth = trace.Depth;
                if (!containsPage) return GalleryFocusObserved(trace, null, NativeGalleryFocusCheck.RootReachedPageMissing);
                if (owner is null) return GalleryFocusObserved(trace, null, NativeGalleryFocusCheck.RootReachedOwnerMissing);
                if (!owner.IsLoaded) return GalleryFocusObserved(trace, null, NativeGalleryFocusCheck.OwnerUnloaded);
                if (!ReferenceEquals(owner.XamlRoot, root.XamlRoot)) return GalleryFocusObserved(trace, null, NativeGalleryFocusCheck.OwnerXamlRootMismatch);
                return GalleryFocusObserved(trace, owner, NativeGalleryFocusCheck.OwnerReturned);
            }
        }
        return GalleryFocusObserved(trace, null, NativeGalleryFocusCheck.RootNotReached); // A shared XamlRoot alone does not prove ownership.
    }

    private static ScrollViewer? GalleryFocusObserved(GalleryFocusTrace? trace, ScrollViewer? result, NativeGalleryFocusCheck check)
    {
        if (trace is not null) trace.Check = check;
        return result;
    }

    private static (NativeGalleryAboveRootResult Result, int Visited, int? OwnerDistance) GalleryFocusAboveRoot(FrameworkElement root)
    {
        int visited = 0;
        try
        {
            DependencyObject parent = root;
            for (var distance = 1; distance <= 16; distance++)
            {
                var next = VisualTreeHelper.GetParent(parent);
                if (next is null) return (NativeGalleryAboveRootResult.ChainEnded, visited, null);
                visited = distance;
                if (next is ScrollViewer) return (NativeGalleryAboveRootResult.Found, visited, distance);
                parent = next;
            }
            return (NativeGalleryAboveRootResult.Capped, visited, null); // No seventeenth GetParent call.
        }
        catch (Exception) { return (NativeGalleryAboveRootResult.Unavailable, visited, null); }
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
