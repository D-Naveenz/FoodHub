using System.Reflection;
using System.Text.Json;
using Dhara.Extensions.WinUI.Storage;
using Dhara.Extensions.WinUI.Threading;
using Dhara.Extensions.WinUI.Windowing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Dhara.Extensions.WinUI.Hosting;

/// <summary>Customizes desktop hosting without coupling the library to an application's domain services.</summary>
public sealed class WinUIApplicationOptions
{
    /// <summary>Gets the unpackaged data directory name, unique to the consuming application.</summary>
    public required string ApplicationDataName { get; init; }
    /// <summary>Gets an optional content root; null uses the executable's base directory.</summary>
    public string? ContentRootPath { get; init; }
    /// <summary>Gets an explicit writable root for isolated tests or a deliberate custom storage policy.</summary>
    public string? LocalStorageRoot { get; init; }
    /// <summary>Gets an optional metadata assembly; normally the existing Application object's assembly is used.</summary>
    /// <remarks>This supports applications whose concrete App class is supplied by another assembly and metadata fixtures in tests.</remarks>
    public Assembly? ApplicationAssembly { get; init; }
    /// <summary>Gets a dispatcher captured by the consumer; null captures the current WinUI UI queue.</summary>
    public IUIThreadDispatcher? Dispatcher { get; init; }
    /// <summary>Gets the total cooperative shutdown budget; defaults to five seconds.</summary>
    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(5);
    /// <summary>Gets an optional exit callback for an externally owned Application, such as a native test harness.</summary>
    /// <remarks>Normally Application.Exit is used only when startup failed before a main window existed.</remarks>
    public Action? ExitApplication { get; init; }
}

/// <summary>Reports application configuration loading without exposing values or raw exception messages.</summary>
/// <param name="Error">Null on success, or a safe diagnostic when bundled/local JSON could not be loaded.</param>
public sealed record WinUIConfigurationStatus(string? Error);

/// <summary>Creates a Generic Host around an existing, framework-created WinUI Application.</summary>
/// <remarks>
/// The generated WinUI entry point continues to own COM/XAML initialization and the message loop. Call this
/// factory on that UI thread, register services and a main-window type, build once, and forward OnLaunched
/// to the resolved IWinUIApplicationLifetime. Never instantiate a second Application through DI or call host.Run.
/// </remarks>
/// <example>
/// <code>
/// var builder = WinUIApplication.CreateBuilder(this,
///     new WinUIApplicationOptions { ApplicationDataName = "MyApplication" });
/// builder.UseWinUIApplication&lt;MainWindow&gt;();
/// // Register application services and optional startup participants here.
/// IHost host = builder.Build();
/// var lifetime = host.Services.GetRequiredService&lt;IWinUIApplicationLifetime&lt;MainWindow&gt;&gt;();
/// // In OnLaunched: await lifetime.StartAsync(args);
/// </code>
/// </example>
public static class WinUIApplication
{
    /// <summary>Captures the existing application and creates an extensible desktop host builder.</summary>
    /// <typeparam name="TApplication">The consumer's generated Application subclass.</typeparam>
    /// <param name="application">The instance already created by WinUI, normally this inside App's constructor.</param>
    /// <param name="options">Application identity, roots, and shutdown policy.</param>
    /// <returns>A builder exposing standard host configuration, services, logging, metrics, and container customization.</returns>
    /// <exception cref="InvalidOperationException">There is no current UI queue or build metadata is invalid.</exception>
    public static WinUIApplicationBuilder CreateBuilder<TApplication>(TApplication application, WinUIApplicationOptions options)
        where TApplication : Application => new(application, options);
}

/// <summary>Resolves desktop environments from consumer build metadata, independently of process environment variables.</summary>
public static class WinUIEnvironment
{
    /// <summary>Maps Debug to Development, Release to Production, and preserves custom configuration names.</summary>
    /// <param name="assembly">The consuming assembly, never the reusable library's assembly.</param>
    /// <returns>The hosting environment and corresponding JSON filename component.</returns>
    /// <exception cref="InvalidOperationException">SDK configuration metadata is missing or unsafe for a filename.</exception>
    public static string Resolve(Assembly assembly)
    {
        var name = assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration;
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name is "." or "..")
            throw new InvalidOperationException("The application assembly must have valid AssemblyConfigurationAttribute metadata.");
        if (name.Equals("Debug", StringComparison.OrdinalIgnoreCase)) return Environments.Development;
        if (name.Equals("Release", StringComparison.OrdinalIgnoreCase)) return Environments.Production;
        return name;
    }
}

/// <summary>Wraps the standard host builder with desktop defaults and no application-specific dependencies.</summary>
/// <remarks>
/// JSON precedence is base, selected environment, then local overrides. Loading occurs once without watchers.
/// Malformed configuration produces an empty snapshot and safe status rather than silently choosing another database.
/// The built host owns services and configuration. After registration, the lifecycle controller owns starting,
/// stopping and disposing that host on the captured dispatcher.
/// </remarks>
public sealed class WinUIApplicationBuilder : IHostApplicationBuilder
{
    private readonly HostApplicationBuilder _builder;

