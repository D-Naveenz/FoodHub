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
        var runtime = new AppRuntimeEnvironment();
        var storage = WindowsLocalStorage.Create(runtime);
        string? storageError = null;
        if (runtime.IsPackaged)
        {
            try { LegacySettingsMigration.Import(new ApplicationPaths().UserDirectory, storage); }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            { storageError = "Legacy settings could not be imported. Check access to local application storage."; }
        }
        var paths = new ApplicationPaths(userDirectory: storage.RootDirectory);
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
        builder.Services.AddSingleton<ILocalStorageService>(storage);
        builder.Services.AddSingleton(new DatabaseSettings(configuration, storageError ?? error));
        builder.Services.AddSingleton<DatabaseSettingsStore>();
        builder.Services.AddSingleton<DatabaseConnectionTester>();
        builder.Services.AddSingleton<IAppRuntimeEnvironment>(runtime);
        builder.Services.AddSingleton<ILocalStorageFolderLauncher, LocalStorageFolderLauncher>();
        builder.Services.AddSingleton<Func<FoodHub.Core.Models.FoodHub_DevContext>>(sp => sp.GetRequiredService<DatabaseSettings>().Open);
        builder.Services.AddSingleton<AccountService>();
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