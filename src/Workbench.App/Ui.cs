using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Workbench.App;

internal static class Ui
{
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
        Padding = new Thickness(20),
        CornerRadius = new CornerRadius(8),
        BorderThickness = new Thickness(1),
        BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
        Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"]
    };

    public static T Named<T>(T element, string name) where T : DependencyObject
    {
        AutomationProperties.SetName(element, name);
        return element;
    }
}
