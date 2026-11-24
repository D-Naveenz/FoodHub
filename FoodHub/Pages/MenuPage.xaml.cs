using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace FoodHub.Pages;
/// <summary>Navigation skeleton for menu; the customer workflow is intentionally deferred.</summary>
public sealed partial class MenuPage : Page
{
    /// <summary>Creates this clearly labeled page preview.</summary>
    public MenuPage() => InitializeComponent();
    private void Next_Click(object sender, RoutedEventArgs e) => App.MainWindow.Navigate(typeof(ItemDetailsPage));
}
