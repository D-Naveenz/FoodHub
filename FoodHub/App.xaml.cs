using FoodHub.Core.Services;
using FoodHub.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FoodHub
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private readonly IHost? _host;
        private readonly CancellationTokenSource _lifetime = new();
        private readonly string? _startupFailure;
        private Task? _launchTask;
        private Task? _shutdownTask;
        private CancellationTokenRegistration _stoppingRegistration;
        private IMainWindowService? _windows;
        private bool _closing;
        /// <summary>Resolves dependencies only at WinUI construction boundaries.</summary>
        public static T GetService<T>() where T : notnull => ((App)Current)._host!.Services.GetRequiredService<T>();
        /// <summary>Forwards to the hosted window service for existing pickers and owned-window behavior.</summary>
        public static MainWindow MainWindow => ((App)Current)._windows!.Window;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            InitializeComponent();
            try { _host = ApplicationHost.Build(); }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or System.ComponentModel.Win32Exception)
            {
                // No logger exists if storage-root/host construction fails. Never include exception payloads.
                System.Diagnostics.Trace.TraceError("Application bootstrap failed: {0}", ex.GetType().Name);
                _startupFailure = "FoodHub could not access its local storage. Check Windows application data permissions and restart.";
            }
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            if (_host is null)
            {
                var errorWindow = new Window { Content = new TextBlock { Text = _startupFailure, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(32) } };
                errorWindow.Closed += (_, _) => { _lifetime.Dispose(); Exit(); };
                errorWindow.Activate();
                return;
            }
            _windows = GetService<IMainWindowService>();
            var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            _stoppingRegistration = GetService<IHostApplicationLifetime>().ApplicationStopping.Register(
                () => dispatcher.TryEnqueue(RequestShutdown));
            try { _launchTask = LaunchAsync(); await _launchTask; }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                GetService<ILogger<App>>().LogError("Application startup failed: {Category}", ex.GetType().Name);
                RequestShutdown();
            }
        }

        /// <summary>Starts hosted services before activation; private state is restored only after storage preparation.</summary>
        private async Task LaunchAsync()
        {
            await _host!.StartAsync(_lifetime.Token);
            var session = GetService<SessionState>();
            var logger = GetService<ILogger<App>>();
            try
            {
                await GetService<DatabaseSettingsStore>().PrepareSessionAsync(() => GetService<SessionStorage>().WriteAsync(null));
                _windows!.Activate();
                if (!_closing) await session.RestoreAsync(_lifetime.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (ex is DatabaseConfigurationException or System.IO.IOException or UnauthorizedAccessException)
            {
                logger.LogWarning("Session preparation failed: {Category}", ex.GetType().Name);
                _windows!.Activate();
                if (!_closing) session.Message = "Account settings need attention. Open Settings to repair configuration or check access to app data.";
            }
        }

        /// <summary>All close sources converge here; repeated requests reuse the same shutdown task.</summary>
        private void RequestShutdown() => _shutdownTask ??= ShutdownAsync();

        /// <summary>Stops/disposes the host before releasing the dispatcher and its native window.</summary>
        private async Task ShutdownAsync()
        {
            _closing = true;
            _lifetime.Cancel();
            try
            {
                // Startup must finish cancelling before hosted service shutdown can begin.
                if (_launchTask is not null)
                    try { await _launchTask; } catch (Exception) { /* OnLaunched already reports startup failures safely. */ }
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _host!.StopAsync(timeout.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { GetService<ILogger<App>>().LogError("Application shutdown failed: {Category}", ex.GetType().Name); }
            finally
            {
                _stoppingRegistration.Dispose();
                _host!.Dispose();
                _lifetime.Dispose();
                if (_windows is not null)
                {
                    try { _ = _windows.Window; _windows.CompleteShutdown(); }
                    catch (InvalidOperationException) { Exit(); }
                }
                else Exit();
            }
        }
    }
}