using Dhara.Extensions.WinUI.Storage;
using Dhara.Extensions.WinUI.Hosting;
using Dhara.Extensions.WinUI;
using FoodHub.Core.Hosting;
using FoodHub.Core.Services;
using FoodHub.Services;
using FoodHub.ViewModels;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodHub.WinUI.Test;

/// <summary>Exercises real JSON providers and isolated persistent settings without touching customer files.</summary>
[TestClass]
[TestCategory("Unit")]
[DoNotParallelize]
public sealed class DatabaseConfigurationTests
{
    private string _root = "";
    private string _userRoot = "";
    private SettingsTestContainer _container = null!;
    private LocalSettingsStore _settings = null!;
    private readonly Dictionary<string, string?> _environment = new();
    private const string Key = "ConnectionStrings:FoodHub";
    private const string Local = "Server=localhost;Database=FoodHub_Dev;Integrated Security=True;TrustServerCertificate=True";

    /// <summary>Isolates filesystem and process environment for deterministic provider tests.</summary>
    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "FoodHub-config-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _userRoot = Path.Combine(_root, "user");
        _container = new SettingsTestContainer();
        _settings = new LocalSettingsStore(_container);
        foreach (var key in new[] { "FOODHUB_CONNECTION_STRING", "ConnectionStrings__FoodHub", "DOTNET_ENVIRONMENT" })
        {
            _environment[key] = Environment.GetEnvironmentVariable(key);
            Environment.SetEnvironmentVariable(key, null);
        }
    }
    /// <summary>Removes only the uniquely generated test directory and restores environment values.</summary>
    [TestCleanup]
    public void Cleanup()
    {
        foreach (var entry in _environment) Environment.SetEnvironmentVariable(entry.Key, entry.Value);
        _settings.Dispose();
        Directory.Delete(_root, recursive: true);
    }
    private DatabaseSettings Load(string environment = "Development")
    {
        var (configuration, error) = ApplicationConfiguration.Load(_root, environment);
        using (configuration as IDisposable) return new(configuration, error, _settings);
    }
    private void Write(string path, string value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new { ConnectionStrings = new { FoodHub = value } }));
    }
    /// <summary>Native runtime overrides resolve separately from host JSON; process variables and obsolete local JSON are ignored.</summary>
    [TestMethod]
    public void ProviderPrecedenceIsExplicit()
    {
        Write(Path.Combine(_root, "appsettings.json"), "base");
        Write(Path.Combine(_root, "appsettings.Development.json"), "development");
        Assert.AreEqual("development", Load().ConnectionString);
        Assert.AreEqual("base", Load("Production").ConnectionString);
        Environment.SetEnvironmentVariable("ConnectionStrings__FoodHub", "environment");
        Assert.AreEqual("development", Load().ConnectionString);
        Environment.SetEnvironmentVariable("FOODHUB_CONNECTION_STRING", "legacy");
        Assert.AreEqual("development", Load().ConnectionString);
        Write(Path.Combine(_userRoot, "appsettings.json"), "obsolete local JSON");
        Assert.AreEqual("development", Load().ConnectionString);
        _container.Values["Database"] = new Dictionary<string, object> { ["ConnectionString"] = "native override" };
        Assert.AreEqual("native override", Load().ConnectionString);
    }
    /// <summary>Launch location cannot alter the bundled configuration path.</summary>
    [TestMethod]
    public void LoadingIsIndependentOfWorkingDirectory()
    {
        Write(Path.Combine(_root, "appsettings.json"), Local);
        var original = Environment.CurrentDirectory;
        try { Environment.CurrentDirectory = Path.GetTempPath(); Assert.IsNull(Load().LoadError); Assert.AreEqual(Local, Load().ConnectionString); }
        finally { Environment.CurrentDirectory = original; }
    }
    /// <summary>Build metadata chooses the environment even when DOTNET_ENVIRONMENT disagrees.</summary>
    [TestMethod]
    public void BuildConfigurationIgnoresEnvironmentVariable()
    {
        var expected = ApplicationEnvironment.Resolve(typeof(DatabaseConfigurationTests).Assembly);
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Unrelated");
        Assert.AreEqual(expected, ApplicationEnvironment.Resolve(typeof(DatabaseConfigurationTests).Assembly));
    }
    /// <summary>Saving a local override cannot repair or modify malformed host files.</summary>
    [TestMethod]
    public async Task HostFilesRequireExternalRepair()
    {
        var file = Path.Combine(_root, "appsettings.json");
        await File.WriteAllTextAsync(file, "{ broken");
        var active = Load();
        Assert.IsNotNull(active.LoadError);
        await new DatabaseSettingsStore(_settings, active).SaveAsync(Local);
        Assert.IsNotNull(Load().LoadError);
        Assert.AreEqual("{ broken", await File.ReadAllTextAsync(file));
    }
    /// <summary>An invalid native group blocks fallback but explicit Save can repair that group alone.</summary>
    [TestMethod]
    public async Task InvalidLocalGroupCanBeRepaired()
    {
        Write(Path.Combine(_root, "appsettings.json"), Local);
        _container.Values["Database"] = "invalid group";
        Assert.Throws<DatabaseConfigurationException>(() => Load().RequireConnectionString());
        await new DatabaseSettingsStore(_settings, Load()).SaveAsync(Local);
        Assert.IsNull(Load().LoadError);
    }
    /// <summary>Inaccessible local storage cannot silently select the default database.</summary>
    [TestMethod]
    public void InaccessibleLocalStorageBlocksDatabaseFallback()
    {
        Write(Path.Combine(_root, "appsettings.json"), Local);
        _container.FailReads = true;
        Assert.Throws<DatabaseConfigurationException>(() => Load().RequireConnectionString());
    }
    /// <summary>Missing, invalid, and incomplete connection values produce safe setup errors.</summary>
    [TestMethod]
    [DataRow("")]
    [DataRow("not a connection string")]
    [DataRow("Server=localhost")]
    public void InvalidConnectionsAreConfigurationFailures(string value)
    {
        Write(Path.Combine(_root, "appsettings.json"), value);
        Assert.Throws<DatabaseConfigurationException>(() => Load().RequireConnectionString());
    }
    /// <summary>Explicit options bypass external configuration even if no connection is configured.</summary>
    [TestMethod]
    public void SuppliedContextOptionsTakePrecedence()
    {
        using var db = new FoodHub.Core.Models.FoodHub_DevContext(new DbContextOptionsBuilder<FoodHub.Core.Models.FoodHub_DevContext>()
            .UseSqlServer(Local).Options);
        var actual = new SqlConnectionStringBuilder(db.Database.GetConnectionString());
        Assert.AreEqual("localhost", actual.DataSource);
        Assert.AreEqual("FoodHub_Dev", actual.InitialCatalog);
        Assert.IsTrue(actual.IntegratedSecurity);
    }
    /// <summary>Saving atomically changes only the next-launch snapshot and durably requests authentication reset.</summary>
    [TestMethod]
    public async Task SavedSettingsApplyOnlyAfterReload()
    {
        Write(Path.Combine(_root, "appsettings.json"), Local);
        var active = Load();
        var store = new DatabaseSettingsStore(_settings, active);
        var changed = DatabaseSettingsStore.BuildConnectionString("other-server", "Other", false);
        await store.SaveAsync(changed);
        Assert.IsTrue(store.RestartRequired);
        Assert.AreEqual(Local, active.ConnectionString);
        Assert.AreEqual(changed, Load().ConnectionString);
        Assert.IsTrue(Load().ResetSessionOnStartup);
        Assert.IsFalse(File.Exists(Path.Combine(_userRoot, "appsettings.json") + ".tmp"));
    }
    /// <summary>Token deletion must complete before the reset marker is consumed; failures remain retryable.</summary>
    [TestMethod]
    public async Task ResetMarkerSurvivesFailedTokenDeletion()
    {
        var store = new DatabaseSettingsStore(_settings, Load());
        await store.SaveAsync(Local);
        var next = new DatabaseSettingsStore(_settings, Load());
        await Assert.ThrowsAsync<IOException>(() => next.PrepareSessionAsync(() => throw new IOException()));
        Assert.IsTrue(Load().ResetSessionOnStartup);
        var removed = false;
        await next.PrepareSessionAsync(() => { removed = true; return Task.CompletedTask; });
        Assert.IsTrue(removed);
        Assert.IsFalse(Load().ResetSessionOnStartup);
    }
    /// <summary>Reset removes only the connection override while preserving appearance, unrelated values and host bytes.</summary>
    [TestMethod]
    public async Task DefaultResetPreservesOtherSettingsAndHostFiles()
    {
        Write(Path.Combine(_root, "appsettings.json"), Local);
        var before = await File.ReadAllBytesAsync(Path.Combine(_root, "appsettings.json"));
        await _settings.WriteAsync("Other", "keep");
        await _settings.WriteAsync("Appearance.Theme", "Dark");
        await new DatabaseSettingsStore(_settings, Load()).SaveAsync("Server=other;Database=Other;Integrated Security=True");
        await new DatabaseSettingsStore(_settings, Load()).SaveAsync(null);
        Assert.AreEqual(Local, Load().ConnectionString);
        Assert.AreEqual("keep", _settings.Read("Other", ""));
        Assert.AreEqual("Dark", _settings.Read("Appearance.Theme", ""));
        Assert.IsTrue(Load().ResetSessionOnStartup);
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(Path.Combine(_root, "appsettings.json")));
        Assert.IsFalse(File.Exists(Path.Combine(_userRoot, "appsettings.json")));
    }
    /// <summary>A configuration failure is distinguishable from an unavailable SQL Server.</summary>
    [TestMethod]
    public async Task AccountsReportConfigurationFailureSafely()
    {
        var service = new AccountService(Load().Open);
        var result = await service.LoginAsync("fictional@example.test", "password", false);
        Assert.AreEqual(AccountError.Configuration, result.Error);
        StringAssert.Contains(result.Message, "Settings");
    }
    /// <summary>Saving from the view model does not require a server; a new view shows pending values.</summary>
    [TestMethod]
    public async Task SettingsViewModelPersistsPendingFields()
    {
        var active = Load();
        var store = new DatabaseSettingsStore(_settings, active);
        var tester = new DatabaseConnectionTester(NullLogger<DatabaseConnectionTester>.Instance);
        using var first = new DatabaseSettingsViewModel(active, store, tester, NullLogger<DatabaseSettingsViewModel>.Instance)
            { Server = "offline", Database = "FoodHub_Test", TrustCertificate = true };
        await first.SaveCommand.ExecuteAsync(null);
        Assert.IsTrue(first.RestartRequired);
        Assert.AreEqual("", active.ConnectionString);
        using var second = new DatabaseSettingsViewModel(active, store, tester, NullLogger<DatabaseSettingsViewModel>.Instance);
        Assert.AreEqual("offline", second.Server);
        Assert.AreEqual("FoodHub_Test", second.Database);
    }
    /// <summary>Cancellation is propagated without saving configuration or contacting SQL.</summary>
    [TestMethod]
    public async Task CancelledConnectionCheckDoesNotSave()
    {
        var tester = new DatabaseConnectionTester(NullLogger<DatabaseConnectionTester>.Instance);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => tester.TestAsync(Local, cancellation.Token));
        Assert.AreEqual(0, _container.Values.Count);
    }
    /// <summary>An unavailable local endpoint returns safe feedback within the operation deadline without saving.</summary>
    [TestMethod]
    public async Task UnavailableServerIsBoundedAndDoesNotSave()
    {
        var tester = new DatabaseConnectionTester(NullLogger<DatabaseConnectionTester>.Instance);
        var candidate = DatabaseSettingsStore.BuildConnectionString("tcp:127.0.0.1,1", "Unavailable", false);
        var message = await tester.TestAsync(candidate).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.IsTrue(message.Contains("Could not connect", StringComparison.Ordinal) || message.Contains("timed out", StringComparison.Ordinal));
        Assert.IsFalse(message.Contains(candidate, StringComparison.Ordinal));
        Assert.AreEqual(0, _container.Values.Count);
    }
    /// <summary>A failed native settings commit cannot claim that changes were saved or alter active configuration.</summary>
    [TestMethod]
    public async Task FailedSaveLeavesSnapshotAndRestartStateUnchanged()
    {
        _container.FailWrites = true;
        var active = Load();
        var store = new DatabaseSettingsStore(_settings, active);
        await Assert.ThrowsAsync<IOException>(() => store.SaveAsync(Local));
        Assert.IsFalse(store.RestartRequired);
        Assert.AreEqual("", active.ConnectionString);
    }
    /// <summary>Runtime and host convenience checks agree with the actual process identity.</summary>
    [TestMethod]
    public void PackageIdentityIsIndependentOfHostEnvironment()
    {
        using var host = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true, EnvironmentName = "Production" }).Build();
        var environment = (IHostEnvironment)host.Services.GetService(typeof(IHostEnvironment))!;
        Assert.AreEqual(new AppRuntimeEnvironment().IsPackaged, environment.IsPackaged());
        Assert.IsTrue(environment.IsProduction());
    }
}