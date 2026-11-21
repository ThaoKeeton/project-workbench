using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Exceptions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using Workbench.TestExecutor;

namespace Workbench.TestExecutor.Windows;

public sealed class FlaUiSessionFactory : IUiAutomationSessionFactory
{
    public Task<IUiAutomationSession> OpenAsync(ApplicationTarget? application, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (application?.ProcessName is not { Length: > 0 })
            return Task.FromResult<IUiAutomationSession>(new MissingSession());
        var process = Process.GetProcessesByName(application.ProcessName).FirstOrDefault(item => !item.HasExited);
        if (process is null) return Task.FromResult<IUiAutomationSession>(new MissingSession());
        return Task.FromResult<IUiAutomationSession>(new FlaUiSession(process, application.WindowTitle));
    }

    private sealed class MissingSession : IUiAutomationSession
    {
        public Task<UiActionResult> ExecuteAsync(UiAction action, CancellationToken cancellationToken)
            => Task.FromResult(UiActionResult.Fail(ExecutorError.ControlNotFound, true, "process-not-found"));

        public Task<RawFrame> CaptureAsync(CancellationToken cancellationToken)
            => Task.FromResult(SyntheticFrames.PasswordField());

        public PixelRect? Locate(ElementSelector selector) => null;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

public sealed class FlaUiSession : IUiAutomationSession
{
    private readonly Process _process;
    private readonly int _processId;
    private readonly string? _windowTitle;
    private UIA3Automation? _automation;
    private Application? _application;
    private AutomationElement? _window;

    public FlaUiSession(Process process, string? windowTitle)
    {
        _process = process;
        // GetMainWindow disposes the Process it was given, so keep the id separately.
        _processId = process.Id;
        _windowTitle = windowTitle;
    }

    public Task<UiActionResult> ExecuteAsync(UiAction action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var element = Find(action.Target);
            if (element is null) return Task.FromResult(Missing(action));
            return Task.FromResult(Perform(element, action));
        }
        catch (Exception error) when (error is ElementNotAvailableException or COMException or TimeoutException)
        {
            return Task.FromResult(UiActionResult.Fail(ExecutorError.TreeUnstable, true, "tree-unstable"));
        }
    }

    public Task<RawFrame> CaptureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var window = Window();
        using var bitmap = window.Capture();
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return Task.FromResult(new RawFrame(stream.ToArray(), PasswordRegions(window)));
    }

    public PixelRect? Locate(ElementSelector selector)
    {
        var element = Find(selector);
        return element is null ? null : ToLocal(Window(), element);
    }

    public ValueTask DisposeAsync()
    {
        _automation?.Dispose();
        _application?.Dispose();
        return ValueTask.CompletedTask;
    }

    private UiActionResult Perform(AutomationElement element, UiAction action)
    {
        switch (action.Name)
        {
            case "click":
                element.Click();
                return UiActionResult.Ok(element.Name);
            case "invoke":
                element.Patterns.Invoke.Pattern.Invoke();
                return UiActionResult.Ok(element.Name);
            case "expand":
                if (element.Patterns.ExpandCollapse.IsSupported) element.Patterns.ExpandCollapse.Pattern.Expand();
                else element.Click();
                return UiActionResult.Ok(element.Name);
            case "set-value":
                element.Patterns.Value.Pattern.SetValue(action.Text ?? "");
                return UiActionResult.Ok(action.Sensitive ? "[masked]" : action.Text);
            case "type":
                element.Focus();
                if (element.Patterns.Value.IsSupported) element.Patterns.Value.Pattern.SetValue(action.Text ?? "");
                else Keyboard.Type(action.Text ?? "");
                return UiActionResult.Ok(action.Sensitive ? "[masked]" : action.Text);
            case "wait-for":
                return UiActionResult.Ok(element.Name);
            case "read":
                var actual = element.Patterns.Value.IsSupported ? element.Patterns.Value.Pattern.Value : element.Name;
                return UiActionResult.Ok(actual);
            case "open-file":
                return FillFileDialog(action);
            default:
                return UiActionResult.Fail(ExecutorError.ActionFailed, false, "unknown-action");
        }
    }

