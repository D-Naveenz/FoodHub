using FoodHub.Core.Services;
using FoodHub.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FoodHub.Services;

/// <summary>Builds the application's one service provider; UI services are first resolved on the dispatcher.</summary>
public static class ApplicationHost
{
    /// <summary>Creates the host with the same configuration rules as EF and administration tools.</summary>
    public static IHost Build()
    {
        var paths = new ApplicationPaths();
        var (configuration, error) = DatabaseConfiguration.Load(paths, DatabaseConfiguration.EnvironmentName);
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
            ApplicationName = typeof(App).Assembly.GetName().Name,
            EnvironmentName = DatabaseConfiguration.EnvironmentName,
            ContentRootPath = paths.InstallDirectory
        });
        builder.Configuration.AddConfiguration(configuration, shouldDisposeConfiguration: true);
        builder.Logging.AddDebug();
        builder.Services.AddSingleton(paths);
        builder.Services.AddSingleton(new DatabaseSettings(configuration, error));
        builder.Services.AddSingleton<DatabaseSettingsStore>();
        builder.Services.AddSingleton<DatabaseConnectionTester>();
        builder.Services.AddSingleton<IAppRuntimeEnvironment, AppRuntimeEnvironment>();
        builder.Services.AddSingleton<Func<FoodHub.Core.Models.FoodHub_DevContext>>(sp => sp.GetRequiredService<DatabaseSettings>().Open);
        builder.Services.AddSingleton<AccountService>();
        builder.Services.AddSingleton(sp => new SessionStorage(sp.GetRequiredService<ApplicationPaths>().UserDirectory));
        builder.Services.AddSingleton<SessionState>();
        builder.Services.AddSingleton<AppearanceSettings>();
        builder.Services.AddSingleton<ViewModelFactory>();
        builder.Services.AddSingleton<MainWindow>();
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
    /// <summary>The caller cancels connection checks when its Settings page unloads.</summary>
    public DatabaseSettingsViewModel CreateDatabaseSettings() => ActivatorUtilities.CreateInstance<DatabaseSettingsViewModel>(services);
}