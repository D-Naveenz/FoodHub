using Dhara.Extensions.WinUI.Hosting;
using Dhara.Extensions.WinUI.Storage;
using FoodHub.Core.Hosting;
using FoodHub.Core.Services;
using FoodHub.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FoodHub.Services;

/// <summary>Builds the application's one service provider; UI services are first resolved on the dispatcher.</summary>
public static class ApplicationHost
{
    /// <summary>Connects Dhara's reusable WinUI infrastructure to FoodHub's domain services.</summary>
    /// <param name="application">The existing App instance created by WinUI's generated entry point.</param>
    /// <returns>The host whose startup and disposal are owned by Dhara's application lifetime controller.</returns>
    public static IHost Build(App application)
    {
        var builder = WinUIApplication.CreateBuilder(application, new WinUIApplicationOptions { ApplicationDataName = "FoodHub" });
        builder.UseWinUIApplication<MainWindow>();
        // Adapt reusable Windows paths/status to Core contracts without giving Core a Windows dependency.
        builder.Services.AddSingleton(sp => new ApplicationPaths(
            sp.GetRequiredService<ApplicationDataPaths>().ContentRoot, sp.GetRequiredService<ApplicationDataPaths>().RootDirectory));
        builder.Services.AddSingleton<ILocalStorageService>(sp => new LocalStorageService(sp.GetRequiredService<ApplicationDataPaths>().RootDirectory));
        builder.Services.AddSingleton(sp => new ApplicationConfigurationStatus(sp.GetRequiredService<WinUIConfigurationStatus>().Error));
        builder.Services.AddSingleton<LocalSettingsStore>();
        builder.Services.AddFoodHubCore();
        builder.Services.AddSingleton<IWinUIStartupParticipant, FoodHubStartupParticipant>();
        builder.Services.AddSingleton<SessionStorage>();
        builder.Services.AddSingleton<SessionState>();
        builder.Services.AddSingleton<AppearanceSettings>();
        builder.Services.AddSingleton<ViewModelFactory>();
        return builder.Build();
    }
}

/// <summary>Creates view-owned state without keeping disposable view models in the root container.</summary>
public sealed class ViewModelFactory(IServiceProvider services)
{
    /// <summary>The caller disposes this form state when its native window closes.</summary>
    public AuthenticationViewModel CreateAuthentication(bool registration) => ActivatorUtilities.CreateInstance<AuthenticationViewModel>(services, registration);
    /// <summary>The caller disposes profile state when its page unloads.</summary>
    public ProfileViewModel CreateProfile() => ActivatorUtilities.CreateInstance<ProfileViewModel>(services);
    /// <summary>The caller disposes developer commands when the Settings page unloads.</summary>
    public DevelopmentSettingsViewModel CreateDevelopmentSettings() => ActivatorUtilities.CreateInstance<DevelopmentSettingsViewModel>(services);
    /// <summary>The caller cancels connection checks when its Settings page unloads.</summary>
    public DatabaseSettingsViewModel CreateDatabaseSettings() => ActivatorUtilities.CreateInstance<DatabaseSettingsViewModel>(services);
}