    private static UiActionResult Missing(UiAction action)
        => UiActionResult.Fail(ExecutorError.ControlNotFound, true, action.Target?.Display ?? "missing");

    private UiActionResult FillFileDialog(UiAction action)
    {
        var automation = _automation ??= new UIA3Automation();
        var dialog = FindFileDialog(automation);
        if (dialog is null) return UiActionResult.Fail(ExecutorError.ControlNotFound, true, "dialog-missing");
        var filename = dialog.FindFirstDescendant(cf => cf.ByAutomationId("1148"));
        if (filename is null) return UiActionResult.Fail(ExecutorError.ControlNotFound, true, "filename-missing");
        var handle = NativeHandle(dialog);
        var path = action.Text ?? "";
        BringToFront(dialog, handle);
        InputFileName(filename, path);
        Try(() => filename.Focus());
        Keyboard.Press(VirtualKeyShort.RETURN);
        if (Closed(handle)) return UiActionResult.Ok("file-selected");
        return UiActionResult.Fail(ExecutorError.ActionFailed, true, Describe(dialog, filename, handle));
    }

    private AutomationElement? FindFileDialog(UIA3Automation automation)
    {
        var desktop = automation.GetDesktop();
        var processId = _processId;
        return desktop.FindFirstDescendant(cf => cf.ByProcessId(processId).And(cf.ByClassName("#32770")));
    }

