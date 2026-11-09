using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Workbench.App;

internal sealed partial class MainWindow
{
    private void InitializeShellTitleBar()
    {
        Title = "Project Workbench";
        var titleBar = Ui.Named(new TitleBar
        {
            Title = "Project Workbench",
            IconSource = new ImageIconSource
            {
                ImageSource = new SvgImageSource(new Uri("ms-appx:///Assets/Workbench.svg"))
            },
            IsBackButtonVisible = false,
            IsPaneToggleButtonVisible = false
        }, "工作台标题栏");

        // Keep the existing notice, project header and body in their own rows.
        foreach (FrameworkElement child in _root.Children) Grid.SetRow(child, Grid.GetRow(child) + 1);
        _root.RowDefinitions.Insert(0, new RowDefinition { Height = GridLength.Auto });
        _root.Children.Insert(0, titleBar);
        _navigation.IsTitleBarAutoPaddingEnabled = false;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(titleBar); // WinUI owns drag regions; Windows retains its caption buttons.
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Workbench.ico"));

        // ThemeResource keeps the fallback readable as the existing host theme changes.
        _root.Style = (Style)XamlReader.Load("""
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Grid">
                <Setter Property="Background" Value="{ThemeResource ApplicationPageBackgroundThemeBrush}" />
            </Style>
            """);
        if (MicaController.IsSupported())
        {
            SystemBackdrop = new MicaBackdrop();
            _root.Background = new SolidColorBrush(Colors.Transparent);
        }

        _root.ActualThemeChanged += (_, _) => ApplyShellTitleBarTheme();
        titleBar.Loaded += (_, _) => ApplyShellTitleBarTheme();
        ApplyShellTitleBarTheme();
    }

    private void ApplyShellTitleBarTheme()
    {
        var dark = _root.ActualTheme == ElementTheme.Dark;
        var caption = AppWindow.TitleBar;
        var foreground = dark ? Colors.White : Colors.Black;
        caption.ButtonBackgroundColor = Colors.Transparent;
        caption.ButtonInactiveBackgroundColor = Colors.Transparent;
        caption.ButtonForegroundColor = foreground;
        caption.ButtonInactiveForegroundColor = dark
            ? Windows.UI.Color.FromArgb(255, 153, 153, 153)
            : Windows.UI.Color.FromArgb(255, 102, 102, 102);
        caption.ButtonHoverForegroundColor = foreground;
        caption.ButtonPressedForegroundColor = foreground;
        caption.ButtonHoverBackgroundColor = dark
            ? Windows.UI.Color.FromArgb(255, 58, 58, 58)
            : Windows.UI.Color.FromArgb(255, 230, 230, 230);
        caption.ButtonPressedBackgroundColor = dark
            ? Windows.UI.Color.FromArgb(255, 48, 48, 48)
            : Windows.UI.Color.FromArgb(255, 217, 217, 217);
    }
}
