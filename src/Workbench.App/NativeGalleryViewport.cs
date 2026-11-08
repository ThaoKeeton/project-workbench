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

    private static FrameworkElement? GalleryFocusViewportFor(FrameworkElement root, FrameworkElement page, FrameworkElement focus,
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
            // Prove the complete chain before choosing its nearest in-root scroller
            // or the exact root as a fixed viewport. Never inspect a parent above root.
            var isRoot = ReferenceEquals(parent, root);
            if (trace is not null) trace.SawRoot = trace.SawRoot == true || isRoot;
            if (isRoot)
            {
                if (trace is not null) trace.RootDepth = trace.Depth;
                if (!containsPage) return GalleryFocusObserved(trace, null, NativeGalleryFocusCheck.RootReachedPageMissing);
                if (owner is null) return GalleryFocusObserved(trace, root, NativeGalleryFocusCheck.OwnerReturned);
                if (!owner.IsLoaded) return GalleryFocusObserved(trace, null, NativeGalleryFocusCheck.OwnerUnloaded);
                if (!ReferenceEquals(owner.XamlRoot, root.XamlRoot)) return GalleryFocusObserved(trace, null, NativeGalleryFocusCheck.OwnerXamlRootMismatch);
                return GalleryFocusObserved(trace, owner, NativeGalleryFocusCheck.OwnerReturned);
            }
        }
        return GalleryFocusObserved(trace, null, NativeGalleryFocusCheck.RootNotReached); // A shared XamlRoot alone does not prove ownership.
    }

    private static FrameworkElement? GalleryFocusObserved(GalleryFocusTrace? trace, FrameworkElement? result, NativeGalleryFocusCheck check)
    {
        if (trace is not null) trace.Check = check;
        return result;
    }

    private static bool GalleryFixedFocusInViewport(FrameworkElement root, FrameworkElement page, FrameworkElement focus)
    {
        if (!root.IsLoaded || !page.IsLoaded || !focus.IsLoaded || root.XamlRoot is null
            || !ReferenceEquals(page.XamlRoot, root.XamlRoot) || !ReferenceEquals(focus.XamlRoot, root.XamlRoot)) return false;
        var visible = new NativeGalleryBounds(0, 0, root.ActualWidth, root.ActualHeight);
        static bool Finite(Point point) => double.IsFinite(point.X) && double.IsFinite(point.Y);
        bool IntersectMeasured(UIElement element, NativeGalleryBounds local, Transform? clipTransform = null)
        {
            if (!NativeGalleryPolicy.TryIntersect(local, local, out _)) return false;
            var toRoot = element.TransformToVisual(root);
            bool TryProject(Point point, out Point projected)
            {
                if (clipTransform is not null) point = clipTransform.TransformPoint(point);
                projected = default;
                if (!Finite(point)) return false;
                projected = toRoot.TransformPoint(point);
                return Finite(projected);
            }
            // Transform corners in order before taking their bounds; an intermediate
            // axis-aligned box would enlarge a rotated clip before its root transform.
            var right = local.X + local.Width;
            var bottom = local.Y + local.Height;
            if (!TryProject(new(local.X, local.Y), out var topLeft)
                || !TryProject(new(right, local.Y), out var topRight)
                || !TryProject(new(local.X, bottom), out var bottomLeft)
                || !TryProject(new(right, bottom), out var bottomRight)) return false;
            var left = Math.Min(Math.Min(topLeft.X, topRight.X), Math.Min(bottomLeft.X, bottomRight.X));
            var top = Math.Min(Math.Min(topLeft.Y, topRight.Y), Math.Min(bottomLeft.Y, bottomRight.Y));
            right = Math.Max(Math.Max(topLeft.X, topRight.X), Math.Max(bottomLeft.X, bottomRight.X));
            bottom = Math.Max(Math.Max(topLeft.Y, topRight.Y), Math.Max(bottomLeft.Y, bottomRight.Y));
            return NativeGalleryPolicy.TryIntersect(visible, new(left, top, right - left, bottom - top), out visible);
        }
        if (!IntersectMeasured(focus, new(0, 0, focus.ActualWidth, focus.ActualHeight))
            || !IntersectMeasured(page, new(0, 0, page.ActualWidth, page.ActualHeight))) return false;
        bool containsPage = false;
        for (DependencyObject? parent = focus; parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is UIElement element)
            {
                if (element.Visibility != Visibility.Visible) return false;
                if (element.Clip is { } clip
                    && !IntersectMeasured(element, new(clip.Rect.X, clip.Rect.Y, clip.Rect.Width, clip.Rect.Height), clip.Transform))
                    return false;
            }
            containsPage |= ReferenceEquals(parent, page);
            if (ReferenceEquals(parent, root)) return containsPage;
        }
        return false;
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
