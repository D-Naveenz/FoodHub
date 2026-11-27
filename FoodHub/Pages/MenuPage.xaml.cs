using CommunityToolkit.WinUI.Controls;
using FoodHub.Controls;
using FoodHub.Core.Services;
using FoodHub.Services;
using FoodHub.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using System.ComponentModel;
using System.Collections.ObjectModel;
using Windows.UI.ViewManagement;

namespace FoodHub.Pages;

/// <summary>Compact public catalogue with floating filters and independently sized, virtualized meal cards.</summary>
public sealed partial class MenuPage : Page
{
    private readonly MenuViewModel _viewModel = App.GetService<ViewModelFactory>().CreateMenu();
    private readonly BasketState _basket = App.GetService<BasketState>();
    private readonly HashSet<MealCard> _realized = [];
    private readonly SemaphoreSlim _editGate = new(1, 1);
    private readonly UISettings _uiSettings = new();
    private IReadOnlyList<MealDetails> _shown = [];
    private readonly ObservableCollection<MealDetails> _items = [];
    private MealCard? _expanded;
    private bool _binding;
    private bool _leaving;
    /// <summary>Creates the page while retaining only lightweight browsing preferences across navigation.</summary>
    public MenuPage()
    {
        InitializeComponent();
        Meals.ItemsSource = _items;
        Loaded += PageLoaded;
        Unloaded += (_, _) =>
        {
            _leaving = true;
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) _uiSettings.AnimationsEnabledChanged -= MotionPreferenceChanged;
            _viewModel.Browse.Offset = MenuScroll.VerticalOffset;
            foreach (var card in _realized.ToArray()) Detach(card);
            _expanded = null;
            _basket.PropertyChanged -= BasketChanged; _viewModel.PropertyChanged -= Changed; _viewModel.Dispose();
        };
    }
    private async void PageLoaded(object sender, RoutedEventArgs e)
    {
        _binding = true;
        Search.Text = _viewModel.Browse.Search; AvailableOnly.IsChecked = _viewModel.Browse.AvailableOnly;
        UpdateSortChecks(); _binding = false;
        _basket.PropertyChanged += BasketChanged; _viewModel.PropertyChanged += Changed;
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) _uiSettings.AnimationsEnabledChanged += MotionPreferenceChanged;
        BasketChanged(null, new(null));
        await _viewModel.LoadAsync();
        if (!IsLoaded) return;
        BindCategories(); MenuScroll.UpdateLayout();
        MenuScroll.ChangeView(null, _viewModel.Browse.Offset, null, true);
    }
    private void BindCategories()
    {
        _binding = true; Categories.Items.Clear();
        // Chips need an opaque surface because pale food photos can pass directly behind them.
        var chipBackground = (Brush)Application.Current.Resources["SolidBackgroundFillColorSecondaryBrush"];
        var all = new TokenItem { Content = "All", Background = chipBackground }; Categories.Items.Add(all);
        object selected = all;
        foreach (var category in _viewModel.Categories)
        {
            var token = new TokenItem { Content = category.Name, Tag = category.Id, Background = chipBackground }; Categories.Items.Add(token);
            if (_viewModel.Browse.Category == category.Id) selected = token;
        }
        Categories.SelectedItem = selected; _binding = false;
    }
    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (!IsLoaded) return;
        BusyProgress.Visibility = _viewModel.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        Feedback.Message = _viewModel.Message; Feedback.IsOpen = _viewModel.Message.Length > 0;
        var meals = _viewModel.Meals;
        // Busy-state and feedback changes must not recycle cards or discard a quantity draft.
        if (!_shown.SequenceEqual(meals))
        {
            foreach (var card in _realized.ToArray()) card.QuantityEditor.ResetPresentation();
            _expanded = null; _shown = meals;
            _items.Clear();
            foreach (var meal in meals) _items.Add(meal);
            // Recreate the layout context when the projection changes. The Toolkit caches measured
            // elements by index; a new context prevents a filtered/reordered index from keeping an old card.
            Meals.Layout = new StaggeredLayout
            {
                DesiredColumnWidth = 260, ColumnSpacing = 12, RowSpacing = 12,
                ItemsStretch = StaggeredLayoutItemsStretch.Fill
            };
        }
        Empty.Text = _viewModel.EmptyMessage;
        Empty.Visibility = !_viewModel.IsBusy && meals.Count == 0 && Empty.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        FeedbackPanel.Visibility = _viewModel.IsBusy || Feedback.IsOpen || Empty.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
    }
    private void BasketChanged(object? sender, PropertyChangedEventArgs e)
    {
        BasketButton.SetSummary(_basket.Count, MenuImages.Money(_basket.Total, _basket.Prices?.Currency));
        UpdateHeaderLayout();
    }
    private void Header_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateHeaderLayout();
    private void Basket_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateHeaderLayout();
    /// <summary>Moves the basket action below the title when the title and artwork cannot comfortably share a row.</summary>
    private void UpdateHeaderLayout()
    {
        if (MenuHeader is null || BasketButton is null || MenuTitle is null || MenuHeader.ActualWidth <= 0) return;
        var basketWidth = Math.Max(212, BasketButton.ActualWidth);
        var stacked = MenuHeader.ActualWidth < basketWidth + 252;
        Grid.SetRow(BasketButton, stacked ? 1 : 0);
        Grid.SetColumn(BasketButton, stacked ? 0 : 1);
        Grid.SetColumnSpan(BasketButton, stacked ? 2 : 1);
        Grid.SetColumnSpan(MenuTitle, stacked ? 2 : 1);
    }
    private void Search_Changed(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (_binding) return;
        _viewModel.Browse.Search = sender.Text; _viewModel.FilterChanged();
    }
    private void Sort_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleMenuFlyoutItem item || !int.TryParse(item.Tag?.ToString(), out var sort)) return;
        _viewModel.Browse.Sort = sort; UpdateSortChecks(); _viewModel.FilterChanged();
    }
    private void UpdateSortChecks()
    {
        var options = new[] { SortMenuOrder, SortName, SortPriceAscending, SortPriceDescending };
        for (var i = 0; i < options.Length; i++) options[i].IsChecked = i == _viewModel.Browse.Sort;
        var current = options[Math.Clamp(_viewModel.Browse.Sort, 0, options.Length - 1)].Text;
        ToolTipService.SetToolTip(SortButton, $"Sort: {current}");
    }
    private void Category_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_binding) return;
        _viewModel.Browse.Category = (Categories.SelectedItem as TokenItem)?.Tag as int?; _viewModel.FilterChanged();
    }
    private void Availability_Click(object sender, RoutedEventArgs e)
    { _viewModel.Browse.AvailableOnly = AvailableOnly.IsChecked == true; _viewModel.FilterChanged(); }
    private async void Refresh_Click(object sender, RoutedEventArgs e)
    { await _viewModel.LoadAsync(); if (IsLoaded) BindCategories(); }
    private void Basket_Click(object sender, RoutedEventArgs e) => App.MainWindow.Navigate(typeof(BasketPage));
    private void Categories_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // This padding belongs to scroll content, not the viewport: only the chips stay fixed.
        ScrollContent.Padding = new Thickness(0, e.NewSize.Height + Categories.Margin.Top + 12, 0, 20);
    }
    private void Toolbar_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (Search is not null) Search.Width = Math.Clamp(e.NewSize.Width - 140, 100, 280);
    }
    private void Meal_Prepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is not MealCard card) return;
        card.BindMeal(_items[args.Index]);
        if (!_realized.Add(card)) return;
        card.QuantityEditor.PrepareEditAsync = () => PrepareEditAsync(card);
        card.QuantityEditor.EditStateChanged += EditStateChanged;
        card.Loaded += CardLoaded;
    }
    private void CardLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is MealCard card) ConfigureRepositionAnimation(card);
    }
    /// <summary>Applies live Windows motion preferences to layout animations as well as editor transitions.</summary>
    private void MotionPreferenceChanged(UISettings sender, object args) => DispatcherQueue.TryEnqueue(() =>
    {
        if (_leaving) return;
        foreach (var card in _realized)
        {
            if (!sender.AnimationsEnabled) card.QuantityEditor.ResetPresentation();
            ConfigureRepositionAnimation(card);
        }
    });
    private void ConfigureRepositionAnimation(MealCard card)
    {
        // Offset animations affect rendering only; the layout still owns the final natural card bounds.
        var visual = ElementCompositionPreview.GetElementVisual(card);
        visual.ImplicitAnimations = null;
        if (!_uiSettings.AnimationsEnabled) { visual.StopAnimation("Offset"); return; }
        var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
        animation.Target = "Offset"; animation.Duration = TimeSpan.FromMilliseconds(300);
        animation.InsertExpressionKeyFrame(1, "this.FinalValue");
        var collection = visual.Compositor.CreateImplicitAnimationCollection();
        collection["Offset"] = animation; visual.ImplicitAnimations = collection;
    }
    private async Task PrepareEditAsync(MealCard card)
    {
        await _editGate.WaitAsync();
        try
        {
            if (_leaving || !_realized.Contains(card)) return;
            var previous = _expanded; _expanded = card;
            if (previous is not null && previous != card) await previous.QuantityEditor.CancelEditAsync();
        }
        finally { _editGate.Release(); }
    }
    private void EditStateChanged(object? sender, EventArgs e)
    {
        if (_expanded is not null && ReferenceEquals(sender, _expanded.QuantityEditor) && !_expanded.QuantityEditor.IsEditing) _expanded = null;
    }
    private void Meal_Clearing(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs args)
    { if (args.Element is MealCard card) Detach(card); }
    private void Detach(MealCard card)
    {
        card.Loaded -= CardLoaded;
        card.QuantityEditor.PrepareEditAsync = null;
        card.QuantityEditor.EditStateChanged -= EditStateChanged;
        card.QuantityEditor.ResetPresentation();
        var visual = ElementCompositionPreview.GetElementVisual(card);
        visual.ImplicitAnimations = null; visual.StopAnimation("Offset");
        if (_expanded == card) _expanded = null;
        _realized.Remove(card);
    }
}
