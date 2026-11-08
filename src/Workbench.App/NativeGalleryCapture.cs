using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Workbench.App;

// This is called only by the existing isolated synthetic probes. It never creates a page,
// targets another window, reads a credential, or changes the original probe verdict.
internal sealed partial class MainWindow
{
    private int _galleryAttempts;
    private readonly HashSet<string> _galleryIds = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions GalleryJson = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly Regex GalleryId = new("^[a-z][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant);

    internal async Task SmokeCaptureGalleryAsync(RunOptions options, string shotId, string pageKey,
        string? pluginId, string? viewId, FrameworkElement displayedPage, string witnessCode = "asserted",
        int witnessCount = 1, FrameworkElement? focus = null, bool requireFocus = false)
    {
        // A bad argument must never cause a path escape or a non-probe capture.
        if (!options.IsIsolated || options.ReportPath is null || !GalleryId.IsMatch(shotId)
            || !GalleryId.IsMatch(pageKey) || !GalleryId.IsMatch(witnessCode) || witnessCount < 0
            || ++_galleryAttempts > 32 || !_galleryIds.Add(shotId)) return;
        var phase = options.SmokeTest ? "native-smoke" : options.WorkspaceProbe is not null ? "workspace-" + options.WorkspaceProbe
            : options.TestingProbe is not null ? "testing-" + options.TestingProbe : options.BusinessProbe is not null ? "business-" + options.BusinessProbe : "unavailable";
        var directory = Path.Combine(Path.GetDirectoryName(options.ReportPath)!, "gallery", phase);
        var receiptPath = Path.Combine(directory, shotId + ".json");
        var pngPath = Path.Combine(directory, shotId + ".png");
        string status = "blocked", reasonCode = "precondition-unavailable";
        int width = 0, height = 0, pngBytes = 0;
        string? pngSha256 = null;
        long hwndValue = 0, birth = 0;
        int processId = Environment.ProcessId, sessionId = 0;
        bool desktopVisible = false, windowVisible = false;
        double rasterizationScale = 0, viewportWidth = 0, viewportHeight = 0, scrollOffset = 0;
        string? exeHash = null, informationalVersion = null, sourceCommit = null, manifestHash = null, entryHash = null;
        string? selectedPageKey = null, selectedPluginId = null, selectedViewId = null;
        var captureUtc = DateTimeOffset.UtcNow;
        ScrollViewer? scroller = null;
        double originalOffset = 0;
        HostRuntime? originalRuntime = null;
        long originalGeneration = 0;
        bool restorationAttempted = false;
        bool PageLive() => GalleryLive(displayedPage, hwndValue, birth, originalRuntime, originalGeneration, pageKey, pluginId, viewId);
        bool FocusAttached() => focus is null || (scroller is not null
            && ReferenceEquals(GalleryFocusScroller(displayedPage, focus), scroller));
        bool CaptureLive(bool requireVisible = true)
        {
            if (!PageLive()) { status = "blocked"; reasonCode = "page-detached"; return false; }
            if (!FocusAttached()) { status = "blocked"; reasonCode = "focus-detached"; return false; }
            if (requireVisible && focus is not null && !GalleryFocusInViewport(scroller!, focus))
            { status = "blocked"; reasonCode = "focus-outside-viewport"; return false; }
            return true;
        }
        async Task<bool> RestoreAsync()
        {
            restorationAttempted = true;
            return scroller is null || await GalleryChangeViewAsync(scroller, originalOffset,
                () => PageLive() && FocusAttached(), () => true);
        }
        try
        {
            AssertUiThread();
            status = "blocked"; reasonCode = "page-detached";
            if (_closing || _stopped || _busy || _runtime is null || !_initialized
                || !ReferenceEquals(_content.Content, displayedPage) || !displayedPage.IsLoaded
                || displayedPage.XamlRoot is null || !ReferenceEquals(displayedPage.XamlRoot, _root.XamlRoot)
                || !ReferenceEquals(Content, _root)) return;
            originalRuntime = _runtime;
            originalGeneration = _runtimeGeneration;
            var selected = _navigation.SelectedItem;
            NavigationTarget? selectedTarget = selected == _navigation.SettingsItem ? new("settings")
                : (selected as NavigationViewItem)?.Tag as NavigationTarget;
            selectedPageKey = selectedTarget?.Page;
            selectedPluginId = selectedTarget?.PluginId;
            selectedViewId = selectedTarget?.ViewId;
            if (selectedPageKey != pageKey || selectedPluginId != pluginId || selectedViewId != viewId) return;

            status = "blocked"; reasonCode = "desktop-unavailable";
            desktopVisible = GalleryInputDesktopReadable();
            if (!desktopVisible) return;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            hwndValue = hwnd.ToInt64();
            if (hwnd == IntPtr.Zero || GetWindowThreadProcessId(hwnd, out var owner) == 0
                || owner != (uint)processId || !IsWindowVisible(hwnd) || !GetClientRect(hwnd, out var rect)
                || rect.Right <= rect.Left || rect.Bottom <= rect.Top) return;
            windowVisible = true;
            using (var process = Process.GetCurrentProcess())
            {
                birth = process.StartTime.ToUniversalTime().ToFileTimeUtc();
                sessionId = process.SessionId;
            }
            if (birth <= 0) return;
            var executable = Environment.ProcessPath;
            if (executable is null || !File.Exists(executable)) return;
            exeHash = ShaFile(executable);
            informationalVersion = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            var match = Regex.Match(informationalVersion ?? "", @"\+([0-9a-f]{40})(?:$|\.)", RegexOptions.CultureInvariant);
            sourceCommit = match.Success ? match.Groups[1].Value : null;
            if (sourceCommit is null) return;
            if (pluginId is not null)
            {
                var snapshot = Manager.Plugins.SingleOrDefault(p => p.Manifest?.Id == pluginId);
                if (snapshot?.Manifest is null) return;
                var manifest = Path.Combine(snapshot.PackagePath, "plugin.json");
                var entry = Path.Combine(snapshot.PackagePath, snapshot.Manifest.EntryAssembly);
                if (!File.Exists(manifest) || !File.Exists(entry)) return;
                manifestHash = ShaFile(manifest);
                entryHash = ShaFile(entry);
            }

            if (requireFocus && focus is null)
            {
                status = "blocked"; reasonCode = "focus-detached"; return;
            }
            _root.UpdateLayout();
            if (focus is not null)
            {
                status = "blocked"; reasonCode = "focus-detached";
                scroller = GalleryFocusScroller(displayedPage, focus);
                if (scroller is null) return;
                originalOffset = scroller.VerticalOffset;
                status = "blocked"; reasonCode = "focus-outside-viewport";
                if (!GalleryTryFocusBounds(scroller, focus, out var bounds)) return;
                var target = bounds.Y >= 0 && bounds.Bottom <= scroller.ViewportHeight ? originalOffset
                    : Math.Clamp(originalOffset + bounds.Y - Math.Max(0, (scroller.ViewportHeight - bounds.Height) / 2),
                        0, scroller.ScrollableHeight);
                reasonCode = "layout-unavailable";
                var positioned = await GalleryChangeViewAsync(scroller, target,
                    () => PageLive() && FocusAttached(), () => GalleryFocusInViewport(scroller, focus));
                viewportWidth = scroller.ViewportWidth;
                viewportHeight = scroller.ViewportHeight;
                scrollOffset = scroller.VerticalOffset;
                if (!CaptureLive() || !positioned) return;
            }
            else
            {
                await Task.Yield();
                if (!CaptureLive()) return;
                await Task.Yield();
                if (!CaptureLive()) return;
                _root.UpdateLayout();
                viewportWidth = _root.ActualWidth; viewportHeight = _root.ActualHeight;
            }
            rasterizationScale = _root.XamlRoot?.RasterizationScale ?? 0;
            status = "blocked"; reasonCode = "layout-unavailable";
            if (viewportWidth <= 0 || viewportHeight <= 0 || _root.ActualWidth <= 0 || _root.ActualHeight <= 0
                || _root.ActualWidth * rasterizationScale > NativeGalleryPolicy.MaxDimension
                || _root.ActualHeight * rasterizationScale > NativeGalleryPolicy.MaxDimension
                || !CaptureLive()) return;

            status = "failed"; reasonCode = "render-invalid";
            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(_root);
            if (!CaptureLive()) return;
            width = bitmap.PixelWidth;
            height = bitmap.PixelHeight;
            if (width is < 1 or > NativeGalleryPolicy.MaxDimension || height is < 1 or > NativeGalleryPolicy.MaxDimension
                || (long)width * height * 4 > NativeGalleryPolicy.MaxBgraBytes) return;
            var buffer = await bitmap.GetPixelsAsync();
            if (!CaptureLive()) return;
            var pixels = new byte[buffer.Length];
            using (var reader = DataReader.FromBuffer(buffer)) reader.ReadBytes(pixels);
            if (!NativeGalleryPolicy.IsValidFrame(pixels, width, height)) return;

            using var stream = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            if (!CaptureLive()) return;
            // RenderTargetBitmap supplies premultiplied BGRA8. Tell the codec the
            // input alpha convention; PNG retains the actual content transparency.
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)width, (uint)height, 96, 96, pixels);
            await encoder.FlushAsync();
            if (!CaptureLive() || stream.Size is < 1 or > NativeGalleryPolicy.MaxPngBytes) return;
            stream.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            if (!CaptureLive() || decoder.PixelWidth != width || decoder.PixelHeight != height) return;
            var bytes = new byte[(int)stream.Size];
            using (var reader = new DataReader(stream.GetInputStreamAt(0)))
            {
                await reader.LoadAsync((uint)bytes.Length);
                if (!CaptureLive()) return;
                reader.ReadBytes(bytes);
            }
            if (bytes.Length < 24
                || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
                || ShaFile(executable) != exeHash) return;
            // Finish the owned scroll work before publishing bytes. A rejected or
            // unsettled restore must not leave a final-name PNG behind.
            status = "blocked"; reasonCode = "layout-unavailable";
            var restored = await RestoreAsync();
            if (!CaptureLive(requireVisible: false) || !restored) return;
            Directory.CreateDirectory(directory);
            var candidateHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            // No await or live-failure return follows CreateNew: a desktop transition
            // during rendering/encoding cannot leave a rejected final-name PNG.
            if (!CaptureLive(requireVisible: false)) return;
            using (var output = new FileStream(pngPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                output.Write(bytes);
            pngBytes = bytes.Length;
            pngSha256 = candidateHash;
            status = "captured"; reasonCode = "none";
        }
        catch (Exception)
        {
            status = "failed"; reasonCode = "capture-error";
        }
        finally
        {
            if (scroller is not null && !restorationAttempted)
            {
                try { await RestoreAsync(); } catch (Exception) { }
            }
            // A receipt is immutable. If creation fails, the collector treats any image as an orphan.
            try
            {
                Directory.CreateDirectory(directory);
                var receipt = new {
                    schemaVersion = 1, captureKind = "WinUiContentRender", scope = "actual-window-content-only",
                    titleBarIncluded = false, popupsIncluded = false, status, reasonCode, shotId, pageKey, pluginId, viewId,
                    probeKind = options.SmokeTest ? "smoke" : options.WorkspaceProbe is not null ? "workspace"
                        : options.TestingProbe is not null ? "testing" : "business",
                    phase, projectId = _workspace?.ActiveProject?.Id, selectedPageKey, selectedPluginId, selectedViewId,
                    processId, processCreationFileTimeUtc = birth, hwnd = hwndValue, captureUtc,
                    executableSha256 = exeHash, hostInformationalVersion = informationalVersion, sourceCommit,
                    packageManifestSha256 = manifestHash, packageEntrySha256 = entryHash,
                    osDescription = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                    sessionId, desktopVisible, windowVisible, width, height, rasterizationScale,
                    theme = _root.ActualTheme.ToString(), viewportWidth, viewportHeight, scrollOffset,
                    witnessCode, witnessCount, pngPath = status == "captured" ? shotId + ".png" : null,
                    pngBytes = status == "captured" ? pngBytes : 0, pngSha256
                };
                using var output = new FileStream(receiptPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await JsonSerializer.SerializeAsync(output, receipt, GalleryJson);
            }
            catch (Exception) { /* The original native probe owns its original verdict. */ }
        }
    }

    private bool GalleryLive(FrameworkElement page, long hwndValue, long birth, HostRuntime? originalRuntime,
        long originalGeneration, string pageKey, string? pluginId, string? viewId)
    {
        if (_closing || _stopped || _busy || !ReferenceEquals(_content.Content, page) || !page.IsLoaded
            || !ReferenceEquals(page.XamlRoot, _root.XamlRoot) || !ReferenceEquals(Content, _root)
            || !ReferenceEquals(_runtime, originalRuntime) || _runtimeGeneration != originalGeneration) return false;
        var selected = _navigation.SelectedItem;
        NavigationTarget? target = selected == _navigation.SettingsItem ? new("settings")
            : (selected as NavigationViewItem)?.Tag as NavigationTarget;
        if (target?.Page != pageKey || target.PluginId != pluginId || target.ViewId != viewId) return false;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (!GalleryInputDesktopReadable() || hwnd.ToInt64() != hwndValue || !IsWindowVisible(hwnd)
            || !GetClientRect(hwnd, out var rect) || rect.Right <= rect.Left || rect.Bottom <= rect.Top
            || GetWindowThreadProcessId(hwnd, out var owner) == 0
            || owner != (uint)Environment.ProcessId) return false;
        using var process = Process.GetCurrentProcess();
        return process.StartTime.ToUniversalTime().ToFileTimeUtc() == birth;
    }

    private static bool GalleryInputDesktopReadable()
    {
        if (!Environment.UserInteractive) return false;
        var desktop = OpenInputDesktop(0, false, 0x0001);
        return desktop != IntPtr.Zero && CloseDesktop(desktop);
    }

    private static string ShaFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    [StructLayout(LayoutKind.Sequential)]
    private struct GalleryRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetClientRect(IntPtr window, out GalleryRect rect);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
