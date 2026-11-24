using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace FoodHub.Pages;
/// <summary>Navigation skeleton for orders; the customer workflow is intentionally deferred.</summary>
public sealed partial class OrdersPage : Page
{
    /// <summary>Creates this clearly labeled page preview.</summary>
    public OrdersPage() => InitializeComponent();
    private void Next_Click(object sender, RoutedEventArgs e) => App.MainWindow.Navigate(typeof(OrderDetailsPage));
}
