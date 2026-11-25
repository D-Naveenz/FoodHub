using Dhara.Extensions.WinUI.Hosting;
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
        private readonly string? _startupFailure;
        private IWinUIApplicationLifetime<MainWindow>? _application;
        /// <summary>Resolves dependencies only at WinUI construction boundaries.</summary>
        public static T GetService<T>() where T : notnull => ((App)Current)._host!.Services.GetRequiredService<T>();
        /// <summary>Forwards to the hosted window service for existing pickers and owned-window behavior.</summary>
        public static MainWindow MainWindow => ((App)Current)._application!.MainWindow;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            InitializeComponent();
            try { _host = ApplicationHost.Build(this); }
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
                errorWindow.Closed += (_, _) => Exit();
                errorWindow.Activate();
                return;
            }
            _application = GetService<IWinUIApplicationLifetime<MainWindow>>();
            try { await _application.StartAsync(args); }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                // Dhara has already scheduled host cleanup. The app keeps only this async-void exception boundary.
                System.Diagnostics.Trace.TraceError("Application startup failed: {0}", ex.GetType().Name);
                await _application.RequestShutdownAsync();
            }
        }
    }
}