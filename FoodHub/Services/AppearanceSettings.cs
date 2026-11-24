using System.IO;
using FoodHub.Core.Services;
using Microsoft.UI.Xaml;

namespace FoodHub.Services;

/// <summary>Stores the selected appearance in per-Windows-user application data.</summary>
public sealed class AppearanceSettings
{
    private readonly string _path;
    /// <summary>Selected theme; System is represented by Default.</summary>
    public ElementTheme Theme { get; private set; } = ElementTheme.Default;

    /// <summary>Loads a known value, falling back to System after corrupt or inaccessible data.</summary>
    public AppearanceSettings(ApplicationPaths paths)
    {
        _path = Path.Combine(paths.UserDirectory, "theme.txt");
        try { if (File.Exists(_path) && Enum.TryParse<ElementTheme>(File.ReadAllText(_path), out var theme) && Enum.IsDefined(theme)) Theme = theme; }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>Saves and applies appearance to all current app windows.</summary>
    public void SetTheme(ElementTheme theme)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, theme.ToString());
        Theme = theme;
        Apply(App.MainWindow);
        if (App.MainWindow.AuthenticationWindow is { } window) Apply(window);
    }

    /// <summary>Applies the current setting to the root element of a new window.</summary>
    public void Apply(Window window)
    {
        if (window.Content is FrameworkElement root) root.RequestedTheme = Theme;
    }
}