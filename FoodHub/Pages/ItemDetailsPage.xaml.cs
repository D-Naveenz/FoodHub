using FoodHub.Core.Services;
using FoodHub.Services;
using FoodHub.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using System.ComponentModel;

namespace FoodHub.Pages;

/// <summary>Shows a meal and the customer's current purchase, with connected image navigation.</summary>
public sealed partial class ItemDetailsPage : Page
{
    private readonly JourneyViewModel _vm = App.GetService<ViewModelFactory>().CreateJourney();
    private MealDetails? _meal;
    /// <summary>Creates the detail view and its page-owned read lifetime.</summary>
    public ItemDetailsPage()
    {
        InitializeComponent(); MenuImages.AttachFallback(Photo);
        Editor.DraftChanged += (_, _) => UpdatePurchase();
        Loaded += PageLoaded;
        Unloaded += (_, _) => { _vm.PropertyChanged -= Changed; _vm.Basket.PropertyChanged -= BasketChanged; _vm.Dispose(); };
    }
    /// <summary>Uses a public navigation snapshot immediately so the animation does not wait for SQL.</summary>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is MealDetails meal) Bind(meal);
    }
    /// <summary>Prepares the reverse image connection before the shell navigates away.</summary>
    protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        if (e.NavigationMode == NavigationMode.Back && MenuImages.AnimationsEnabled)
            ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("MealBack", Photo);
        base.OnNavigatingFrom(e);
    }
    private async void PageLoaded(object sender, RoutedEventArgs e)
    {
        _vm.PropertyChanged += Changed; _vm.Basket.PropertyChanged += BasketChanged;
        ConnectedAnimationService.GetForCurrentView().GetAnimation("MealForward")?.TryStart(Photo);
        if (_meal is null) return;
        await _vm.MealAsync(_meal.Id);
        if (IsLoaded && _vm.Meal is not null) Bind(_vm.Meal);
        else if (IsLoaded) Editor.IsEnabled = false;
    }
    private void Bind(MealDetails meal)
    {
        _meal = meal; NameText.Text = meal.Name; Description.Text = meal.Description ?? "";
        Price.Text = MenuImages.Money(meal.Price, _vm.Basket.Prices?.Currency);
        Availability.Text = meal.Available ? "Available" : "Currently unavailable";
        MenuImages.Set(Photo, meal); Editor.Bind(meal); UpdatePurchase();
    }
    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (!IsLoaded) return;
        BusyProgress.Visibility = _vm.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        Editor.IsEnabled = !_vm.IsBusy;
        Feedback.Message = _vm.Message; Feedback.IsOpen = _vm.Message.Length > 0;
    }
    private void BasketChanged(object? sender, PropertyChangedEventArgs e) => UpdatePurchase();
    private void UpdatePurchase()
    {
        if (_meal is null) return;
        Purchase.Text = $"Selected: {Editor.DraftQuantity} · Line total: {MenuImages.Money(_meal.Price * Editor.DraftQuantity, _vm.Basket.Prices?.Currency)} · In basket: {_vm.Basket.Quantity(_meal.Id)}";
    }
    private void Back_Click(object sender, RoutedEventArgs e) => App.MainWindow.GoBack();
    private void Basket_Click(object sender, RoutedEventArgs e) => App.MainWindow.Navigate(typeof(BasketPage));
}