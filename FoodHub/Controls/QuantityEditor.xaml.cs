using CommunityToolkit.WinUI;
using FoodHub.Core.Services;
using FoodHub.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System.ComponentModel;
using Windows.Foundation;

namespace FoodHub.Controls;

/// <summary>A staged quantity editor whose measured height and matched purchase action animate together.</summary>
public sealed partial class QuantityEditor : UserControl
{
    private readonly BasketState _basket = App.GetService<BasketState>();
    private readonly CheckoutCoordinator _checkout = App.GetService<CheckoutCoordinator>();
    private TransitionHelper? _transition;
    private Storyboard? _heightAnimation;
    private CancellationTokenSource? _motion;
    private int _presentationVersion;
    private bool _editing;
    private bool _animating;
    /// <summary>Meal whose confirmed quantity is edited.</summary>
    public MealDetails? Meal { get; private set; }
    /// <summary>Notifies details views when the draft line total should update.</summary>
    public event EventHandler? DraftChanged;
    /// <summary>Notifies the owning card when an editing session opens or closes.</summary>
    public event EventHandler? EditStateChanged;
    /// <summary>Optional page-owned coordination, awaited before opening a new draft.</summary>
    public Func<Task>? PrepareEditAsync { get; set; }
    /// <summary>Whether this view currently holds an unconfirmed draft.</summary>
    public bool IsEditing => _editing;
    /// <summary>Current staged quantity; zero is a valid removal request.</summary>
    public int DraftQuantity => double.IsNaN(QuantityBox.Value) ? 0 : (int)Math.Clamp(Math.Round(QuantityBox.Value), 0, short.MaxValue);
    /// <summary>Creates the editor and observes confirmed state only while visible.</summary>
    public QuantityEditor()
    {
        InitializeComponent();
        Loaded += (_, _) => { _basket.PropertyChanged += BasketChanged; _checkout.PropertyChanged += BasketChanged; Update(); };
        Unloaded += (_, _) =>
        {
            _basket.PropertyChanged -= BasketChanged; _checkout.PropertyChanged -= BasketChanged;
            ResetPresentation();
        };
    }
    /// <summary>Rebinds a recycled view without retaining its old draft or animation.</summary>
    public void Bind(MealDetails meal)
    {
        ResetPresentation(); Meal = meal;
        QuantityBox.Value = _basket.Quantity(meal.Id); Update();
    }
    /// <summary>Stops motion and discards transient state immediately during recycling or navigation.</summary>
    public void ResetPresentation()
    {
        _presentationVersion++;
        _motion?.Cancel(); _heightAnimation?.Stop(); _transition?.Reset();
        _motion?.Dispose(); _motion = null; _heightAnimation = null; _transition = null;
        var wasEditing = _editing;
        _editing = false; _animating = false;
        ApplyState(false); EditorHost.Height = double.NaN; EditorHost.Clip = null;
        if (wasEditing) EditStateChanged?.Invoke(this, EventArgs.Empty);
    }
    /// <summary>Cancels the draft, optionally reversing the animation without stealing another card's focus.</summary>
    public async Task CancelEditAsync(bool animate = true)
    {
        if (_animating || !animate) { ResetPresentation(); Update(); return; }
        if (!_editing) return;
        _editing = false;
        await SetExpandedAsync(false);
        Update(); EditStateChanged?.Invoke(this, EventArgs.Empty);
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
        if (Meal is null || _animating || _editing) return;
        var version = _presentationVersion;
        if (PrepareEditAsync is not null) await PrepareEditAsync();
        if (!IsLoaded || version != _presentationVersion || Meal is null || _checkout.IsPlacing) return;
        _editing = true; QuantityBox.Value = _basket.Quantity(Meal.Id);
        EditStateChanged?.Invoke(this, EventArgs.Empty);
        await SetExpandedAsync(true);
        if (IsLoaded && _editing) QuantityBox.Focus(FocusState.Programmatic);
    }
    private async void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (Meal is null || _animating || _checkout.IsPlacing) return;
        // Only confirmation mutates the shared basket; page coordination never changes confirmed quantities.
        _basket.Set(Meal, DraftQuantity); _editing = false;
        await SetExpandedAsync(false); Update(); EditStateChanged?.Invoke(this, EventArgs.Empty);
        if (IsLoaded) AddButton.Focus(FocusState.Programmatic);
    }
    private async void Cancel_Click(object sender, RoutedEventArgs e)
    {
        await CancelEditAsync();
        if (IsLoaded) AddButton.Focus(FocusState.Programmatic);
    }
    private void QuantityChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) => DraftChanged?.Invoke(this, EventArgs.Empty);
    private void Minus_Click(object sender, RoutedEventArgs e) => QuantityBox.Value = Math.Max(0, DraftQuantity - 1);
    private void Plus_Click(object sender, RoutedEventArgs e) => QuantityBox.Value = Math.Min(short.MaxValue, DraftQuantity + 1);

    /// <summary>Measures both presentations before morphing, while a bounded height animation reflows the surrounding layout.</summary>
    private async Task SetExpandedAsync(bool expanded)
    {
        if (!IsLoaded || !MenuImages.AnimationsEnabled) { ApplyState(expanded); return; }
        var version = ++_presentationVersion;
        _motion?.Dispose(); _motion = new CancellationTokenSource();
        var token = _motion.Token;
        _animating = true;
        try
        {
            var initialHeight = EditorHost.ActualHeight;
            var width = Math.Max(1, ActualWidth);
            // Freeze the outer extent before revealing the target for measurement. No frame reserves its full height early.
            EditorHost.Height = initialHeight;
            Compact.Visibility = Visibility.Visible; Expanded.Visibility = Visibility.Visible;
            Compact.Measure(new Size(width, double.PositiveInfinity));
            Expanded.Measure(new Size(width, double.PositiveInfinity));
            var targetHeight = expanded ? Expanded.DesiredSize.Height : Compact.DesiredSize.Height;
            Compact.Arrange(new Rect(0, 0, width, Compact.DesiredSize.Height));
            Expanded.Arrange(new Rect(0, 0, width, Expanded.DesiredSize.Height));
            EditorHost.Clip = new RectangleGeometry { Rect = new Rect(0, 0, width, initialHeight) };
            _transition ??= new TransitionHelper
            {
                Source = Compact, Target = Expanded,
                Duration = TimeSpan.FromMilliseconds(300), ReverseDuration = TimeSpan.FromMilliseconds(300),
                IndependentElementShowDuration = TimeSpan.FromMilliseconds(180),
                IndependentElementShowDelay = TimeSpan.FromMilliseconds(70),
                IndependentElementShowInterval = TimeSpan.Zero,
                IndependentElementHideDuration = TimeSpan.FromMilliseconds(120)
            };
            var morph = expanded ? _transition.StartAsync(token, true) : _transition.ReverseAsync(token, true);
            await Task.WhenAll(morph, AnimateHeightAsync(initialHeight, targetHeight, token));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException or OperationCanceledException)
        { /* Decorative motion must not strand the editor or prevent Confirm/Cancel. */ }
        finally
        {
            // A recycled view may already represent another meal; an old continuation must not restore its old state.
            if (version == _presentationVersion)
            {
                _heightAnimation?.Stop(); _heightAnimation = null;
                _transition?.Reset(!expanded);
                ApplyState(expanded); EditorHost.Height = double.NaN; EditorHost.Clip = null;
                _animating = false;
            }
        }
    }
    private async Task AnimateHeightAsync(double from, double to, CancellationToken token)
    {
        var completion = new TaskCompletionSource();
        var storyboard = new Storyboard();
        var animation = new DoubleAnimation
        {
            From = from, To = to, Duration = TimeSpan.FromMilliseconds(300), EnableDependentAnimation = true,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(animation, EditorHost); Storyboard.SetTargetProperty(animation, "Height");
        storyboard.Children.Add(animation);
        storyboard.Completed += (_, _) => completion.TrySetResult();
        _heightAnimation = storyboard;
        using var registration = token.Register(() => completion.TrySetCanceled(token));
        storyboard.Begin();
        await completion.Task;
    }
    private void ApplyState(bool expanded)
    {
        Compact.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
        Expanded.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        Compact.IsHitTestVisible = !expanded; Expanded.IsHitTestVisible = expanded;
    }
    private void Host_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (EditorHost.Clip is RectangleGeometry clip) clip.Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height);
    }
}