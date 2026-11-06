using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace Workbench.App;

internal static class Ui
{
    // Fixed host-owned style only. ThemeResource resolves against each card's actual window theme.
    // Independent plugins remain code-only and do not load external XAML/PRI resources.
    private static readonly Lazy<Style> CardStyle = new(() => (Style)XamlReader.Load("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Border">
            <Setter Property="Padding" Value="20" />
            <Setter Property="CornerRadius" Value="8" />
            <Setter Property="BorderThickness" Value="1" />
            <Setter Property="BorderBrush" Value="{ThemeResource CardStrokeColorDefaultBrush}" />
            <Setter Property="Background" Value="{ThemeResource CardBackgroundFillColorDefaultBrush}" />
        </Style>
        """));

    public static TextBlock Text(string text, double size = 14) => new()
    {
        Text = text,
        FontSize = size,
        TextWrapping = TextWrapping.Wrap,
        IsTextSelectionEnabled = true
    };

    public static StackPanel Page(string title, string description)
    {
        var panel = new StackPanel { Spacing = 16, Padding = new Thickness(28), HorizontalAlignment = HorizontalAlignment.Stretch };
        panel.Children.Add(Text(title, 28));
        panel.Children.Add(Text(description));
        return panel;
    }

    public static Border Card(UIElement child) => new()
    {
        Child = child,
        Style = CardStyle.Value
    };

    public static T Named<T>(T element, string name) where T : DependencyObject
    {
        AutomationProperties.SetName(element, name);
        return element;
    }
}