    /// <summary>Installs desktop conventions while leaving UI construction until host startup.</summary>
    internal WinUIApplicationBuilder(Application application, WinUIApplicationOptions options)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(options);
        if (options.ShutdownTimeout <= TimeSpan.Zero || options.ShutdownTimeout > TimeSpan.FromMilliseconds(uint.MaxValue - 1))
            throw new ArgumentOutOfRangeException(nameof(options), "Shutdown timeout must be positive and finite.");
        var dispatcher = options.Dispatcher ?? new UIThreadDispatcher(DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("Create the WinUI host on the application's UI thread."));
        var assembly = options.ApplicationAssembly ?? application.GetType().Assembly;
        var environment = WinUIEnvironment.Resolve(assembly);
        var runtime = new AppRuntimeEnvironment();
        var paths = new ApplicationDataPaths(options.ApplicationDataName, runtime, options.ContentRootPath, options.LocalStorageRoot);
        _builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
            ApplicationName = assembly.GetName().Name,
            EnvironmentName = environment,
            ContentRootPath = paths.ContentRoot
        });
        var (configuration, status) = LoadConfiguration(paths, environment);
        Configuration.AddConfiguration(configuration, shouldDisposeConfiguration: true);
        Logging.AddConfiguration(Configuration.GetSection("Logging"));
        Logging.AddDebug();
        _builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateScopes = Environment.IsDevelopment(),
            ValidateOnBuild = Environment.IsDevelopment()
        }));
        Services.AddSingleton(options);
        Services.AddSingleton(new WinUIApplicationContext(application, options.ExitApplication ?? application.Exit));
        Services.AddSingleton<IUIThreadDispatcher>(dispatcher);
        Services.AddSingleton<IAppRuntimeEnvironment>(runtime);
        Services.AddSingleton(paths);
        Services.AddSingleton(status);
        Services.AddSingleton<ILocalStorageFolderLauncher, LocalStorageFolderLauncher>();
        Services.AddSingleton<WindowRegistry>();
        Services.AddSingleton<IWindowRegistry>(sp => sp.GetRequiredService<WindowRegistry>());
        Services.AddSingleton<WindowThemeService>();
        // ConsoleLifetime must not attach console shutdown behavior or own the WinUI message loop.
        Services.Replace(ServiceDescriptor.Singleton<IHostLifetime, WinUIHostLifetime>());
    }

    /// <summary>Loads reusable desktop JSON conventions; Core/tool adapters can be tested against the same contract.</summary>
    private static (IConfigurationRoot Configuration, WinUIConfigurationStatus Status) LoadConfiguration(ApplicationDataPaths paths, string environment)
    {
        try
        {
            var configuration = new ConfigurationBuilder().SetBasePath(paths.ContentRoot)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: false)
                .AddJsonFile(Path.Combine(paths.RootDirectory, "appsettings.json"), optional: true, reloadOnChange: false).Build();
            return (configuration, new(null));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or FormatException or JsonException)
        {
            return (new ConfigurationBuilder().Build(), new("Application settings could not be read. Repair or reset them in Settings, then restart."));
        }
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
    /// <remarks>A replacement container supplies its own dependency-validation policy.</remarks>
    public void ConfigureContainer<TContainerBuilder>(IServiceProviderFactory<TContainerBuilder> factory,
        Action<TContainerBuilder>? configure = null) where TContainerBuilder : notnull => _builder.ConfigureContainer(factory, configure);

    /// <summary>Builds one host without starting services or creating windows.</summary>
    /// <returns>The host owned by the application lifecycle bridge, or by the caller when used without that bridge.</returns>
    /// <exception cref="InvalidOperationException">The host has already been built.</exception>
    public IHost Build() => _builder.Build();
}

/// <summary>Registers the generic window lifecycle before the host is built.</summary>
public static class WinUIApplicationBuilderExtensions
{
    /// <summary>Registers the application's main-window type and startup/shutdown controller.</summary>
    /// <typeparam name="TMainWindow">The window created through constructor injection on the UI thread.</typeparam>
    /// <param name="builder">A builder created with WinUIApplication.CreateBuilder.</param>
    /// <returns>The same builder for additional application-specific registrations.</returns>
    /// <exception cref="InvalidOperationException">Application infrastructure is missing or another main window was registered.</exception>
    public static IHostApplicationBuilder UseWinUIApplication<TMainWindow>(this IHostApplicationBuilder builder) where TMainWindow : Window
    {
        if (!builder.Services.Any(x => x.ServiceType == typeof(WinUIApplicationContext)))
            throw new InvalidOperationException("Use WinUIApplication.CreateBuilder with the existing Application first.");
        if (builder.Services.Any(x => x.ServiceType == typeof(WinUIMainWindowRegistration)))
            throw new InvalidOperationException("One main-window registration is allowed per WinUI application host.");
        builder.Services.AddSingleton<WinUIMainWindowRegistration>();
        builder.Services.TryAddSingleton<TMainWindow>();
        builder.Services.AddSingleton<WinUIWindowHostedService<TMainWindow>>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<WinUIWindowHostedService<TMainWindow>>());
        builder.Services.AddSingleton<IWinUIApplicationLifetime<TMainWindow>, WinUIApplicationLifetime<TMainWindow>>();
        return builder;
    }
}

/// <summary>Marks the single main-window registration without depending on a consumer's window type.</summary>
internal sealed class WinUIMainWindowRegistration;

/// <summary>Retains the already-created application and its process-exit boundary.</summary>
/// <param name="Application">The application instance created by the generated WinUI entry point.</param>
/// <param name="Exit">The application-exit callback used when startup allocated no main window.</param>
internal sealed record WinUIApplicationContext(Application Application, Action Exit);

/// <summary>Leaves UI message-loop ownership with WinUI and host orchestration with the lifecycle controller.</summary>
internal sealed class WinUIHostLifetime : IHostLifetime
{
    /// <inheritdoc/>
    public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}