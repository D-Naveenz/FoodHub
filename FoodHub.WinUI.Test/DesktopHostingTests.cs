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
    private DesktopApplicationBuilder Builder(string configuration) => DesktopApplication.CreateBuilder(
        new DesktopApplicationOptions(Consumer(configuration)) { ContentRootPath = _root, LocalStorage = _storage });

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
        Assert.Throws<InvalidOperationException>(() => DesktopEnvironment.Resolve(Consumer(null)));
        Assert.Throws<InvalidOperationException>(() => DesktopEnvironment.Resolve(Consumer("../outside")));
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
    public async Task AppearanceUsesJsonAndPreservesStateOnFailure()
    {
        await _storage.WriteAsync("theme.txt", "Dark"u8.ToArray());
        using var settings = new LocalSettingsStore(_storage);
        var appearance = new AppearanceSettings(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Appearance:Theme"] = "Light" }).Build(), settings);
        await appearance.LoadAsync();
        Assert.AreEqual(ElementTheme.Light, appearance.Theme);
        await _storage.WriteAsync("appsettings.json", "{ broken"u8.ToArray());
        await Assert.ThrowsAsync<IOException>(() => appearance.SetThemeAsync(ElementTheme.Dark));
        Assert.AreEqual(ElementTheme.Light, appearance.Theme);
    }

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