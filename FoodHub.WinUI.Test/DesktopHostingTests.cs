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

    /// <summary>Creates content and writable roots belonging exclusively to this test.</summary>
    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "FoodHub-host-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
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
            ApplicationDataName = "Dhara.Tests",
            ApplicationAssembly = Consumer(configuration),
            ContentRootPath = _root,
            LocalStorageRoot = _storage.RootDirectory,
            Dispatcher = new UIThreadDispatcher(UITestMethodAttribute.DispatcherQueue!)
        });
        builder.Services.AddSingleton<ILocalStorageService>(_storage);
        builder.Services.AddSingleton<LocalSettingsStore>();
        builder.Services.AddSingleton(sp => new ApplicationConfigurationStatus(sp.GetRequiredService<WinUIConfigurationStatus>().Error));
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
        Assert.Throws<InvalidOperationException>(() => WinUIEnvironment.Resolve(Consumer(null)));
        Assert.Throws<InvalidOperationException>(() => WinUIEnvironment.Resolve(Consumer("../outside")));
    }

    /// <summary>The Windows host and portable tool loader select the same profile names and effective JSON values.</summary>
    [TestMethod]
    [DataRow("Debug", "Development")]
    [DataRow("Release", "Production")]
    [DataRow("Staging", "Staging")]
    public async Task ToolAndWinUIConfigurationContractsMatch(string profile, string environment)
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "appsettings.json"), "{\"Appearance\":{\"Theme\":\"System\"},\"Other\":\"base\"}");
        await File.WriteAllTextAsync(Path.Combine(_root, $"appsettings.{environment}.json"), "{\"Appearance\":{\"Theme\":\"Dark\"}}");
        await _storage.WriteAsync("appsettings.json", "{\"Other\":\"local\"}"u8.ToArray());
        using var host = Builder(profile).Build();
        var (portable, error) = ApplicationConfiguration.Load(new ApplicationPaths(_root, _storage.RootDirectory), environment);
        using (portable as IDisposable)
        {
            Assert.IsNull(error);
            Assert.AreEqual(DesktopEnvironment.Resolve(Consumer(profile)), host.Services.GetRequiredService<IHostEnvironment>().EnvironmentName);
            var windows = host.Services.GetRequiredService<IConfiguration>();
            Assert.AreEqual(portable["Appearance:Theme"], windows["Appearance:Theme"]);
            Assert.AreEqual(portable["Other"], windows["Other"]);
        }
    }

    /// <summary>Both consumers reject malformed/duplicate settings rather than using defaults from another layer.</summary>
    [TestMethod]
    [DataRow("{ broken")]
    [DataRow("{\"Other\":1,\"other\":2}")]
    [DataRow("[]")]
    public async Task ToolAndWinUIConfigurationFailuresMatch(string malformed)
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "appsettings.json"), "{\"Other\":\"must not fall through\"}");
        await _storage.WriteAsync("appsettings.json", System.Text.Encoding.UTF8.GetBytes(malformed));
        using var host = Builder("Release").Build();
        var (portable, error) = ApplicationConfiguration.Load(new ApplicationPaths(_root, _storage.RootDirectory), "Production");
        using (portable as IDisposable)
        {
            Assert.IsNotNull(error);
            Assert.IsNotNull(host.Services.GetRequiredService<WinUIConfigurationStatus>().Error);
            Assert.IsNull(host.Services.GetRequiredService<IConfiguration>()["Other"]);
            Assert.IsNull(portable["Other"]);
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

    /// <summary>Appearance participates in all three JSON layers, just like the database connection.</summary>
    [TestMethod]
    public async Task GeneralSettingsUseTheSamePrecedence()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "appsettings.json"), "{\"Appearance\":{\"Theme\":\"System\"}}");
        await File.WriteAllTextAsync(Path.Combine(_root, "appsettings.Staging.json"), "{\"Appearance\":{\"Theme\":\"Dark\"}}");
        using (var host = Builder("Staging").Build())
            Assert.AreEqual("Dark", host.Services.GetRequiredService<IConfiguration>()["Appearance:Theme"]);
        await _storage.WriteAsync("appsettings.json", System.Text.Encoding.UTF8.GetBytes("{\"Appearance\":{\"Theme\":\"Light\"}}"));
        using (var host = Builder("Staging").Build())
            Assert.AreEqual("Light", host.Services.GetRequiredService<IConfiguration>()["Appearance:Theme"]);
    }

    /// <summary>Concurrent feature edits retain both settings; resetting the database leaves appearance intact.</summary>
    [TestMethod]
    public async Task SharedWriterPreservesConcurrentFeatureEdits()
    {
        using var host = Builder("Release").Build();
        var settings = host.Services.GetRequiredService<LocalSettingsStore>();
        var database = new DatabaseSettingsStore(settings, new DatabaseSettings(host.Services.GetRequiredService<IConfiguration>()));
        var connection = DatabaseSettingsStore.BuildConnectionString("localhost", "FoodHub_Test", true);
        await Task.WhenAll(database.SaveAsync(connection), settings.UpdateAsync(json =>
            json["Appearance"] = new System.Text.Json.Nodes.JsonObject { ["Theme"] = "Dark" }));
        await database.SaveAsync(null);
        var (configuration, error) = ApplicationConfiguration.Load(new ApplicationPaths(_root, _storage.RootDirectory), "Production");
        using (configuration as IDisposable)
        {
            Assert.IsNull(error);
            Assert.AreEqual("Dark", configuration["Appearance:Theme"]);
            Assert.IsNull(configuration.GetConnectionString("FoodHub"));
            Assert.AreEqual("True", configuration["FoodHub:ResetSessionOnStartup"]);
        }
    }

    /// <summary>Normal saves cannot silently destroy malformed JSON; explicit database repair can replace it.</summary>
    [TestMethod]
    [DataRow("{ broken")]
    [DataRow("{\"Other\":1,\"other\":2}")]
    [DataRow("[]")]
    public async Task MalformedSettingsRequireExplicitRepair(string malformed)
    {
        await _storage.WriteAsync("appsettings.json", System.Text.Encoding.UTF8.GetBytes(malformed));
        using var settings = new LocalSettingsStore(_storage);
        await Assert.ThrowsAsync<IOException>(() => settings.UpdateAsync(json => json["Other"] = true));
        Assert.AreEqual(malformed, System.Text.Encoding.UTF8.GetString((await _storage.ReadAsync("appsettings.json"))!));
        await new DatabaseSettingsStore(settings, new DatabaseSettings(new ConfigurationBuilder().Build())).SaveAsync(null);
        Assert.IsTrue(System.Text.Json.Nodes.JsonNode.Parse((await _storage.ReadAsync("appsettings.json"))!) is not null);
    }

    /// <summary>Cancelled edits preserve existing bytes and release the gate for a subsequent successful save.</summary>
    [TestMethod]
    public async Task CancelledSettingsEditCanRetry()
    {
        using var settings = new LocalSettingsStore(_storage);
        await settings.UpdateAsync(json => json["Other"] = "keep");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => settings.UpdateAsync(json => json["Other"] = "lost", cancellation: cancellation.Token));
        await settings.UpdateAsync(json => Assert.AreEqual("keep", json["Other"]!.GetValue<string>()));
    }

    /// <summary>Appearance reads the launch snapshot, ignores theme.txt, and changes state only after a successful save.</summary>
    [TestMethod]
    public Task AppearanceUsesJsonAndPreservesStateOnFailure() => WinUITestSupport.OnDispatcher(async () =>
    {
        await _storage.WriteAsync("theme.txt", "Dark"u8.ToArray());
        using var settings = new LocalSettingsStore(_storage);
        var appearance = new AppearanceSettings(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Appearance:Theme"] = "Light" }).Build(), settings,
            new WindowThemeService(new WindowRegistry(WinUITestSupport.Dispatcher), WinUITestSupport.Dispatcher));
        await appearance.LoadAsync();
        Assert.AreEqual(ElementTheme.Light, appearance.Theme);
        await _storage.WriteAsync("appsettings.json", "{ broken"u8.ToArray());
        await Assert.ThrowsAsync<IOException>(() => appearance.SetThemeAsync(ElementTheme.Dark));
        Assert.AreEqual(ElementTheme.Light, appearance.Theme);
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