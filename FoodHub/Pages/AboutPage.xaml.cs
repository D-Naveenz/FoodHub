using Microsoft.UI.Xaml.Controls;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FoodHub.Pages
{
    /// <summary>Displays application release information from its assembly metadata.</summary>
    public sealed partial class AboutPage : Page
    {
        /// <summary>Shows the release version without repeating a version constant in UI code.</summary>
        public string ApplicationVersion { get; } = $"FoodHub v{typeof(App).Assembly.GetName().Version!.ToString(3)}";

        /// <summary>Creates the About page and its version binding.</summary>
        public AboutPage()
        {
            InitializeComponent();
        }
    }
}