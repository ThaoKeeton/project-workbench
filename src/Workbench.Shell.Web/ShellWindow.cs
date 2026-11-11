using System.Text.Json;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.Graphics;

namespace Workbench.Shell.Web;

/// <summary>
/// Thin WinUI 3 window. WinUI 3 is used instead of WPF so this shell stays on the
/// Windows App SDK already pinned by the existing host, and so the native window
/// keeps that host's 1,100px floor. The web UI itself still lays out from 640px.
/// </summary>
public sealed class ShellWindow : Window
{
    public const int MinimumWidth = 1100;
    public const int MinimumHeight = 640;
    private readonly WebView2 _web = new();
    private readonly IAsyncDisposable? _ownedService;
    private readonly Windows.UI.ViewManagement.UISettings _uiSettings = new();
    private bool _adjusting;

    public ShellWindow(IAsyncDisposable? ownedService)
    {
        _ownedService = ownedService;
        Title = "项目工作台";
        Content = _web;
        AppWindow.Title = "项目工作台";
        AppWindow.Resize(new SizeInt32(1180, 800));
        AppWindow.Changed += OnChanged;
        Closed += OnClosed;
        _uiSettings.ColorValuesChanged += (_, _) => DispatcherQueue.TryEnqueue(ApplyTitleBar);
        ApplyTitleBar();
    }

    public async Task AttachAsync(string baseUrl, string sessionToken)
    {
        try
        {
            await _web.EnsureCoreWebView2Async();
        }
        catch (Exception)
        {
            Content = new TextBlock
            {
                Text = "需要安装 WebView2 运行时后才能打开本地界面。",
                Margin = new Thickness(24),
                TextWrapping = TextWrapping.Wrap
            };
            return;
        }

        var core = _web.CoreWebView2;
        var literal = JsonSerializer.Serialize(sessionToken);
        await core.AddScriptToExecuteOnDocumentCreatedAsync($"sessionStorage.setItem('workbench.session', {literal});");
        var origin = new Uri(baseUrl).GetLeftPart(UriPartial.Authority);
        core.AddWebResourceRequestedFilter(origin + "/*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, eventArgs) =>
            eventArgs.Request.Headers.SetHeader("Authorization", "Bearer " + sessionToken);
        ApplyTitleBar();
        _web.Source = new Uri(baseUrl);
    }

    /// <summary>
    /// Matches the Windows app theme onto the native title bar. Not verified on Linux:
    /// this process cannot host WinUI or WebView2 there.
    /// </summary>
    private void ApplyTitleBar()
    {
        var background = _uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background);
        var dark = background.R + background.G + background.B < 384;
        var title = AppWindow.TitleBar;
        var bar = dark ? Color(0x11, 0x13, 0x17) : Color(0xFF, 0xFF, 0xFF);
        var ink = dark ? Color(0xEC, 0xEE, 0xF2) : Color(0x14, 0x17, 0x1C);
        var hover = dark ? Color(0x1C, 0x20, 0x26) : Color(0xEE, 0xF1, 0xF4);
        title.BackgroundColor = bar;
        title.ForegroundColor = ink;
        title.InactiveBackgroundColor = bar;
        title.InactiveForegroundColor = ink;
        title.ButtonBackgroundColor = bar;
        title.ButtonForegroundColor = ink;
        title.ButtonHoverBackgroundColor = hover;
        title.ButtonHoverForegroundColor = ink;
        title.ButtonInactiveBackgroundColor = bar;
        title.ButtonInactiveForegroundColor = ink;
        _web.DefaultBackgroundColor = bar;
    }

    private static Windows.UI.Color Color(byte red, byte green, byte blue)
        => Windows.UI.Color.FromArgb(255, red, green, blue);

    private void OnChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (_adjusting || !args.DidSizeChange) return;
        var size = sender.Size;
        var width = Math.Max(size.Width, MinimumWidth);
        var height = Math.Max(size.Height, MinimumHeight);
        if (width == size.Width && height == size.Height) return;
        _adjusting = true;
        sender.Resize(new SizeInt32(width, height));
        _adjusting = false;
    }

    private void OnClosed(Window sender, WindowEventArgs args)
    {
        if (_ownedService is not null)
            _ = _ownedService.DisposeAsync().AsTask();
    }
}
