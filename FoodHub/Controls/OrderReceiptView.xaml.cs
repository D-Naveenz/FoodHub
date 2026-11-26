using FoodHub.Core.Services;
using FoodHub.Pages;
using FoodHub.Services;
using FoodHub.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.ComponentModel;

namespace FoodHub.Controls;

/// <summary>Shared view of saved receipt snapshots and SQL-backed simulated delivery events.</summary>
public sealed partial class OrderReceiptView : UserControl
{
    private readonly JourneyViewModel _vm = App.GetService<ViewModelFactory>().CreateJourney();
    /// <summary>Untrusted navigation identity; Core validates its owner on every read and write.</summary>
    public int OrderId { get; set; }
    /// <summary>Creates page-owned tracking state and cancels reads when the view unloads.</summary>
    public OrderReceiptView()
    {
        InitializeComponent(); Loaded += ViewLoaded;
        Unloaded += (_, _) => { _vm.PropertyChanged -= Changed; _vm.Dispose(); ContentPanel.Visibility = Visibility.Collapsed; };
    }
    private async void ViewLoaded(object sender, RoutedEventArgs e) { _vm.PropertyChanged += Changed; await _vm.ReceiptAsync(OrderId); }
    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (!IsLoaded) return;
        BusyProgress.Visibility = _vm.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        RefreshButton.IsEnabled = !_vm.IsBusy;
        Feedback.Message = _vm.Message; Feedback.IsOpen = _vm.Message.Length > 0;
        var receipt = _vm.Receipt;
        ContentPanel.Visibility = receipt is null ? Visibility.Collapsed : Visibility.Visible;
        if (receipt is null) return;
        _vm.Checkout.ReceiptShown(receipt.Summary.Id);
        OrderNumber.Text = $"{receipt.Summary.Number} · {Local(receipt.Summary.PlacedUtc)}";
        Status.Text = DeliveryStages.Label(receipt.Summary.Status);
        Timeline.Children.Clear();
        foreach (byte stage in new byte[] { 10, 20, 30, 40 })
        {
            var ev = receipt.Events.FirstOrDefault(e => e.Status == stage);
            Timeline.Children.Add(ViewTools.Text($"{(ev is null ? "○" : "✓")}  {DeliveryStages.Label(stage)} · {(ev is null ? receipt.Summary.Status == 90 ? "Not reached — order cancelled" : "Pending" : Local(ev.AtUtc))}", 18));
        }
        if (receipt.Events.FirstOrDefault(e => e.Status == 90) is { } cancelled) Timeline.Children.Add(ViewTools.Text($"Cancelled · {Local(cancelled.AtUtc)}", 18));
        Contact.Text = $"{receipt.CustomerName} · {receipt.Phone}\n{receipt.Address}";
        Lines.Children.Clear();
        foreach (var line in receipt.Lines) Lines.Children.Add(ViewTools.Text($"{line.Quantity} × {line.Name} · {MenuImages.Money(line.Price, receipt.Summary.Currency)} each · {MenuImages.Money(line.Total, receipt.Summary.Currency)}"));
        Totals.Text = $"Subtotal: {MenuImages.Money(receipt.Subtotal, receipt.Summary.Currency)}\nDelivery: {MenuImages.Money(receipt.DeliveryFee, receipt.Summary.Currency)}\nTotal: {MenuImages.Money(receipt.Summary.Total, receipt.Summary.Currency)}";
        var next = DeliveryStages.Next(receipt.Summary.Status);
        AdvanceButton.Visibility = next is null ? Visibility.Collapsed : Visibility.Visible;
        AdvanceButton.Content = next is byte status ? $"Demo: advance to {DeliveryStages.Label(status)}" : "Demo complete";
        AdvanceButton.IsEnabled = !_vm.IsBusy;
        CancelButton.Visibility = receipt.Summary.Status == 10 ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.IsEnabled = !_vm.IsBusy;
    }
    private static string Local(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime().ToString("g");
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await _vm.ReceiptAsync(OrderId);
    private async void Advance_Click(object sender, RoutedEventArgs e)
    { if (_vm.Receipt is { } receipt && DeliveryStages.Next(receipt.Summary.Status) is byte status) await _vm.ChangeStatusAsync(OrderId, status); }
    private async void Cancel_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Cancel this order?", Content = "Cancellation is available only while the order is still at Order placed.", PrimaryButtonText = "Cancel order", CloseButtonText = "Keep order", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && IsLoaded) await _vm.ChangeStatusAsync(OrderId, 90);
    }
    private void Orders_Click(object sender, RoutedEventArgs e) => App.MainWindow.Navigate(typeof(OrdersPage));
    private void Menu_Click(object sender, RoutedEventArgs e) => App.MainWindow.Navigate(typeof(MenuPage));
}