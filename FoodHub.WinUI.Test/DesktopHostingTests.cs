using Dhara.Extensions.WinUI.Storage;
using Dhara.Extensions.WinUI.Hosting;
using Dhara.Extensions.WinUI.Threading;
using Dhara.Extensions.WinUI.Windowing;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using System.Reflection;
using System.Reflection.Emit;
using FoodHub.Core.Hosting;
using FoodHub.Core.Services;
using FoodHub.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;

namespace FoodHub.WinUI.Test;

/// <summary>Checks desktop conventions against real host builders and isolated JSON files without SQL.</summary>
[TestClass]
[TestCategory("Unit")]
[DoNotParallelize]
public sealed class DesktopHostingTests
{
    private string _root = "";
    private LocalStorageService _storage = null!;
    private SettingsTestContainer _container = null!;

    /// <summary>Creates content and writable roots belonging exclusively to this test.</summary>
    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "FoodHub-host-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _container = new SettingsTestContainer();
        _storage = new LocalStorageService(Path.Combine(_root, "user"));
    }

    /// <summary>Removes only this test's generated directory after hosts have been disposed.</summary>
    [TestCleanup]
    public void Cleanup() => Directory.Delete(_root, recursive: true);

    /// <summary>Uses actual assembly attributes to exercise consumer metadata independently of Core's build.</summary>
    private static Assembly Consumer(string? configuration)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Consumer" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run);
        if (configuration is not null)
            assembly.SetCustomAttribute(new CustomAttributeBuilder(typeof(AssemblyConfigurationAttribute).GetConstructor([typeof(string)])!, [configuration]));
        return assembly;
    }

    /// <summary>Creates a real desktop builder with a deliberately separate consumer assembly and storage root.</summary>
    private WinUIApplicationBuilder Builder(string configuration)
    {
        var builder = WinUIApplication.CreateBuilder(Application.Current, new WinUIApplicationOptions
        {
            PublisherName = "Dhara.Tests",
            ApplicationDataName = "ConfigurationTests",
            SettingsContainer = _container,
            ApplicationAssembly = Consumer(configuration),
            ContentRootPath = _root,
            LocalStorageRoot = _storage.RootDirectory,
            Dispatcher = new UIThreadDispatcher(UITestMethodAttribute.DispatcherQueue!)
        });
        return builder;
    }

    /// <summary>Standard profiles map to host conventions; custom profiles retain their identity.</summary>
    [TestMethod]
    [DataRow("Debug", "Development")]
    [DataRow("Release", "Production")]
    [DataRow("Staging", "Staging")]
    public void ConsumingAssemblySelectsEnvironment(string configuration, string expected)
    {
        var builder = Builder(configuration);
        using var host = builder.Build();
        Assert.AreEqual(expected, host.Services.GetRequiredService<IHostEnvironment>().EnvironmentName);
        Assert.AreEqual(Path.GetFullPath(_root), builder.Environment.ContentRootPath);
    }

    /// <summary>A missing configuration attribute cannot silently select a different deployment environment.</summary>
    [TestMethod]
    public void MissingOrUnsafeMetadataFailsClearly()
    {
        Assert.Throws<InvalidOperationException>(() => ApplicationEnvironment.Resolve(Consumer(null)));
        Assert.Throws<InvalidOperationException>(() => ApplicationEnvironment.Resolve(Consumer("../outside")));
    }

    /// <summary>Host IConfiguration never merges native overrides or reads an obsolete local appsettings file.</summary>
    [TestMethod]
    public async Task HostConfigurationIsSeparateFromRuntimeSettings()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "appsettings.json"), "{\"Appearance\":{\"Theme\":\"System\"}}");
        await _storage.WriteAsync("appsettings.json", "{ broken obsolete JSON"u8.ToArray());
        _container.Values["Appearance.Theme"] = "Dark";
        using var host = Builder("Release").Build();
        Assert.AreEqual("System", host.Services.GetRequiredService<IConfiguration>()["Appearance:Theme"]);
        Assert.AreEqual("Dark", host.Services.GetRequiredService<ILocalSettingsStore>().Read("Appearance.Theme", "System"));
        Assert.IsNull(host.Services.GetRequiredService<ApplicationConfigurationStatus>().Error);
    }

    /// <summary>External file changes reload host defaults only when watching is explicitly enabled.</summary>
    [TestMethod]
    public async Task OptionalFileWatchingPreservesDatabaseSnapshot()
    {
        var file = Path.Combine(_root, "appsettings.json");
        await File.WriteAllTextAsync(file, "{\"ConnectionStrings\":{\"FoodHub\":\"Server=first;Database=One\"}}");
        var (configuration, error) = ApplicationConfiguration.Load(_root, "Production", reloadOnChange: true);
        using (configuration as IDisposable)
        {
            Assert.IsNull(error);
            var active = new DatabaseSettings(configuration);
            var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var subscription = Microsoft.Extensions.Primitives.ChangeToken.OnChange(configuration.GetReloadToken, () =>
            {
                if (configuration.GetConnectionString("FoodHub") == "Server=second;Database=Two") changed.TrySetResult();
            });
            await File.WriteAllTextAsync(file, "{\"ConnectionStrings\":{\"FoodHub\":\"Server=second;Database=Two\"}}");
            await changed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual("Server=first;Database=One", active.ConnectionString);
            Assert.AreEqual("Server=second;Database=Two", configuration.GetConnectionString("FoodHub"));
        }
    }

    /// <summary>Process environment values cannot change the WinUI profile or override bundled application defaults.</summary>
    [TestMethod]
    public async Task WinUIConfigurationIgnoresProcessOverrides()
    {
        var names = new[] { "DOTNET_ENVIRONMENT", "ConnectionStrings__FoodHub", "FOODHUB_CONNECTION_STRING" };
        var previous = names.ToDictionary(x => x, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var name in names) Environment.SetEnvironmentVariable(name, "Ignored");
            await File.WriteAllTextAsync(Path.Combine(_root, "appsettings.Development.json"), "{\"ConnectionStrings\":{\"FoodHub\":\"bundled\"}}");
            using var host = Builder("Debug").Build();
            Assert.AreEqual("Development", host.Services.GetRequiredService<IHostEnvironment>().EnvironmentName);
            Assert.AreEqual("bundled", host.Services.GetRequiredService<IConfiguration>().GetConnectionString("FoodHub"));
        }
        finally { foreach (var value in previous) Environment.SetEnvironmentVariable(value.Key, value.Value); }
    }

    /// <summary>Configuration and service APIs stay customizable, and host disposal releases registered resources.</summary>
    [TestMethod]
    public void StandardBuilderApisAndDisposalWork()
    {
        var builder = Builder("Release");
        builder.Configuration["Example:Value"] = "custom";
        builder.Services.AddFoodHubCore();
        builder.Services.AddSingleton<DisposalProbe>();
        DisposalProbe probe;
        using (var host = builder.Build())
        {
            Assert.AreEqual("custom", host.Services.GetRequiredService<IConfiguration>()["Example:Value"]);
            Assert.IsNotNull(host.Services.GetRequiredService<AccountService>());
            probe = host.Services.GetRequiredService<DisposalProbe>();
            Assert.Throws<InvalidOperationException>(() => builder.Build());
        }
        Assert.IsTrue(probe.Disposed);
    }

    /// <summary>Development detects captive scoped dependencies before application startup.</summary>
    [TestMethod]
    public void DevelopmentValidatesServiceLifetimes()
    {
        var builder = Builder("Debug");
        builder.Services.AddScoped<ScopedProbe>();
        builder.Services.AddSingleton<CaptiveProbe>();
        Assert.Throws<AggregateException>(() => builder.Build());
    }

    /// <summary>Environment defaults override base JSON; legacy local JSON never becomes host configuration.</summary>
    [TestMethod]
    public async Task GeneralSettingsUseTheSamePrecedence()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "appsettings.json"), "{\"Appearance\":{\"Theme\":\"System\"}}");
        await File.WriteAllTextAsync(Path.Combine(_root, "appsettings.Staging.json"), "{\"Appearance\":{\"Theme\":\"Dark\"}}");
        using (var host = Builder("Staging").Build())
            Assert.AreEqual("Dark", host.Services.GetRequiredService<IConfiguration>()["Appearance:Theme"]);
        await _storage.WriteAsync("appsettings.json", System.Text.Encoding.UTF8.GetBytes("{\"Appearance\":{\"Theme\":\"Light\"}}"));
        using (var host = Builder("Staging").Build())
            Assert.AreEqual("Dark", host.Services.GetRequiredService<IConfiguration>()["Appearance:Theme"]);
    }

    /// <summary>Concurrent native feature edits preserve each other and do not mutate the host defaults.</summary>
    [TestMethod]
    public async Task SharedWriterPreservesConcurrentFeatureEdits()
    {
        using var host = Builder("Release").Build();
        var settings = host.Services.GetRequiredService<ILocalSettingsStore>();
        var database = new DatabaseSettingsStore(settings, new DatabaseSettings(host.Services.GetRequiredService<IConfiguration>()));
        var connection = DatabaseSettingsStore.BuildConnectionString("localhost", "FoodHub_Test", true);
        await Task.WhenAll(database.SaveAsync(connection), settings.WriteAsync("Appearance.Theme", "Dark"));
        await database.SaveAsync(null);
        Assert.AreEqual("Dark", settings.Read("Appearance.Theme", "System"));
        Assert.IsTrue((bool)settings.ReadGroup("Database")["ResetSessionOnStartup"]);
        Assert.IsFalse(settings.ReadGroup("Database").ContainsKey("ConnectionString"));
        Assert.IsNull(host.Services.GetRequiredService<IConfiguration>().GetConnectionString("FoodHub"));
    }

    /// <summary>Cancelled native edits leave the prior value intact and allow a later successful write.</summary>
    [TestMethod]
    public async Task CancelledSettingsEditCanRetry()
    {
        using var settings = new LocalSettingsStore(_container);
        await settings.WriteAsync("Other", "keep");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => settings.WriteAsync("Other", "lost", cancellation.Token));
        Assert.AreEqual("keep", settings.Read("Other", ""));
        await settings.WriteAsync("Other", "retry");
        Assert.AreEqual("retry", settings.Read("Other", ""));
    }

    /// <summary>Appearance resolves native overrides, ignores theme.txt, and changes UI state only after a successful commit.</summary>
    [TestMethod]
    public Task AppearanceUsesLocalSettingsAndPreservesStateOnFailure() => WinUITestSupport.OnDispatcher(async () =>
    {
        await _storage.WriteAsync("theme.txt", "Dark"u8.ToArray());
        using var settings = new LocalSettingsStore(_container);
        using var themes = new WindowThemeService(new WindowRegistry(WinUITestSupport.Dispatcher), WinUITestSupport.Dispatcher);
        var appearance = new AppearanceSettings(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Appearance:Theme"] = "Light" }).Build(), settings, themes);
        await appearance.LoadAsync();
        Assert.AreEqual(ElementTheme.Light, appearance.Theme);
        _container.FailWrites = true;
        await Assert.ThrowsAsync<IOException>(() => appearance.SetThemeAsync(ElementTheme.Dark));
        Assert.AreEqual(ElementTheme.Light, appearance.Theme);
        _container.FailWrites = false;
        await settings.WriteAsync("Appearance.Theme", "Dark");
        await appearance.LoadAsync();
        Assert.AreEqual(ElementTheme.Dark, appearance.Theme);
    });

    /// <summary>A resource whose lifetime is observable without touching application data.</summary>
    private sealed class DisposalProbe : IDisposable
    {
        /// <summary>Gets whether the owning host disposed this service.</summary>
        public bool Disposed { get; private set; }
        /// <inheritdoc/>
        public void Dispose() => Disposed = true;
    }

    /// <summary>A scoped dependency used to check Development validation.</summary>
    private sealed class ScopedProbe;
    /// <summary>An intentionally invalid singleton that captures a scoped dependency.</summary>
    /// <param name="scoped">The dependency whose lifetime is shorter than this object's lifetime.</param>
    private sealed class CaptiveProbe(ScopedProbe scoped)
    {
        /// <summary>Gets the captured dependency to keep the example meaningful to the compiler.</summary>
        public object Scoped { get; } = scoped;
    }
}