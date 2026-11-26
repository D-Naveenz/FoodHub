using FoodHub.Core.Services;
using FoodHub.Services;
using FoodHub.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.ComponentModel;

namespace FoodHub.Pages;

/// <summary>Checkout presentation; revalidation and placement are application workflow operations.</summary>
public sealed partial class CheckoutPage : Page
{
    private readonly JourneyViewModel _vm = App.GetService<ViewModelFactory>().CreateJourney();
    private int? _addressId;
    private bool _binding;
    /// <summary>Loads a fresh owned review and observes navigation-independent placement.</summary>
    public CheckoutPage()
    {
        InitializeComponent(); Loaded += PageLoaded;
        Unloaded += (_, _) => { _vm.PropertyChanged -= Changed; _vm.Checkout.PropertyChanged -= Changed; _vm.Dispose(); };
    }
    private async void PageLoaded(object sender, RoutedEventArgs e)
    {
        _vm.PropertyChanged += Changed; _vm.Checkout.PropertyChanged += Changed;
        Render(); await _vm.ReviewAsync();
    }
    private void Changed(object? sender, PropertyChangedEventArgs e) { if (IsLoaded) Render(); }
    private void Render()
    {
        BusyProgress.Visibility = _vm.IsBusy || _vm.Checkout.IsPlacing ? Visibility.Visible : Visibility.Collapsed;
        Feedback.Message = _vm.Checkout.Uncertain ? "The previous order may have succeeded. Check refreshed history before deliberately trying again." : _vm.Message;
        Feedback.IsOpen = Feedback.Message.Length > 0;
        Lines.Children.Clear();
        var review = _vm.Review;
        _binding = true;
        Addresses.ItemsSource = review?.Profile.Addresses.Select(a => new AddressOption(a.Id, $"{a.Label} · {a.Line1}, {a.Line2} {a.City} {a.PostalCode}")).ToArray();
        Addresses.SelectedValue = _addressId;
        _binding = false;
        Contact.Text = review is null ? "Refresh checkout to load current contact details." : $"{review.Profile.FullName} · {review.Profile.Phone}";
        if (review is not null)
        {
            foreach (var line in review.Lines) Lines.Children.Add(ViewTools.Text($"{line.Quantity} × {line.Name} · {MenuImages.Money(line.Price * line.Quantity, review.Prices.Currency)}{(line.Available ? "" : " · Unavailable")}"));
            Totals.Text = $"Subtotal: {MenuImages.Money(review.Subtotal, review.Prices.Currency)}\nDelivery: {MenuImages.Money(review.Prices.DeliveryFee, review.Prices.Currency)}\nTotal: {MenuImages.Money(review.Total, review.Prices.Currency)}";
            if (review.Profile.Addresses.Count == 0) Lines.Children.Add(ViewTools.Text("Save a delivery address in your profile to continue."));
        }
        else Totals.Text = "";
        PlaceButton.IsEnabled = review is not null && review.Lines.All(l => l.Available) && _addressId is not null && !_vm.IsBusy && !_vm.Checkout.IsPlacing && !_vm.Checkout.Uncertain;
        Addresses.IsEnabled = !_vm.IsBusy && !_vm.Checkout.IsPlacing;
        RefreshButton.IsEnabled = !_vm.IsBusy && !_vm.Checkout.IsPlacing;
        HistoryButton.Visibility = _vm.Checkout.Uncertain ? Visibility.Visible : Visibility.Collapsed;
        SavedButton.Visibility = _vm.Checkout.ConfirmedOrder is not null ? Visibility.Visible : Visibility.Collapsed;
    }
    private void Address_Changed(object sender, SelectionChangedEventArgs e) { if (_binding) return; _addressId = Addresses.SelectedValue as int?; Render(); }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await _vm.ReviewAsync();
    private async void Place_Click(object sender, RoutedEventArgs e)
    {
        var id = await _vm.PlaceAsync(_addressId);
        if (IsLoaded && id is int saved) App.MainWindow.Navigate(typeof(ConfirmationPage), saved);
    }
    private void Profile_Click(object sender, RoutedEventArgs e) => App.MainWindow.Navigate(typeof(ProfilePage));
    private void Basket_Click(object sender, RoutedEventArgs e) => App.MainWindow.Navigate(typeof(BasketPage));
    private void History_Click(object sender, RoutedEventArgs e) => App.MainWindow.Navigate(typeof(OrdersPage));
    private void Saved_Click(object sender, RoutedEventArgs e) { if (_vm.Checkout.ConfirmedOrder is int id) App.MainWindow.Navigate(typeof(ConfirmationPage), id); }
    /// <summary>Native address picker label with a stable owned identifier.</summary>
    private sealed record AddressOption(int Id, string Text);
}