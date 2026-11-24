using FoodHub.Core.Services;

namespace FoodHub.Services;

/// <summary>Explicit composition root; services never depend on WinUI from Core.</summary>
public sealed class ApplicationServices
{
    /// <summary>Database-backed account and customer workflows.</summary>
    public AccountService Accounts { get; } = new(DatabaseConfiguration.Open);
    /// <summary>Session coordinator shared by all pages and windows.</summary>
    public SessionState Session { get; }
    /// <summary>Persisted application appearance.</summary>
    public AppearanceSettings Appearance { get; } = new();
    /// <summary>Composes the application on its UI thread.</summary>
    public ApplicationServices() => Session = new(Accounts, new SessionStorage());
}
