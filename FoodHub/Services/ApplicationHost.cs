using FoodHub.Core.Hosting;
using FoodHub.Core.Services;
using FoodHub.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FoodHub.Services;

/// <summary>Builds the application's one service provider; UI services are first resolved on the dispatcher.</summary>
public static class ApplicationHost
{
    /// <summary>Creates the host with the same configuration rules as EF and administration tools.</summary>
    public static IHost Build()
    {
        var runtime = new AppRuntimeEnvironment();
        var storage = WindowsLocalStorage.Create(runtime);
        var builder = DesktopApplication.CreateBuilder(new DesktopApplicationOptions(typeof(App).Assembly)
        {
            LocalStorage = storage
        });
        builder.Services.AddFoodHubCore();
        builder.Services.AddSingleton<IAppRuntimeEnvironment>(runtime);
        builder.Services.AddSingleton<ILocalStorageFolderLauncher, LocalStorageFolderLauncher>();
        builder.Services.AddSingleton<SessionStorage>();
        builder.Services.AddSingleton<SessionState>();
        builder.Services.AddSingleton<AppearanceSettings>();
        builder.Services.AddSingleton<ViewModelFactory>();
        builder.Services.AddSingleton<MainWindow>();
        builder.Services.AddSingleton<Func<MainWindow>>(sp => sp.GetRequiredService<MainWindow>);
        builder.Services.AddSingleton<MainWindowHostedService>();
        builder.Services.AddSingleton<IMainWindowService>(sp => sp.GetRequiredService<MainWindowHostedService>());
        builder.Services.AddHostedService(sp => sp.GetRequiredService<MainWindowHostedService>());
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