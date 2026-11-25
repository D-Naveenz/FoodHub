using Dhara.Extensions.WinUI.Hosting;
using FoodHub.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace FoodHub.Services;

/// <summary>Keeps account restoration and appearance persistence outside the reusable WinUI host.</summary>
/// <param name="appearance">Loads the saved theme before native window construction.</param>
/// <param name="settings">Prepares a durable database-change token reset before authentication restoration.</param>
/// <param name="storage">The protected local token store.</param>
/// <param name="session">The customer's visible account/session state.</param>
/// <param name="logger">Receives categories only, never token bytes or connection details.</param>
public sealed class FoodHubStartupParticipant(AppearanceSettings appearance, DatabaseSettingsStore settings,
    SessionStorage storage, SessionState session, ILogger<FoodHubStartupParticipant> logger) : IWinUIStartupParticipant
{
    private bool _restore = true;

    /// <inheritdoc/>
    public Task BeforeWindowCreatedAsync(CancellationToken cancellation) => appearance.LoadAsync(cancellation);

    /// <inheritdoc/>
    public async Task BeforeWindowActivatedAsync(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        try { await settings.PrepareSessionAsync(() => storage.WriteAsync(null)); }
        catch (Exception ex) when (ex is DatabaseConfigurationException or System.IO.IOException or UnauthorizedAccessException)
        {
            // Configuration failures are recoverable: still show Guest navigation and Settings.
            logger.LogWarning("Session preparation failed: {Category}", ex.GetType().Name);
            _restore = false;
            session.Message = "Account settings need attention. Open Settings to repair configuration or check access to app data.";
        }
    }

    /// <inheritdoc/>
    public async Task AfterWindowActivatedAsync(LaunchActivatedEventArgs args, CancellationToken cancellation)
    {
        if (_restore) await session.RestoreAsync(cancellation);
    }
}