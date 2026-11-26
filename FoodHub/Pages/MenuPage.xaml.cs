using CommunityToolkit.WinUI.Controls;
using FoodHub.Core.Services;
using FoodHub.Services;
using FoodHub.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.ComponentModel;

namespace FoodHub.Pages;

/// <summary>Public catalogue presentation; the view model owns data loading and filtering.</summary>
public sealed partial class MenuPage : Page
{
    private readonly MenuViewModel _viewModel = App.GetService<ViewModelFactory>().CreateMenu();
    private readonly BasketState _basket = App.GetService<BasketState>();
    private bool _binding;
    private ScrollView? _scroll;
    /// <summary>Restores public browsing preferences and starts a cancellable catalogue read.</summary>
    public MenuPage()
    {
        InitializeComponent();
        Loaded += PageLoaded;
        Unloaded += (_, _) =>
        {
            if (_scroll is not null) _viewModel.Browse.Offset = _scroll.VerticalOffset;
            _basket.PropertyChanged -= BasketChanged; _viewModel.PropertyChanged -= Changed; _viewModel.Dispose();
        };
    }
    private async void PageLoaded(object sender, RoutedEventArgs e)
    {
        _binding = true;
        Search.Text = _viewModel.Browse.Search; Sort.SelectedIndex = _viewModel.Browse.Sort; AvailableOnly.IsChecked = _viewModel.Browse.AvailableOnly;
        _binding = false;
        _basket.PropertyChanged += BasketChanged; _viewModel.PropertyChanged += Changed;
        BasketChanged(null, new(null));
        await _viewModel.LoadAsync();
        if (!IsLoaded) return;
        BindCategories(); Meals.UpdateLayout();
        _scroll = ViewTools.Find<ScrollView>(Meals);
        _scroll?.ScrollTo(0, _viewModel.Browse.Offset, new ScrollingScrollOptions(ScrollingAnimationMode.Disabled));
    }
    private void BindCategories()
    {
        _binding = true; Categories.Items.Clear();
        var all = new TokenItem { Content = "All" }; Categories.Items.Add(all);
        object selected = all;
        foreach (var category in _viewModel.Categories)
        {
            var token = new TokenItem { Content = category.Name, Tag = category.Id }; Categories.Items.Add(token);
            if (_viewModel.Browse.Category == category.Id) selected = token;
        }
        Categories.SelectedItem = selected; _binding = false;
    }
    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (!IsLoaded) return;
        BusyProgress.Visibility = _viewModel.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        Feedback.Message = _viewModel.Message; Feedback.IsOpen = _viewModel.Message.Length > 0;
        Meals.ItemsSource = _viewModel.Meals;
        Empty.Text = _viewModel.EmptyMessage;
        Empty.Visibility = !_viewModel.IsBusy && _viewModel.Meals.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void BasketChanged(object? sender, PropertyChangedEventArgs e) => BasketButton.Content = $"Basket · {_basket.Count} · {MenuImages.Money(_basket.Total, _basket.Prices?.Currency)}";
    private void Search_Changed(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) { if (_binding) return; _viewModel.Browse.Search = sender.Text; _viewModel.FilterChanged(); }
    private void Sort_Changed(object sender, SelectionChangedEventArgs e) { if (_binding || Meals is null) return; _viewModel.Browse.Sort = Sort.SelectedIndex; _viewModel.FilterChanged(); }
    private void Category_Changed(object sender, SelectionChangedEventArgs e) { if (_binding) return; _viewModel.Browse.Category = (Categories.SelectedItem as TokenItem)?.Tag as int?; _viewModel.FilterChanged(); }
    private void Availability_Click(object sender, RoutedEventArgs e) { _viewModel.Browse.AvailableOnly = AvailableOnly.IsChecked == true; _viewModel.FilterChanged(); }
    private async void Refresh_Click(object sender, RoutedEventArgs e) { await _viewModel.LoadAsync(); if (IsLoaded) BindCategories(); }
    private void Basket_Click(object sender, RoutedEventArgs e) => App.MainWindow.Navigate(typeof(BasketPage));
}
