using System.Reflection;
using FoodHub.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FoodHub.Core.Hosting;

/// <summary>Specifies the consuming application's identity and desktop storage boundaries.</summary>
/// <param name="ApplicationAssembly">The consuming assembly whose build configuration selects the environment.</param>
/// <remarks>These options contain no WinUI types; a Windows application injects its package-aware storage adapter.</remarks>
public sealed record DesktopApplicationOptions(Assembly ApplicationAssembly)
{
    /// <summary>Gets the content root; null selects <see cref="AppContext.BaseDirectory"/>.</summary>
    public string? ContentRootPath { get; init; }
    /// <summary>Gets the writable storage adapter; null selects unpackaged LocalAppData/FoodHub.</summary>
    public ILocalStorageService? LocalStorage { get; init; }
}

/// <summary>Creates a Generic Host builder with FoodHub's desktop configuration conventions.</summary>
/// <example>
/// A WinUI composition root selects its Windows adapter before registering application services:
/// <code>
/// var runtime = new AppRuntimeEnvironment();
/// var builder = DesktopApplication.CreateBuilder(new DesktopApplicationOptions(typeof(App).Assembly)
/// {
///     LocalStorage = WindowsLocalStorage.Create(runtime)
/// });
/// builder.Services.AddFoodHubCore();
/// builder.Services.AddSingleton&lt;IAppRuntimeEnvironment&gt;(runtime);
/// // Register WinUI services and the hosted main window here.
/// IHost host = builder.Build();
/// </code>
/// The WinUI application calls StartAsync on launch and StopAsync/Dispose during dispatcher-aware shutdown.
/// It retains the generated WinUI entry point and never calls the blocking console Run method.
/// </example>
public static class DesktopApplication
{
    /// <summary>Creates an extensible builder without opening a database or constructing UI objects.</summary>
    /// <param name="options">Application assembly, content root, and optional writable storage.</param>
    /// <returns>A builder that the caller customizes before calling <see cref="DesktopApplicationBuilder.Build"/>.</returns>
    /// <exception cref="InvalidOperationException">The consuming assembly lacks valid build metadata.</exception>
    public static DesktopApplicationBuilder CreateBuilder(DesktopApplicationOptions options) => new(options);
}

/// <summary>Adapts the standard Generic Host builder for a desktop application.</summary>
/// <remarks>
/// This wrapper owns conventions, not a message loop. The built host owns configuration and services;
/// the application owns the host's startup, shutdown, and disposal. Database services are opt-in through
/// <see cref="DesktopServiceCollectionExtensions.AddFoodHubCore"/>. All WinUI objects stay in the consuming application.
/// </remarks>
public sealed class DesktopApplicationBuilder : IHostApplicationBuilder
{
    private readonly HostApplicationBuilder _builder;

    /// <summary>Builds the shared configuration and installs desktop defaults exactly once.</summary>
    internal DesktopApplicationBuilder(DesktopApplicationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var environment = DesktopEnvironment.Resolve(options.ApplicationAssembly);
        var storage = options.LocalStorage ?? new LocalStorageService(new ApplicationPaths().UserDirectory);
        var paths = new ApplicationPaths(options.ContentRootPath, storage.RootDirectory);
        _builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
            ApplicationName = options.ApplicationAssembly.GetName().Name,
            EnvironmentName = environment,
            ContentRootPath = paths.InstallDirectory
        });
        var (configuration, error) = ApplicationConfiguration.Load(paths, environment);
        // The host disposes the chained root; no second service provider is constructed here.
        Configuration.AddConfiguration(configuration, shouldDisposeConfiguration: true);
        Logging.AddConfiguration(Configuration.GetSection("Logging"));
        Logging.AddDebug();
        _builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateScopes = Environment.IsDevelopment(),
            ValidateOnBuild = Environment.IsDevelopment()
        }));
        Services.AddSingleton(paths);
        Services.AddSingleton<ILocalStorageService>(storage);
        Services.AddSingleton(new ApplicationConfigurationStatus(error));
        Services.AddSingleton<LocalSettingsStore>();
    }

    /// <inheritdoc/>
    public IDictionary<object, object> Properties => ((IHostApplicationBuilder)_builder).Properties;
    /// <inheritdoc/>
    public ConfigurationManager Configuration => _builder.Configuration;
    /// <inheritdoc/>
    IConfigurationManager IHostApplicationBuilder.Configuration => Configuration;
    /// <inheritdoc/>
    public IHostEnvironment Environment => _builder.Environment;
    /// <inheritdoc/>
    public IServiceCollection Services => _builder.Services;
    /// <inheritdoc/>
    public ILoggingBuilder Logging => _builder.Logging;
    /// <inheritdoc/>
    public IMetricsBuilder Metrics => _builder.Metrics;

    /// <inheritdoc/>
    /// <remarks>A custom container replaces the default provider, including its Development validation policy.</remarks>
    public void ConfigureContainer<TContainerBuilder>(IServiceProviderFactory<TContainerBuilder> factory,
        Action<TContainerBuilder>? configure = null) where TContainerBuilder : notnull => _builder.ConfigureContainer(factory, configure);

    /// <summary>Creates the single host and transfers service/configuration ownership to it.</summary>
    /// <returns>The host, which the caller must start, stop, and dispose.</returns>
    /// <exception cref="InvalidOperationException">Build was already called, or service validation fails.</exception>
    public IHost Build() => _builder.Build();
}

/// <summary>Registers FoodHub workflows separately from desktop infrastructure and native UI adapters.</summary>
public static class DesktopServiceCollectionExtensions
{
    /// <summary>Registers launch-scoped database settings and account workflows without accessing SQL.</summary>
    /// <param name="services">The desktop builder's service collection.</param>
    /// <returns>The same collection for additional registrations.</returns>
    /// <remarks>Contexts remain short-lived: the singleton factory creates one context for each workflow operation.</remarks>
    public static IServiceCollection AddFoodHubCore(this IServiceCollection services)
    {
        services.TryAddSingleton(sp => new DatabaseSettings(sp.GetRequiredService<IConfiguration>(),
            sp.GetRequiredService<ApplicationConfigurationStatus>().Error));
        services.TryAddSingleton<DatabaseSettingsStore>();
        services.TryAddSingleton<DatabaseConnectionTester>();
        services.TryAddSingleton<Func<Models.FoodHub_DevContext>>(sp => sp.GetRequiredService<DatabaseSettings>().Open);
        services.TryAddSingleton<AccountService>();
        return services;
    }
}