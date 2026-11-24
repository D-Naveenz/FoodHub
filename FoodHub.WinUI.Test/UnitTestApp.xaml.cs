using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FoodHub.WinUI.Test
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class UnitTestApp : Application
    {
        private Window? _window;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public UnitTestApp()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
#if MSTEST_MTP
        protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
#else
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
#endif
        {
#if !MSTEST_MTP
            Microsoft.VisualStudio.TestPlatform.TestExecutor.UnitTestClient.CreateDefaultUI();
#endif

            _window = new UnitTestAppWindow();
            _window.Activate();

            UITestMethodAttribute.DispatcherQueue = _window.DispatcherQueue;

#if MSTEST_MTP
            // The WinUI application owns its UI thread and hosts the same MSTest tests in-process.
            // Propagate failures to the CLI and close the window so automated runs cannot hang.
            try
            {
                Environment.ExitCode = await MicrosoftTestingPlatformApplication.RunAsync(Environment.GetCommandLineArgs()[1..]);
            }
            finally { _window.Close(); Exit(); }
#else
            Microsoft.VisualStudio.TestPlatform.TestExecutor.UnitTestClient.Run(Environment.CommandLine);
#endif
        }
    }
}
