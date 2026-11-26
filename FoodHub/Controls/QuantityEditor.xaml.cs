using CommunityToolkit.WinUI;
using FoodHub.Core.Services;
using FoodHub.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.ComponentModel;

namespace FoodHub.Controls;

/// <summary>A reusable staged quantity editor. Only Confirm mutates the shared basket.</summary>
public sealed partial class QuantityEditor : UserControl
{
    private readonly BasketState _basket = App.GetService<BasketState>();
    private readonly CheckoutCoordinator _checkout = App.GetService<CheckoutCoordinator>();
    private TransitionHelper? _transition;
    private bool _editing;
    private bool _animating;
    /// <summary>Meal whose confirmed quantity is edited.</summary>
    public MealDetails? Meal { get; private set; }
    /// <summary>Notifies details views when the draft line total should update.</summary>
    public event EventHandler? DraftChanged;
    /// <summary>Current staged quantity; zero is a valid removal request.</summary>
    public int DraftQuantity => double.IsNaN(QuantityBox.Value) ? 0 : (int)Math.Clamp(Math.Round(QuantityBox.Value), 0, short.MaxValue);
    /// <summary>Creates the editor and subscribes only while on screen.</summary>
    public QuantityEditor()
    {
        InitializeComponent();
        Loaded += (_, _) => { _basket.PropertyChanged += BasketChanged; _checkout.PropertyChanged += BasketChanged; Update(); };
        Unloaded += (_, _) => { _basket.PropertyChanged -= BasketChanged; _checkout.PropertyChanged -= BasketChanged; _transition?.Stop(); };
    }
    /// <summary>Rebinds a recycled view without retaining an old draft.</summary>
    public void Bind(MealDetails meal)
    {
        _transition?.Stop(); _transition = null; Meal = meal; _editing = false;
        Compact.Visibility = Visibility.Visible; Expanded.Visibility = Visibility.Collapsed;
        QuantityBox.Value = _basket.Quantity(meal.Id); Update();
    }
    private void BasketChanged(object? sender, PropertyChangedEventArgs e) => Update();
    private void Update()
    {
        IsEnabled = !_checkout.IsPlacing;
        AddButton.IsEnabled = Meal?.Available == true;
        AddButton.Content = Meal is not null && _basket.Quantity(Meal.Id) > 0 ? "Edit quantity" : "Add to cart";
        if (!_editing && Meal is not null) QuantityBox.Value = _basket.Quantity(Meal.Id);
    }
    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (Meal is null || _animating) return;
        _editing = true; QuantityBox.Value = _basket.Quantity(Meal.Id);
        await SetExpandedAsync(true);
        QuantityBox.Focus(FocusState.Programmatic);
    }
    private async void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (Meal is null || _animating || _checkout.IsPlacing) return;
        _basket.Set(Meal, DraftQuantity); _editing = false;
        await SetExpandedAsync(false); Update(); AddButton.Focus(FocusState.Programmatic);
    }
    private async void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_animating) return;
        _editing = false; await SetExpandedAsync(false); Update(); AddButton.Focus(FocusState.Programmatic);
    }
    private void QuantityChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) => DraftChanged?.Invoke(this, EventArgs.Empty);
    private void Minus_Click(object sender, RoutedEventArgs e) => QuantityBox.Value = Math.Max(0, DraftQuantity - 1);
    private void Plus_Click(object sender, RoutedEventArgs e) => QuantityBox.Value = Math.Min(short.MaxValue, DraftQuantity + 1);
    /// <summary>Morphs between the compact action and staged editor; state is still correct if motion fails.</summary>
    private async Task SetExpandedAsync(bool expanded)
    {
        _animating = true;
        try
        {
            if (MenuImages.AnimationsEnabled && IsLoaded)
            {
                _transition ??= new TransitionHelper { Source = Compact, Target = Expanded };
                if (expanded) await _transition.StartAsync(); else await _transition.ReverseAsync();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException or OperationCanceledException)
        { /* Decorative animation cannot prevent quantity editing. */ }
        finally
        {
            Compact.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
            Expanded.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            _animating = false;
        }
    }
}