    private static void InputFileName(AutomationElement filename, string path)
    {
        var edit = filename.ControlType == ControlType.Edit
            ? filename
            : filename.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit)) ?? filename;
        Try(() => edit.Focus());
        Try(() => WriteValue(filename, path));
        if (!ReferenceEquals(edit, filename)) Try(() => WriteValue(edit, path));
        if (!HasFocus(edit)) Try(() => filename.Focus());
        if (!HasFocus(edit) && !HasFocus(filename)) return;
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        if (TryPlaceOnClipboard(path)) Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_V);
        else Keyboard.Type(path);
    }

    private static void WriteValue(AutomationElement filename, string path)
    {
        if (filename.Patterns.Value.IsSupported) filename.Patterns.Value.Pattern.SetValue(path);
    }

    private static string Describe(AutomationElement dialog, AutomationElement filename, IntPtr handle)
    {
        var value = ReadValue(filename).Replace('\r', ' ').Replace('\n', ' ');
        if (value.Length > 180) value = value[..180];
        var alive = handle != IntPtr.Zero && IsWindow(handle);
        return $"dialog-open title={dialog.Name} type={filename.ControlType} value={value} focus={HasFocus(filename)} alive={alive}";
    }

    private static void BringToFront(AutomationElement dialog, IntPtr handle)
    {
        Keyboard.Press(VirtualKeyShort.LMENU);
        if (handle != IntPtr.Zero) ForceForeground(handle);
        Try(() => dialog.Focus());
    }

    private static void ForceForeground(IntPtr handle)
    {
        var foreground = GetForegroundWindow();
        var current = GetCurrentThreadId();
        var source = GetWindowThreadProcessId(foreground, out _);
        var target = GetWindowThreadProcessId(handle, out _);
        AttachThreadInput(current, source, true);
        AttachThreadInput(current, target, true);
        SetForegroundWindow(handle);
        AttachThreadInput(current, target, false);
        AttachThreadInput(current, source, false);
    }

    private static bool Closed(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return false;
        for (var attempt = 0; attempt < 30; attempt++)
        {
            if (!IsWindow(handle)) return true;
            Thread.Sleep(100);
        }
        return false;
    }

    private static string ReadValue(AutomationElement element)
    {
        if (!element.Patterns.Value.IsSupported) return element.Name ?? "";
        try { return element.Patterns.Value.Pattern.Value ?? ""; }
        catch (Exception error) when (error is COMException or ElementNotAvailableException)
        {
            return "";
        }
    }

    private static bool HasFocus(AutomationElement element)
    {
        try { return element.Properties.HasKeyboardFocus.ValueOrDefault; }
        catch (Exception error) when (error is COMException or ElementNotAvailableException)
        {
            return false;
        }
    }

    private static IntPtr NativeHandle(AutomationElement element)
    {
        try { return element.Properties.NativeWindowHandle.ValueOrDefault; }
        catch (Exception error) when (error is COMException or ElementNotAvailableException)
        {
            return IntPtr.Zero;
        }
    }

    private static void Try(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is COMException or ElementNotAvailableException or TimeoutException or InvalidOperationException)
        {
            // One confirmation gesture can miss while the dialog is opening. The next one still runs.
        }
    }

    private static bool TryPlaceOnClipboard(string text)
    {
        if (!OpenClipboard(IntPtr.Zero)) return false;
        var memory = IntPtr.Zero;
        try
        {
            if (!EmptyClipboard()) return false;
            memory = GlobalAlloc(GmemMoveable | GmemZeroInit, (nuint)((text.Length + 1) * sizeof(char)));
            if (memory == IntPtr.Zero) return false;
            var locked = GlobalLock(memory);
            if (locked == IntPtr.Zero) return false;
            try { Marshal.Copy(text.ToCharArray(), 0, locked, text.Length); }
            finally { GlobalUnlock(memory); }
            if (SetClipboardData(CfUnicodeText, memory) == IntPtr.Zero) return false;
            memory = IntPtr.Zero;
            return true;
        }
        finally
        {
            if (memory != IntPtr.Zero) GlobalFree(memory);
            CloseClipboard();
        }
    }

    private AutomationElement? Find(ElementSelector? selector)
    {
        if (selector is null) return null;
        var window = Window();
        AutomationElement? found = null;
        if (selector.AutomationId is { Length: > 0 })
        {
            var automationId = selector.AutomationId;
            found = window.FindFirstDescendant(cf => cf.ByAutomationId(automationId))
                ?? window.FindFirstDescendant(cf => cf.ByName(automationId));
        }
        if (found is null && selector.Name is { Length: > 0 })
            found = window.FindFirstDescendant(cf => cf.ByName(selector.Name));
        return found;
    }

    private AutomationElement Window()
    {
        if (_window is not null) return _window;
        _automation ??= new UIA3Automation();
        _application ??= Application.Attach(_process);
        var window = _application.GetMainWindow(_automation, TimeSpan.FromSeconds(10));
        if (window is null) throw new ElementNotAvailableException("window");
        if (_windowTitle is { Length: > 0 } && window.Name?.Contains(_windowTitle, StringComparison.Ordinal) != true)
            throw new ElementNotAvailableException("window");
        _window = window;
        return window;
    }

    private static IReadOnlyList<PixelRect> PasswordRegions(AutomationElement window)
    {
        var regions = new List<PixelRect>();
        foreach (var edit in window.FindAllDescendants(cf => cf.ByControlType(ControlType.Edit)))
        {
            if (edit.Properties.IsPassword.ValueOrDefault) regions.Add(ToLocal(window, edit));
        }
        return regions;
    }

    private static PixelRect ToLocal(AutomationElement window, AutomationElement element)
    {
        var bounds = window.BoundingRectangle;
        var rect = element.BoundingRectangle;
        return new PixelRect((int)(rect.X - bounds.X), (int)(rect.Y - bounds.Y), (int)rect.Width, (int)rect.Height);
    }

    private const uint GmemMoveable = 0x0002;
    private const uint GmemZeroInit = 0x0040;
    private const uint CfUnicodeText = 13;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint source, uint target, bool attach);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    private static extern IntPtr SetClipboardData(uint format, IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, nuint bytes);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr memory);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr memory);
}
