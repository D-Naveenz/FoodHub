using System.Runtime.InteropServices;
using Dhara.Extensions.WinUI.Hosting;
using Dhara.Extensions.WinUI.Threading;
using Dhara.Extensions.WinUI.Windowing;
using FoodHub.Core.Services;
using FoodHub.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;

namespace FoodHub.WinUI.Test;

/// <summary>Uses the test application's existing UI thread without constructing another Application.</summary>
internal static class WinUITestSupport
{
    /// <summary>Gets the test host's dispatcher through the same adapter used by consumers.</summary>
    internal static IUIThreadDispatcher Dispatcher => new UIThreadDispatcher(UITestMethodAttribute.DispatcherQueue
        ?? throw new InvalidOperationException("The test dispatcher is unavailable."));
    /// <summary>Runs the entire asynchronous native test on the real UI dispatcher.</summary>
    internal static Task OnDispatcher(Func<Task> action) => Dispatcher.InvokeAsync(action);
    /// <summary>Allows previously queued native closure to complete before assertions.</summary>
    internal static Task NextTurn()
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!Dispatcher.TryEnqueue(() => completed.SetResult())) throw new InvalidOperationException("The UI queue rejected the test.");
        return completed.Task;
    }
}

/// <summary>Exercises public Dhara lifecycle services with native windows and the real Generic Host.</summary>
[TestClass]
[TestCategory("WindowsStorage")]
[DoNotParallelize]
public sealed class WindowLifetimeTests
{
    private string _root = "";
    /// <summary>Creates one isolated content and storage root per test.</summary>
    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "Dhara-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }
    /// <summary>Removes only the known generated directory after native shutdown.</summary>
    [TestCleanup]
    public void Cleanup() => Directory.Delete(_root, recursive: true);
    /// <summary>Builds the library with no FoodHub-specific registrations or custom Application.</summary>
    private WinUIApplicationBuilder Builder(TimeSpan? timeout = null)
    {
        var builder = WinUIApplication.CreateBuilder(Application.Current, new WinUIApplicationOptions
        {
            ApplicationDataName = "Dhara.Tests", ContentRootPath = _root,
            LocalStorageRoot = Path.Combine(_root, "user"), Dispatcher = WinUITestSupport.Dispatcher,
            // A failed startup must not terminate the Application owned by the test runner.
            ExitApplication = () => { }, ShutdownTimeout = timeout ?? TimeSpan.FromSeconds(5)
        });
        builder.UseWinUIApplication<TestWindow>();
        return builder;
    }
    /// <summary>Startup orders hooks once; programmatic close stops background work before final native closure.</summary>
    [TestMethod]
    public Task StartsOnceAndStopsBackgroundWork() => WinUITestSupport.OnDispatcher(async () =>
    {
        var builder = Builder(); var events = new List<string>();
        builder.Services.AddSingleton<IWinUIStartupParticipant>(new RecordingParticipant(events));
        builder.Services.AddSingleton(sp => { events.Add("window"); return new TestWindow(); });
        builder.Services.AddSingleton<WorkerProbe>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<WorkerProbe>());
        var host = builder.Build();
        var application = host.Services.GetRequiredService<IWinUIApplicationLifetime<TestWindow>>();
        var registry = host.Services.GetRequiredService<IWindowRegistry>();
        var worker = host.Services.GetRequiredService<WorkerProbe>();
        try
        {
            Assert.Throws<InvalidOperationException>(() => application.MainWindow);
            // Native launch arguments cannot be constructed publicly; these hooks do not inspect them.
            await application.StartAsync(null!); await application.StartAsync(null!);
            await worker.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            CollectionAssert.AreEqual(new[] { "before", "window", "activate", "after" }, events);
            Assert.AreEqual(1, registry.Windows.Count);
            application.MainWindow.Close();
            await application.RequestShutdownAsync(); await application.RequestShutdownAsync();
            await WinUITestSupport.NextTurn();
            Assert.IsTrue(worker.Stopped); Assert.AreEqual(0, registry.Windows.Count);
        }
        finally { await application.RequestShutdownAsync(); }
    });
    /// <summary>Worker-originated StopApplication closes secondary windows through the UI queue.</summary>
    [TestMethod]
    public Task WorkerStopClosesSecondaryWindows() => WinUITestSupport.OnDispatcher(async () =>
    {
        var host = Builder().Build();
        var application = host.Services.GetRequiredService<IWinUIApplicationLifetime<TestWindow>>();
        var registry = host.Services.GetRequiredService<IWindowRegistry>();
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        await application.StartAsync(null!);
        var child = new TestWindow(); registry.Register(child); child.Activate();
        var closed = false; child.Closed += (_, _) => closed = true;
        await Task.Run(lifetime.StopApplication);
        await application.RequestShutdownAsync(); await WinUITestSupport.NextTurn();
        Assert.IsTrue(closed); Assert.AreEqual(0, registry.Windows.Count);
    });
    /// <summary>The title-bar close message is cancelled until host shutdown has released its dependencies.</summary>
    [TestMethod]
    public Task NativeCloseWaitsForHostCleanup() => WinUITestSupport.OnDispatcher(async () =>
    {
        var host = Builder().Build();
        var application = host.Services.GetRequiredService<IWinUIApplicationLifetime<TestWindow>>();
        var registry = host.Services.GetRequiredService<IWindowRegistry>();
        await application.StartAsync(null!);
        Native.SendMessage(application.MainWindow.GetWindowHandle(), 0x0010, 0, 0); // WM_CLOSE
        Assert.AreEqual(1, registry.Windows.Count);
        await application.RequestShutdownAsync(); await WinUITestSupport.NextTurn();
        Assert.AreEqual(0, registry.Windows.Count);
    });
    /// <summary>A shutdown request cancels a pending startup hook and prevents late native allocation.</summary>
    [TestMethod]
    public Task CloseDuringStartupCancelsWindowCreation() => WinUITestSupport.OnDispatcher(async () =>
    {
        var builder = Builder(); var participant = new WaitingParticipant(); var created = false;
        builder.Services.AddSingleton<IWinUIStartupParticipant>(participant);
        builder.Services.AddSingleton(sp => { created = true; return new TestWindow(); });
        var host = builder.Build();
        var application = host.Services.GetRequiredService<IWinUIApplicationLifetime<TestWindow>>();
        var startup = application.StartAsync(null!); await participant.Entered.Task;
        await application.RequestShutdownAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => startup);
        Assert.IsFalse(created); await WinUITestSupport.NextTurn();
    });
    /// <summary>Precancelled startup creates no window and remains safe to clean up repeatedly.</summary>
    [TestMethod]
    public Task CancelledStartupCreatesNoWindow() => WinUITestSupport.OnDispatcher(async () =>
    {
        var host = Builder().Build();
        var application = host.Services.GetRequiredService<IWinUIApplicationLifetime<TestWindow>>();
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => application.StartAsync(null!, cancelled.Token));
        Assert.Throws<InvalidOperationException>(() => application.MainWindow);
        await application.RequestShutdownAsync(); await WinUITestSupport.NextTurn();
    });
    /// <summary>A failing startup hook causes cleanup and disposes already resolved dependencies.</summary>
    [TestMethod]
    public Task FailedStartupDisposesHost() => WinUITestSupport.OnDispatcher(async () =>
    {
        var builder = Builder();
        builder.Services.AddSingleton<IWinUIStartupParticipant, FailingParticipant>();
        builder.Services.AddSingleton<DisposalProbe>();
        var host = builder.Build(); var probe = host.Services.GetRequiredService<DisposalProbe>();
        var application = host.Services.GetRequiredService<IWinUIApplicationLifetime<TestWindow>>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => application.StartAsync(null!));
        await application.RequestShutdownAsync(); await WinUITestSupport.NextTurn();
        Assert.IsTrue(probe.Disposed);
    });
    /// <summary>An uncooperative StopAsync does not hold native windows open beyond the shutdown budget.</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task ShutdownFailureStillDisposesAndCloses(bool throwOnStop) => WinUITestSupport.OnDispatcher(async () =>
    {
        var builder = Builder(TimeSpan.FromMilliseconds(100)); var service = new SlowStopService(throwOnStop);
        // Factory registrations are container-owned; a supplied instance would remain owned by this test.
        builder.Services.AddSingleton<IHostedService>(_ => service);
        var host = builder.Build();
        var application = host.Services.GetRequiredService<IWinUIApplicationLifetime<TestWindow>>();
        var registry = host.Services.GetRequiredService<IWindowRegistry>();
        await application.StartAsync(null!);
        try { await application.RequestShutdownAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
        finally { service.Release.TrySetResult(); }
        await WinUITestSupport.NextTurn();
        Assert.IsTrue(service.Disposed); Assert.AreEqual(0, registry.Windows.Count);
    });
    /// <summary>FoodHub persists appearance while Dhara applies it to all registered native windows.</summary>
    [TestMethod]
    public Task AppearanceAppliesToCurrentWindowsAfterSave() => WinUITestSupport.OnDispatcher(async () =>
    {
        var registry = new WindowRegistry(WinUITestSupport.Dispatcher);
        var themes = new WindowThemeService(registry, WinUITestSupport.Dispatcher);
        var storage = new LocalStorageService(_root); using var settings = new LocalSettingsStore(storage);
        var appearance = new AppearanceSettings(new ConfigurationBuilder().Build(), settings, themes);
        var main = new TestWindow(); var child = new TestWindow(); registry.Register(main); registry.Register(child);
        try
        {
            await settings.UpdateAsync(json => json["ConnectionStrings"] = new System.Text.Json.Nodes.JsonObject { ["FoodHub"] = "keep" });
            await appearance.SetThemeAsync(ElementTheme.Dark);
            Assert.AreEqual(ElementTheme.Dark, ((FrameworkElement)main.Content).RequestedTheme);
            Assert.AreEqual(ElementTheme.Dark, ((FrameworkElement)child.Content).RequestedTheme);
            var json = System.Text.Json.Nodes.JsonNode.Parse((await storage.ReadAsync("appsettings.json"))!)!;
            Assert.AreEqual("keep", json["ConnectionStrings"]!["FoodHub"]!.GetValue<string>());
            Assert.AreEqual("Dark", json["Appearance"]!["Theme"]!.GetValue<string>());
        }
        finally { child.Close(); main.Close(); }
    });
    /// <summary>Ownership, input and monitor-bounded placement work without customer/account dependencies.</summary>
    [TestMethod]
    public Task NativeHelpersPreserveOwnershipAndInput() => WinUITestSupport.OnDispatcher(() =>
    {
        var owner = new TestWindow(); var child = new TestWindow();
        try
        {
            owner.Activate(); child.SetOwner(owner);
            Assert.AreEqual(owner.GetWindowHandle(), Native.GetWindow(child.GetWindowHandle(), 4)); // GW_OWNER
            child.CenterOnOwner(owner, 900, 600);
            var area = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(owner.AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary).WorkArea;
            Assert.IsTrue(child.AppWindow.Size.Width <= area.Width && child.AppWindow.Size.Height <= area.Height);
            owner.SetInputEnabled(false); Assert.IsFalse(Native.IsWindowEnabled(owner.GetWindowHandle()));
            owner.SetInputEnabled(true); Assert.IsTrue(Native.IsWindowEnabled(owner.GetWindowHandle()));
            owner.InitializeOwnedUI(new Windows.Storage.Pickers.FileOpenPicker());
        }
        finally { owner.SetInputEnabled(true); child.Close(); owner.Close(); }
        return Task.CompletedTask;
    });
    /// <summary>Worker callbacks return to the existing UI thread, including after asynchronous continuations.</summary>
    [TestMethod]
    public Task WorkerCallbacksUseTheExistingDispatcher() => WinUITestSupport.OnDispatcher(async () =>
    {
        var dispatcher = WinUITestSupport.Dispatcher;
        await Task.Run(() => dispatcher.InvokeAsync(async () =>
        {
            Assert.IsTrue(dispatcher.HasThreadAccess); await Task.Yield(); Assert.IsTrue(dispatcher.HasThreadAccess);
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(() => dispatcher.InvokeAsync(
            (Action)(() => throw new InvalidOperationException("Expected test error.")))));
    });
    /// <summary>Dispatcher shutdown and cancelled enqueue attempts complete with observable errors.</summary>
    [TestMethod]
    public async Task DispatcherReportsStoppedQueue()
    {
        var controller = Microsoft.UI.Dispatching.DispatcherQueueController.CreateOnDedicatedThread();
        var dispatcher = new UIThreadDispatcher(controller.DispatcherQueue);
        await dispatcher.InvokeAsync(() => { }); await controller.ShutdownQueueAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.InvokeAsync(() => { }));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => dispatcher.InvokeAsync(() => { }, cancelled.Token));
    }

    /// <summary>Cancellation completes a queued task promptly and suppresses execution while the queue is busy.</summary>
    [TestMethod]
    public async Task DispatcherCancelsQueuedWork()
    {
        var controller = Microsoft.UI.Dispatching.DispatcherQueueController.CreateOnDedicatedThread();
        var dispatcher = new UIThreadDispatcher(controller.DispatcherQueue);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocking = dispatcher.InvokeAsync(() => { entered.SetResult(); release.Wait(); });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var executed = false;
        using var cancellation = new CancellationTokenSource();
        try
        {
            var queued = dispatcher.InvokeAsync(() => executed = true, cancellation.Token);
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => queued.WaitAsync(TimeSpan.FromSeconds(3)));
        }
        finally { release.Set(); await blocking; await controller.ShutdownQueueAsync(); }
        Assert.IsFalse(executed);
    }
    /// <summary>One main window is registered, and the library depends on no FoodHub assemblies.</summary>
    [TestMethod]
    public Task GenericRegistrationIsIndependentOfFoodHub() => WinUITestSupport.OnDispatcher(() =>
    {
        var builder = Builder(); Assert.Throws<InvalidOperationException>(() => builder.UseWinUIApplication<TestWindow>());
        using var host = builder.Build();
        Assert.IsFalse(typeof(WinUIApplication).Assembly.GetReferencedAssemblies().Any(x => x.Name!.StartsWith("FoodHub", StringComparison.Ordinal)));
        return Task.CompletedTask;
    });
    /// <summary>A plain native test window without account navigation or application statics.</summary>
    public sealed class TestWindow : Window
    {
        /// <summary>Creates initialized content for activation and appearance assertions.</summary>
        public TestWindow() => Content = new TextBlock { Text = "Dhara lifecycle test" };
    }
    /// <summary>Records the three startup stages and verifies UI affinity after an await.</summary>
    private sealed class RecordingParticipant(List<string> events) : IWinUIStartupParticipant
    {
        /// <inheritdoc/>
        public async Task BeforeWindowCreatedAsync(CancellationToken cancellation)
        { await Task.Yield(); Assert.IsTrue(WinUITestSupport.Dispatcher.HasThreadAccess); events.Add("before"); }
        /// <inheritdoc/>
        public Task BeforeWindowActivatedAsync(CancellationToken cancellation) { events.Add("activate"); return Task.CompletedTask; }
        /// <inheritdoc/>
        public Task AfterWindowActivatedAsync(LaunchActivatedEventArgs args, CancellationToken cancellation) { events.Add("after"); return Task.CompletedTask; }
    }
    /// <summary>Pauses launch before native allocation until application cancellation arrives.</summary>
    private sealed class WaitingParticipant : IWinUIStartupParticipant
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <inheritdoc/>
        public async Task BeforeWindowCreatedAsync(CancellationToken cancellation)
        { Entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, cancellation); }
    }
    /// <summary>Provides an intentional authored launch failure.</summary>
    private sealed class FailingParticipant : IWinUIStartupParticipant
    {
        /// <inheritdoc/>
        public Task BeforeWindowCreatedAsync(CancellationToken cancellation) => throw new InvalidOperationException("Expected test failure.");
    }
    /// <summary>Records provider disposal after aborted startup.</summary>
    private sealed class DisposalProbe : IDisposable
    {
        internal bool Disposed { get; private set; }
        /// <inheritdoc/>
        public void Dispose() => Disposed = true;
    }
    /// <summary>Deliberately ignores StopAsync cancellation to verify bounded native shutdown.</summary>
    private sealed class SlowStopService(bool throwOnStop) : IHostedService, IDisposable
    {
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Disposed { get; private set; }
        /// <inheritdoc/>
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        /// <inheritdoc/>
        public Task StopAsync(CancellationToken cancellationToken) => throwOnStop
            ? Task.FromException(new InvalidOperationException("Expected test stop failure.")) : Release.Task;
        /// <inheritdoc/>
        public void Dispose() => Disposed = true;
    }
    /// <summary>Runs genuine background work and observes cooperative host cancellation.</summary>
    private sealed class WorkerProbe : BackgroundService
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Stopped { get; private set; }
        /// <inheritdoc/>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Started.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken); }
            catch (OperationCanceledException) { }
            finally { Stopped = true; }
        }
    }
    /// <summary>Observes native state and emulates the same close message sent by a title-bar button.</summary>
    private static class Native
    {
        [DllImport("user32.dll")]
        internal static extern nint SendMessage(nint hwnd, uint message, nint wParam, nint lParam);
        [DllImport("user32.dll")]
        internal static extern nint GetWindow(nint hwnd, uint command);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowEnabled(nint hwnd);
    }
}