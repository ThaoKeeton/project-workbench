using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Exceptions;
using FlaUI.Core.Input;
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
    private readonly string? _windowTitle;
    private UIA3Automation? _automation;
    private Application? _application;
    private AutomationElement? _window;

    public FlaUiSession(Process process, string? windowTitle)
    {
        _process = process;
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
                element.Patterns.ExpandCollapse.Pattern.Expand();
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
        var dialog = automation.GetDesktop().FindFirstDescendant(cf => cf.ByClassName("#32770"));
        if (dialog is null) return UiActionResult.Fail(ExecutorError.ControlNotFound, true, "dialog-missing");
        var filename = dialog.FindFirstDescendant(cf => cf.ByAutomationId("1148"))
            ?? dialog.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit));
        if (filename is null || !filename.Patterns.Value.IsSupported)
            return UiActionResult.Fail(ExecutorError.ControlNotFound, true, "filename-missing");
        filename.Patterns.Value.Pattern.SetValue(action.Text ?? "");
        var open = dialog.FindFirstDescendant(cf => cf.ByName("打开"))
            ?? dialog.FindFirstDescendant(cf => cf.ByName("Open"));
        if (open is null) return UiActionResult.Fail(ExecutorError.ActionFailed, false, "open-button-missing");
        if (open.Patterns.Invoke.IsSupported) open.Patterns.Invoke.Pattern.Invoke();
        else open.Click();
        return UiActionResult.Ok("file-selected");
    }

    private AutomationElement? Find(ElementSelector? selector)
    {
        if (selector is null) return null;
        var window = Window();
        AutomationElement? found = null;
        if (selector.AutomationId is { Length: > 0 })
            found = window.FindFirstDescendant(cf => cf.ByAutomationId(selector.AutomationId));
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
        if (_windowTitle is { Length: > 0 } && !string.Equals(window.Name, _windowTitle, StringComparison.Ordinal))
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
}
