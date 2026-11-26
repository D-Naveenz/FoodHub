using FoodHub.Controls;
using FoodHub.Core.Services;
using FoodHub.Services;
using FoodHub.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.ComponentModel;

namespace FoodHub.Pages;

/// <summary>Itemized basket presentation with shared quantity editors and current preview totals.</summary>
public sealed partial class BasketPage : Page
{
    private readonly JourneyViewModel _vm = App.GetService<ViewModelFactory>().CreateJourney();
    /// <summary>Subscribes to basket changes only while the page is visible.</summary>
    public BasketPage()
    {
        InitializeComponent(); Loaded += PageLoaded;
        Unloaded += (_, _) => { _vm.Basket.PropertyChanged -= Changed; _vm.PropertyChanged -= Changed; _vm.Checkout.PropertyChanged -= Changed; _vm.Dispose(); };
    }
    private async void PageLoaded(object sender, RoutedEventArgs e)
    {
        _vm.Basket.PropertyChanged += Changed; _vm.PropertyChanged += Changed; _vm.Checkout.PropertyChanged += Changed;
        Render(); await _vm.RefreshBasketAsync();
    }
    private void Changed(object? sender, PropertyChangedEventArgs e) { if (IsLoaded) Render(); }
    private void Render()
    {
        BusyProgress.Visibility = _vm.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        Feedback.Message = _vm.Checkout.Uncertain ? "An order may already have been saved. Check refreshed order history before placing another." : _vm.Message;
        Feedback.IsOpen = Feedback.Message.Length > 0;
        Lines.Children.Clear();
        foreach (var line in _vm.Basket.Lines)
        {
            var content = new StackPanel { Spacing = 10 };
            content.Children.Add(ViewTools.Text(line.Meal.Name, 20));
            content.Children.Add(ViewTools.Text($"{line.Quantity} × {MenuImages.Money(line.Meal.Price, _vm.Basket.Prices?.Currency)} = {MenuImages.Money(line.Total, _vm.Basket.Prices?.Currency)}"));
            if (!line.Meal.Available) content.Children.Add(ViewTools.Text("Currently unavailable. Remove this item to continue."));
            var editor = new QuantityEditor(); editor.Bind(line.Meal); content.Children.Add(editor);
            var remove = new Button { Content = "Remove", IsEnabled = !_vm.Checkout.IsPlacing };
            remove.Click += (_, _) => _vm.Basket.Set(line.Meal, 0); content.Children.Add(remove);
            Lines.Children.Add(ViewTools.Card(content));
        }
        if (_vm.Basket.Count == 0) Lines.Children.Add(ViewTools.Text("Your basket is empty. Browse the menu to choose a meal.", 18));
        var b = _vm.Basket;
        Totals.Text = b.Prices is null ? "Prices unavailable. Refresh to load store pricing." : $"Subtotal: {MenuImages.Money(b.Subtotal, b.Prices.Currency)}\nDelivery: {MenuImages.Money(b.DeliveryFee, b.Prices.Currency)}\nTotal: {MenuImages.Money(b.Total, b.Prices.Currency)}";
        CheckoutButton.IsEnabled = b.Count > 0 && b.Prices is not null && !_vm.IsBusy && !_vm.Checkout.IsPlacing;
        ClearButton.IsEnabled = b.Count > 0 && !_vm.Checkout.IsPlacing;
        SavedButton.Visibility = _vm.Checkout.ConfirmedOrder is not null ? Visibility.Visible : Visibility.Collapsed;
    }
    private void Menu_Click(object sender, RoutedEventArgs e) => App.MainWindow.Navigate(typeof(MenuPage));
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await _vm.RefreshBasketAsync();
    private async void Clear_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Clear your basket?", PrimaryButtonText = "Clear", CloseButtonText = "Keep items", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !_vm.Checkout.IsPlacing) _vm.Basket.Clear();
    }
    private void Checkout_Click(object sender, RoutedEventArgs e) => App.MainWindow.Navigate(typeof(CheckoutPage));
    private void Saved_Click(object sender, RoutedEventArgs e) { if (_vm.Checkout.ConfirmedOrder is int id) App.MainWindow.Navigate(typeof(ConfirmationPage), id); }
}