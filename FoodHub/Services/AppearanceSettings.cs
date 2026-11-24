using System.IO;
using System.Text;
using FoodHub.Core.Services;
using Microsoft.UI.Xaml;

namespace FoodHub.Services;

/// <summary>Stores the selected appearance in per-Windows-user application data.</summary>
public sealed class AppearanceSettings
{
    private readonly ILocalStorageService _storage;
    /// <summary>Selected theme; System is represented by Default.</summary>
    public ElementTheme Theme { get; private set; } = ElementTheme.Default;

    /// <summary>Captures storage without doing file I/O during dependency construction.</summary>
    public AppearanceSettings(ILocalStorageService storage) => _storage = storage;

    /// <summary>Loads a known value before the first window is constructed; invalid data falls back to System.</summary>
    public async Task LoadAsync(CancellationToken cancellation = default)
    {
        try
        {
            var bytes = await _storage.ReadAsync("theme.txt", cancellation);
            if (bytes is not null && Enum.TryParse<ElementTheme>(Encoding.UTF8.GetString(bytes), out var theme) && Enum.IsDefined(theme)) Theme = theme;
        }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>Atomically saves and applies appearance to all current windows.</summary>
    public async Task SetThemeAsync(ElementTheme theme)
    {
        await _storage.WriteAsync("theme.txt", Encoding.UTF8.GetBytes(theme.ToString()));
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