using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace FoodHub.Pages;
/// <summary>Navigation skeleton for checkout; the customer workflow is intentionally deferred.</summary>
public sealed partial class CheckoutPage : Page
{
    /// <summary>Creates this clearly labeled page preview.</summary>
    public CheckoutPage() => InitializeComponent();
    private void Next_Click(object sender, RoutedEventArgs e) => App.MainWindow.Navigate(typeof(ConfirmationPage));
}
