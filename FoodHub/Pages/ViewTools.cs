using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FoodHub.Pages;

/// <summary>Small view-only helpers for receipt and basket presentations.</summary>
internal static class ViewTools
{
    /// <summary>Finds a realized native scrolling surface without relying on a template name.</summary>
    internal static T? Find<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is T result) return result;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (Find<T>(VisualTreeHelper.GetChild(parent, i)) is { } child) return child;
        return null;
    }
    /// <summary>Creates wrapping text that follows system text scaling.</summary>
    internal static TextBlock Text(string text, double size = 14) => new() { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = size };
    /// <summary>Wraps a group in the application's standard themed card surface.</summary>
    internal static Border Card(UIElement content) => new()
    {
        Child = content, Padding = new Thickness(20), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1),
        Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
        BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"]
    };
}