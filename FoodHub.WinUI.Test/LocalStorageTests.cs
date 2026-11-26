using Dhara.Extensions.WinUI.Storage;
using Dhara.Extensions.WinUI.Hosting;
using Dhara.Extensions.WinUI;
using FoodHub.Core.Hosting;
using FoodHub.Core.Services;
using FoodHub.Services;
using FoodHub.ViewModels;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;

namespace FoodHub.WinUI.Test;

/// <summary>Exercises storage selection and atomic persistence in isolated directories.</summary>
[TestClass]
[TestCategory("Unit")]
public sealed class LocalStorageTests
{
    private string _root = "";
    private LocalStorageService _storage = null!;
    /// <summary>Uses only a uniquely generated test root, never actual application settings.</summary>
    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "FoodHub-local-tests-" + Guid.NewGuid().ToString("N"));
        _storage = new LocalStorageService(Path.Combine(_root, "active"));
        Directory.CreateDirectory(_root);
    }
    /// <summary>Deletes only the known generated test directory.</summary>
    [TestCleanup]
    public void Cleanup() => Directory.Delete(_root, recursive: true);
    /// <summary>Fresh storage can read/delete absent files without requiring a precreated directory.</summary>
    [TestMethod]
    public async Task MissingFilesAreIdempotent()
    {
        Assert.IsNull(await _storage.ReadAsync("session.dat"));
        await _storage.DeleteAsync("session.dat");
    }
    /// <summary>Complete writes survive new service instances and leave no partial files.</summary>
    [TestMethod]
    public async Task AtomicWritePersistsAcrossInstances()
    {
        await _storage.WriteAsync("example.dat", Encoding.UTF8.GetBytes("Dark"));
        await _storage.WriteAsync("example.dat", Encoding.UTF8.GetBytes("Light"));
        Assert.AreEqual("Light", Encoding.UTF8.GetString((await new LocalStorageService(_storage.RootDirectory).ReadAsync("example.dat"))!));
        Assert.AreEqual(0, Directory.GetFiles(_storage.RootDirectory, "*.tmp").Length);
    }
    /// <summary>Cancelled writes cannot replace existing settings.</summary>
    [TestMethod]
    public async Task CancellationPreservesExistingBytes()
    {
        await _storage.WriteAsync("example.dat", new byte[] { 1 });
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => _storage.WriteAsync("example.dat", new byte[] { 2 }, cancelled.Token));
        CollectionAssert.AreEqual(new byte[] { 1 }, (await _storage.ReadAsync("example.dat"))!);
    }
    /// <summary>Callers cannot use application file APIs to escape the active root.</summary>
    [TestMethod]
    [DataRow("../outside")]
    [DataRow("..\\outside")]
    [DataRow("C:\\outside")]
    [DataRow("..")]
    public void PathsRemainInsideRoot(string fileName) => Assert.Throws<ArgumentException>(() => _storage.GetFilePath(fileName));
    /// <summary>Unpackaged selection uses ordinary per-Windows-user FoodHub application data.</summary>
    [TestMethod]
    public void UnpackagedStorageUsesAppData()
    {
        var selected = new ApplicationDataPaths("dashe", "FoodHub", new RuntimeStub(false));
        Assert.AreEqual(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "dashe", "FoodHub"), selected.RootDirectory);
    }
    /// <summary>Packaged selection resolves LocalFolder in an actual packaged test run.</summary>
    [TestMethod]
    public void PackagedStorageUsesLocalFolderWhenAvailable()
    {
        if (!new AppRuntimeEnvironment().IsPackaged) { Assert.Inconclusive("Run the packaged test profile for the actual package LocalFolder check."); return; }
        Assert.AreEqual(Windows.Storage.ApplicationData.Current.LocalFolder.Path, new ApplicationDataPaths("dashe", "FoodHub", new RuntimeStub(true)).RootDirectory);
    }
    private sealed record RuntimeStub(bool IsPackaged) : IAppRuntimeEnvironment;
}

/// <summary>Checks runtime-environment gating and the injected folder launcher without opening real Explorer windows.</summary>
[TestClass]
[TestCategory("Unit")]
public sealed class DevelopmentSettingsTests
{
    private static DevelopmentSettingsViewModel Create(string environment, LauncherStub launcher) => new(
        new EnvironmentStub { EnvironmentName = environment }, new LocalStorageService(Path.Combine(Path.GetTempPath(), "FoodHub-developer-test")),
        launcher, NullLogger<DevelopmentSettingsViewModel>.Instance);
    /// <summary>Visibility follows the host environment, not compilation flags or packaging.</summary>
    [TestMethod]
    [DataRow("Development", true)]
    [DataRow("Production", false)]
    [DataRow("Staging", false)]
    public async Task EnvironmentControlsVisibilityAndExecution(string environment, bool expected)
    {
        var launcher = new LauncherStub(); using var vm = Create(environment, launcher);
        Assert.AreEqual(expected, vm.IsDevelopment);
        Assert.AreEqual(expected, vm.OpenFolderCommand.CanExecute(null));
        await vm.OpenFolderCommand.ExecuteAsync(null);
        Assert.AreEqual(expected ? 1 : 0, launcher.Calls);
        Assert.AreEqual(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FoodHub-developer-test")), vm.StoragePath);
    }
    /// <summary>A launcher declining the request produces safe feedback.</summary>
    [TestMethod]
    public async Task LaunchFailureIsVisible()
    {
        using var vm = Create("Development", new LauncherStub { Result = false });
        await vm.OpenFolderCommand.ExecuteAsync(null);
        StringAssert.Contains(vm.Message, "could not open");
    }
    /// <summary>Platform exceptions do not expose raw details to the Settings page.</summary>
    [TestMethod]
    public async Task LaunchExceptionIsSafe()
    {
        using var vm = Create("Development", new LauncherStub { Failure = new UnauthorizedAccessException("sensitive details") });
        await vm.OpenFolderCommand.ExecuteAsync(null);
        Assert.IsFalse(vm.Message.Contains("sensitive details", StringComparison.Ordinal));
        Assert.IsTrue(vm.Message.Length > 0);
    }
    /// <summary>Repeated command execution cannot open duplicate windows while a launch remains pending.</summary>
    [TestMethod]
    public async Task PendingLaunchSuppressesDuplicates()
    {
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var launcher = new LauncherStub { Pending = pending.Task }; using var vm = Create("Development", launcher);
        var first = vm.OpenFolderCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.IsOpening);
        await vm.OpenFolderCommand.ExecuteAsync(null);
        Assert.AreEqual(1, launcher.Calls);
        pending.SetResult(true); await first;
        Assert.IsFalse(vm.IsOpening);
    }
    private sealed class LauncherStub : ILocalStorageFolderLauncher
    {
        public int Calls { get; private set; }
        public bool Result { get; init; } = true;
        public Exception? Failure { get; init; }
        public Task<bool>? Pending { get; init; }
        public Task<bool> OpenAsync(CancellationToken cancellation = default)
        {
            Calls++;
            return Failure is not null ? Task.FromException<bool>(Failure) : Pending ?? Task.FromResult(Result);
        }
    }
    private sealed class EnvironmentStub : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}