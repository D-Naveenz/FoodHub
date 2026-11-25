using Microsoft.Extensions.Configuration;
using FoodHub.Core.Services;
using FoodHub.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;

// These native test windows isolate hosted-service lifetime from unrelated customer navigation.
namespace FoodHub
{
    /// <summary>Minimal native main window used only by hosted-service tests.</summary>
    public sealed class MainWindow : Window
    {
        /// <summary>Tracks owned-window shutdown using the same service contract as the application.</summary>
        public Window? AuthenticationWindow { get; set; }
        /// <summary>Provides content so this native window can be activated during lifetime checks.</summary>
        public MainWindow() => Content = new TextBlock { Text = "FoodHub window lifetime test" };
    }
    /// <summary>Supplies the appearance service's window access boundary in this isolated test assembly.</summary>
    public static class App
    {
        /// <summary>Set by a test if applying appearance rather than merely loading it.</summary>
        public static MainWindow MainWindow { get; set; } = null!;
    }
}

namespace FoodHub.WinUI.Test
{
    /// <summary>Exercises the real hosted service with native windows and the running test application's dispatcher.</summary>
    [TestClass]
    [TestCategory("WindowsStorage")]
    [DoNotParallelize]
    public sealed class WindowLifetimeTests
    {
        /// <summary>Startup creates one window, appearance loads first, and repeated stop is safe.</summary>
        [TestMethod]
        public Task StartsOnceAndStopsOnDispatcher() => OnDispatcher(async () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "FoodHub-window-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var storage = new LocalStorageService(directory);
            using var settings = new LocalSettingsStore(storage);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["Appearance:Theme"] = "Dark" }).Build();
            var appearance = new AppearanceSettings(configuration, settings);
            var lifetime = new LifetimeStub();
            var created = 0;
            var service = new MainWindowHostedService(() =>
            {
                Assert.AreEqual(ElementTheme.Dark, appearance.Theme);
                created++;
                return new MainWindow();
            }, appearance, lifetime);
            try
            {
                await service.StartAsync(CancellationToken.None);
                await service.StartAsync(CancellationToken.None);
                Assert.AreEqual(1, created);
                service.Activate(); service.Activate();
                service.Window.Close();
                Assert.AreEqual(1, lifetime.StopRequests);
                // Hosted Stop may be called after another service resumed the host on a worker thread.
                await Task.Run(() => service.StopAsync(CancellationToken.None));
                await service.StopAsync(CancellationToken.None);
                service.Window.Close();
                Assert.AreEqual(1, lifetime.StopRequests);
            }
            finally { service.Window.Close(); Directory.Delete(directory, recursive: true); }
        });
        /// <summary>A successful appearance save updates both active windows and preserves database settings.</summary>
        [TestMethod]
        public Task AppearanceAppliesToCurrentWindowsAfterSave() => OnDispatcher(async () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "FoodHub-appearance-test-" + Guid.NewGuid().ToString("N"));
            var storage = new LocalStorageService(directory);
            using var settings = new LocalSettingsStore(storage);
            var appearance = new AppearanceSettings(new ConfigurationBuilder().Build(), settings);
            var window = new MainWindow { AuthenticationWindow = new Window { Content = new TextBlock() } };
            App.MainWindow = window;
            try
            {
                await settings.UpdateAsync(json => json["ConnectionStrings"] = new System.Text.Json.Nodes.JsonObject { ["FoodHub"] = "keep" });
                await appearance.SetThemeAsync(ElementTheme.Dark);
                Assert.AreEqual(ElementTheme.Dark, ((FrameworkElement)window.Content).RequestedTheme);
                Assert.AreEqual(ElementTheme.Dark, ((FrameworkElement)window.AuthenticationWindow.Content).RequestedTheme);
                var json = System.Text.Json.Nodes.JsonNode.Parse((await storage.ReadAsync("appsettings.json"))!)!;
                Assert.AreEqual("Dark", json["Appearance"]!["Theme"]!.GetValue<string>());
                Assert.AreEqual("keep", json["ConnectionStrings"]!["FoodHub"]!.GetValue<string>());
            }
            finally
            {
                window.AuthenticationWindow.Close();
                window.Close();
                App.MainWindow = null!;
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
        });
        /// <summary>Cancellation before startup cannot allocate a native window.</summary>
        [TestMethod]
        public Task CancelledStartupCreatesNoWindow() => OnDispatcher(async () =>
        {
            using var settings = new LocalSettingsStore(new LocalStorageService(Path.Combine(Path.GetTempPath(), "FoodHub-no-files")));
            var appearance = new AppearanceSettings(new ConfigurationBuilder().Build(), settings);
            var created = false;
            var service = new MainWindowHostedService(() => { created = true; return new MainWindow(); }, appearance, new LifetimeStub());
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => service.StartAsync(cancelled.Token));
            Assert.IsFalse(created);
            await service.StopAsync(CancellationToken.None);
        });
        /// <summary>Runs an asynchronous test on the real dispatcher without blocking its message loop.</summary>
        private static Task OnDispatcher(Func<Task> test)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var dispatcher = UITestMethodAttribute.DispatcherQueue ?? throw new InvalidOperationException("Test dispatcher is unavailable.");
            if (!dispatcher.TryEnqueue(async () =>
            {
                try { await test(); completion.SetResult(); }
                catch (Exception ex) { completion.SetException(ex); }
            })) completion.SetException(new InvalidOperationException("Test dispatcher is unavailable."));
            return completion.Task;
        }
        private sealed class LifetimeStub : IHostApplicationLifetime
        {
            public int StopRequests { get; private set; }
            public CancellationToken ApplicationStarted => CancellationToken.None;
            public CancellationToken ApplicationStopping => CancellationToken.None;
            public CancellationToken ApplicationStopped => CancellationToken.None;
            public void StopApplication() => StopRequests++;
        }
    }
}