using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace FoodHub.Pages;

/// <summary>Presents an owned saved receipt through the shared tracking view.</summary>
public sealed partial class OrderDetailsPage : Page
{
    /// <summary>Creates the receipt route without loading unscoped order data.</summary>
    public OrderDetailsPage() => InitializeComponent();
    /// <summary>Passes the navigation identity to the view model's ownership-checked reader.</summary>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ReceiptView.OrderId = e.Parameter is int id ? id : 0;
    }
}