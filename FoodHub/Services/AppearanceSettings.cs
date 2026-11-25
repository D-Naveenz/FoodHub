using Dhara.Extensions.WinUI.Windowing;
using FoodHub.Core.Services;
using Microsoft.Extensions.Configuration;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;

namespace FoodHub.Services;

/// <summary>Stores the selected appearance in per-Windows-user application data.</summary>
public sealed class AppearanceSettings
{
    private readonly LocalSettingsStore _settings;
    private readonly IConfiguration _configuration;
    private readonly WindowThemeService _themes;
    /// <summary>Selected theme; System is represented by Default.</summary>
    public ElementTheme Theme { get; private set; } = ElementTheme.Default;

    /// <summary>Captures the general configuration and shared writer without performing I/O.</summary>
    /// <param name="configuration">The effective launch configuration, including bundled appearance defaults.</param>
    /// <param name="themes">Reusable runtime theme application; persistence remains in FoodHub.</param>
    /// <param name="settings">The same writer used by database settings, preventing lost concurrent edits.</param>
    public AppearanceSettings(IConfiguration configuration, LocalSettingsStore settings, WindowThemeService themes)
    {
        _configuration = configuration;
        _settings = settings;
        _themes = themes;
    }

    /// <summary>Loads a known value before the first window is constructed; invalid data falls back to System.</summary>
    /// <param name="cancellation">Allows cancelled startup to stop before allocating native windows.</param>
    /// <returns>A completed task after reading the already loaded configuration snapshot.</returns>
    public Task LoadAsync(CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        Theme = _configuration["Appearance:Theme"] switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        _themes.SetTheme(Theme);
        return Task.CompletedTask;
    }

    /// <summary>Atomically saves and applies appearance to all current windows.</summary>
    /// <param name="theme">A defined WinUI theme; Default is persisted as the platform-independent string System.</param>
    /// <returns>A task completing after persistence and application to current windows.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The theme is not a defined value.</exception>
    /// <exception cref="System.IO.IOException">Local settings are malformed or could not be saved.</exception>
    /// <remarks>Failed writes leave the active theme unchanged. No host-wide configuration reload occurs.</remarks>
    public async Task SetThemeAsync(ElementTheme theme)
    {
        if (!Enum.IsDefined(theme)) throw new ArgumentOutOfRangeException(nameof(theme));
        await _settings.UpdateAsync(json =>
        {
            if (json["Appearance"] is not JsonObject) json["Appearance"] = new JsonObject();
            json["Appearance"]!["Theme"] = theme == ElementTheme.Default ? "System" : theme.ToString();
        });
        // Failed persistence leaves both the selected theme and existing windows unchanged.
        Theme = theme;
        _themes.SetTheme(theme);
    }

    /// <summary>Applies the current setting to the root element of a new window.</summary>
    /// <param name="window">The native window whose content should receive the current theme.</param>
    public void Apply(Window window)
    {
        _themes.Apply(window);
    }
}