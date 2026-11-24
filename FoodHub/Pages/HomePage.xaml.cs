using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FoodHub.Pages;
/// <summary>Branded starting point for the customer dashboard.</summary>
public sealed partial class HomePage : Page
{
    /// <summary>Initializes the home navigation cards.</summary>
    public HomePage() => InitializeComponent();
    private void Menu_Click(object sender, RoutedEventArgs e) => App.MainWindow.Navigate(typeof(MenuPage));
    private void Basket_Click(object sender, RoutedEventArgs e) => App.MainWindow.Navigate(typeof(BasketPage));
    private void Profile_Click(object sender, RoutedEventArgs e) => App.MainWindow.Navigate(typeof(ProfilePage));
}
