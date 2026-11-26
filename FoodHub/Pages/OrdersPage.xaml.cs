using FoodHub.Core.Services;
using FoodHub.Services;
using FoodHub.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.ComponentModel;

namespace FoodHub.Pages;

/// <summary>Owned active and historical order cards, with explicit refresh after uncertain placement.</summary>
public sealed partial class OrdersPage : Page
{
    private readonly JourneyViewModel _vm = App.GetService<ViewModelFactory>().CreateJourney();
    /// <summary>Loads customer-scoped order cards and releases private projections when leaving.</summary>
    public OrdersPage()
    {
        InitializeComponent(); Loaded += PageLoaded;
        Unloaded += (_, _) => { _vm.PropertyChanged -= Changed; _vm.Dispose(); Active.Children.Clear(); Past.Children.Clear(); };
    }
    private async void PageLoaded(object sender, RoutedEventArgs e) { _vm.PropertyChanged += Changed; await _vm.HistoryAsync(); }
    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (!IsLoaded) return;
        BusyProgress.Visibility = _vm.IsBusy ? Visibility.Visible : Visibility.Collapsed; RefreshButton.IsEnabled = !_vm.IsBusy;
        Feedback.Message = _vm.Message.Length > 0 ? _vm.Message : _vm.Checkout.Uncertain ? "An order attempt had an uncertain result. Inspect these refreshed orders before attempting another purchase." : "";
        Feedback.IsOpen = Feedback.Message.Length > 0;
        RetryPlacement.Visibility = _vm.Checkout.Uncertain && _vm.Checkout.HistoryChecked ? Visibility.Visible : Visibility.Collapsed;
        Active.Children.Clear(); Past.Children.Clear();
        foreach (var order in _vm.History)
        {
            var content = new StackPanel { Spacing = 8 };
            content.Children.Add(ViewTools.Text(order.Number, 22));
            content.Children.Add(ViewTools.Text(DeliveryStages.Label(order.Status), 18));
            content.Children.Add(ViewTools.Text($"{DateTime.SpecifyKind(order.PlacedUtc, DateTimeKind.Utc).ToLocalTime():g} · {MenuImages.Money(order.Total, order.Currency)}"));
            var open = new Button { Content = "View receipt and tracking" };
            open.Click += (_, _) => App.MainWindow.Navigate(typeof(OrderDetailsPage), order.Id); content.Children.Add(open);
            (order.Status is 40 or 90 ? Past : Active).Children.Add(ViewTools.Card(content));
        }
        if (!_vm.IsBusy && Active.Children.Count == 0) Active.Children.Add(ViewTools.Text("No active orders."));
        if (!_vm.IsBusy && Past.Children.Count == 0) Past.Children.Add(ViewTools.Text("No past orders yet."));
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await _vm.HistoryAsync();
    private async void RetryPlacement_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Allow a new order attempt?", Content = "The previous attempt may still have succeeded. Continue only after checking refreshed order history; another confirmation can create another order.", PrimaryButtonText = "Allow new attempt", CloseButtonText = "Keep checking", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || !IsLoaded) return;
        _vm.Checkout.AcknowledgeUncertain(); App.MainWindow.Navigate(typeof(CheckoutPage));
    }
}