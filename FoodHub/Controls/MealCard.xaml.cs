using FoodHub.Core.Services;
using FoodHub.Pages;
using FoodHub.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System.ComponentModel;

namespace FoodHub.Controls;

/// <summary>Photo-led menu card with isolated navigation and purchase actions.</summary>
public sealed partial class MealCard : UserControl
{
    private readonly BasketState _basket = App.GetService<BasketState>();
    private MealDetails? _meal;
    /// <summary>The purchase editor coordinated by the menu page while this card is realized.</summary>
    public QuantityEditor QuantityEditor => Editor;
    /// <summary>Current meal identity, used to preserve navigation without retaining entities.</summary>
    public int? MealId => _meal?.Id;
    /// <summary>Assigns the current projection item explicitly when the repeater prepares a recycled card.</summary>
    public void BindMeal(MealDetails meal)
    {
        if (ReferenceEquals(DataContext, meal)) Bind();
        else DataContext = meal;
    }
    /// <summary>Creates the reusable card and observes its confirmed basket quantity while visible.</summary>
    public MealCard()
    {
        InitializeComponent(); MenuImages.AttachFallback(Photo);
        DataContextChanged += (_, _) => Bind();
        Loaded += (_, _) => { _basket.PropertyChanged += BasketChanged; Bind(); TryReturnAnimation(); };
        Unloaded += (_, _) => _basket.PropertyChanged -= BasketChanged;
    }
    private void Bind()
    {
        if (DataContext is not MealDetails meal) return;
        _meal = meal; MealName.Text = meal.Name; Price.Text = MenuImages.Money(meal.Price, _basket.Prices?.Currency);
        Availability.Text = meal.Available ? "Available" : "Currently unavailable";
        AutomationProperties.SetName(BodyButton, $"View {meal.Name}, {Price.Text}, {Availability.Text}");
        MenuImages.Set(Photo, meal); Editor.Bind(meal); UpdateQuantity();
    }
    private void BasketChanged(object? sender, PropertyChangedEventArgs e) => UpdateQuantity();
    private void UpdateQuantity()
    {
        if (_meal is null) return;
        var quantity = _basket.Quantity(_meal.Id);
        BadgeText.Text = quantity.ToString(); QuantityBadge.Visibility = quantity > 0 ? Visibility.Visible : Visibility.Collapsed;
        CardBorder.BorderBrush = (Brush)Application.Current.Resources[quantity > 0 ? "AccentFillColorDefaultBrush" : "CardStrokeColorDefaultBrush"];
        // Retain the neutral surface so the highlighted price stays readable in both themes.
        // The accent outline and explicit quantity badge carry the selected state without relying on a tinted fill.
        CardBorder.Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
    }
    private void Card_Entered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) => CardBorder.Opacity = 0.92;
    private void Card_Exited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) => CardBorder.Opacity = 1;
    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (_meal is null) return;
        App.GetService<MenuBrowseState>().ReturningMeal = _meal.Id;
        if (MenuImages.AnimationsEnabled) ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("MealForward", Photo);
        App.MainWindow.Navigate(typeof(ItemDetailsPage), _meal);
    }
    /// <summary>Starts a pending reverse animation only for the realized source meal.</summary>
    private void TryReturnAnimation()
    {
        if (_meal?.Id != App.GetService<MenuBrowseState>().ReturningMeal) return;
        var animation = ConnectedAnimationService.GetForCurrentView().GetAnimation("MealBack");
        if (animation is not null) { animation.TryStart(Photo); App.GetService<MenuBrowseState>().ReturningMeal = null; }
    }
}
