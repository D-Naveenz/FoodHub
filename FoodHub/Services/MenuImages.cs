using FoodHub.Core.Services;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI.ViewManagement;

namespace FoodHub.Services;

/// <summary>Resolves bundled local menu artwork and provides animation preferences at the view boundary.</summary>
public static class MenuImages
{
    /// <summary>Whether Windows permits decorative motion.</summary>
    public static bool AnimationsEnabled => new UISettings().AnimationsEnabled;
    /// <summary>Displays local assets only; invalid and failed images use the bundled fallback.</summary>
    public static void Set(Image image, MealDetails meal)
    {
        var path = meal.PhotoPath?.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("Assets/Menu/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("..") || path.Contains(':')) path = "Assets/Menu/fallback.svg";
        if (!Uri.TryCreate("ms-appx:///" + path, UriKind.Absolute, out var uri))
            uri = new Uri("ms-appx:///Assets/Menu/fallback.svg");
        image.Source = path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? new SvgImageSource(uri) : new BitmapImage(uri);
    }
    /// <summary>Installs a decode-failure fallback once per view image.</summary>
    public static void AttachFallback(Image image) => image.ImageFailed += (_, _) =>
        image.Source = new SvgImageSource(new Uri("ms-appx:///Assets/Menu/fallback.svg"));
    /// <summary>Uses an explicit currency code so the fictional store does not inherit Windows currency.</summary>
    public static string Money(decimal amount, string? currency) => $"{currency ?? "LKR"} {amount:N2}";
}
