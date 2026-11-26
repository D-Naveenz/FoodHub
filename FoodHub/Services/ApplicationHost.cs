using Dhara.Extensions.WinUI.Hosting;
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
        var builder = WinUIApplication.CreateBuilder(application, new WinUIApplicationOptions { PublisherName = "dashe", ApplicationDataName = "FoodHub" });
        builder.UseWinUIApplication<MainWindow>();
        builder.Services.AddFoodHubCore();
        builder.Services.AddSingleton<IWinUIStartupParticipant, FoodHubStartupParticipant>();
        builder.Services.AddSingleton<SessionStorage>();
        builder.Services.AddSingleton<SessionState>();
        builder.Services.AddSingleton<AppearanceSettings>();
        builder.Services.AddSingleton<ViewModelFactory>();
        builder.Services.AddSingleton<CheckoutCoordinator>();
        builder.Services.AddSingleton<MenuBrowseState>();
        return builder.Build();
    }
}

/// <summary>Creates view-owned state without keeping disposable view models in the root container.</summary>
public sealed class ViewModelFactory(IServiceProvider services)
{
    /// <summary>Creates page-owned ordering state and its cancellation lifetime.</summary>
    public JourneyViewModel CreateJourney() => ActivatorUtilities.CreateInstance<JourneyViewModel>(services);
    /// <summary>Creates menu state while keeping browsing preferences in a shared lightweight store.</summary>
    public MenuViewModel CreateMenu() => ActivatorUtilities.CreateInstance<MenuViewModel>(services);
    /// <summary>The caller disposes this form state when its native window closes.</summary>
    public AuthenticationViewModel CreateAuthentication(bool registration) => ActivatorUtilities.CreateInstance<AuthenticationViewModel>(services, registration);
    /// <summary>The caller disposes profile state when its page unloads.</summary>
    public ProfileViewModel CreateProfile() => ActivatorUtilities.CreateInstance<ProfileViewModel>(services);
    /// <summary>The caller disposes developer commands when the Settings page unloads.</summary>
    public DevelopmentSettingsViewModel CreateDevelopmentSettings() => ActivatorUtilities.CreateInstance<DevelopmentSettingsViewModel>(services);
    /// <summary>The caller cancels connection checks when its Settings page unloads.</summary>
    public DatabaseSettingsViewModel CreateDatabaseSettings() => ActivatorUtilities.CreateInstance<DatabaseSettingsViewModel>(services);
}
