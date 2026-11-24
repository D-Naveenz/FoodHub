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
        private IHost? _host;
        private readonly CancellationTokenSource _lifetime = new();
        private bool _closing;
        /// <summary>Resolves dependencies only at WinUI construction boundaries.</summary>
        public static T GetService<T>() where T : notnull => ((App)Current)._host!.Services.GetRequiredService<T>();
        /// <summary>The dashboard and owner of secondary windows.</summary>
        public static MainWindow MainWindow { get; private set; } = null!;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            _host = ApplicationHost.Build();
            await _host.StartAsync(_lifetime.Token);
            // Resolve UI-owned services after the await resumes on the WinUI dispatcher.
            _ = GetService<IAppRuntimeEnvironment>();
            MainWindow = GetService<MainWindow>();
            MainWindow.Closed += MainWindowClosed;
            var session = GetService<SessionState>();
            var logger = GetService<ILogger<App>>();
            try
            {
                await GetService<DatabaseSettingsStore>().PrepareSessionAsync(() => GetService<SessionStorage>().WriteAsync(null));
                MainWindow.Activate();
                if (!_closing) await session.RestoreAsync(_lifetime.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (ex is DatabaseConfigurationException or System.IO.IOException or UnauthorizedAccessException)
            {
                logger.LogWarning("Session preparation failed: {Category}", ex.GetType().Name);
                MainWindow.Activate();
                if (!_closing) session.Message = "Account settings need attention. Open Settings to repair configuration or check access to app data.";
            }
        }

        /// <summary>Keeps the dispatcher alive until bounded host shutdown and UI-service disposal finish.</summary>
        private async void MainWindowClosed(object sender, WindowEventArgs args)
        {
            args.Handled = true;
            if (_closing) return;
            _closing = true;
            _lifetime.Cancel();
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _host!.StopAsync(timeout.Token);
            }
            catch (OperationCanceledException) { }
            finally
            {
                _host!.Dispose();
                _lifetime.Dispose();
                MainWindow.Closed -= MainWindowClosed;
                // Queue the final close after this Closed event returns; reentrant Close is ignored by WinUI.
                MainWindow.DispatcherQueue.TryEnqueue(() => MainWindow.Close());
            }
        }
    }
}