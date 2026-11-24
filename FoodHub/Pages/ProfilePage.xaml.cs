using FoodHub.Services;
using FoodHub.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System.ComponentModel;

namespace FoodHub.Pages;

/// <summary>Shows the combined account/customer profile or guest entry actions.</summary>
public sealed partial class ProfilePage : Page
{
    /// <summary>Page-scoped editable state and commands.</summary>
    public ProfileViewModel ViewModel { get; } = new(App.Services);
    /// <summary>Creates the page and subscribes only while it is visible.</summary>
    public ProfilePage()
    {
        InitializeComponent();
        Loaded += PageLoaded;
        Unloaded += (_, _) =>
        {
            App.Services.Session.PropertyChanged -= SessionChanged;
            ViewModel.PropertyChanged -= FormChanged;
            ViewModel.Dispose();
        };
    }

    private async void PageLoaded(object sender, RoutedEventArgs e)
    {
        App.Services.Session.PropertyChanged += SessionChanged;
        ViewModel.PropertyChanged += FormChanged;
        await UpdateStateAsync();
        await ViewModel.RefreshCommand.ExecuteAsync(null);
    }

    private async void SessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SessionState.Profile) or nameof(SessionState.CanSignOut)) await UpdateStateAsync();
    }

    private void FormChanged(object? sender, PropertyChangedEventArgs e)
    {
        Feedback.IsOpen = !string.IsNullOrEmpty(ViewModel.Message);
        foreach (var field in Editor.Children.Concat(AddressEditor.Children).OfType<Control>()) field.IsEnabled = !ViewModel.IsBusy;
        AddressPicker.IsEnabled = !ViewModel.IsBusy;
    }

    private async Task UpdateStateAsync()
    {
        var session = App.Services.Session; var profile = session.Profile;
        AccountPanel.Visibility = session.IsAuthenticated ? Visibility.Visible : Visibility.Collapsed;
        GuestPanel.Visibility = session.IsAuthenticated ? Visibility.Collapsed : Visibility.Visible;
        SignOutButton.Visibility = session.CanSignOut ? Visibility.Visible : Visibility.Collapsed;
        ProfileTitle.Text = session.DisplayName; ProfileAvatar.DisplayName = session.DisplayName;
        var photo = profile is null ? new BitmapImage(new Uri("ms-appx:///Assets/UI/user.png")) : await ProfileImages.FromBytesAsync(profile.Photo);
        if (IsLoaded && ReferenceEquals(profile, session.Profile)) ProfileAvatar.ProfilePicture = photo;
    }

    private void Login_Click(object sender, RoutedEventArgs e) => App.MainWindow.OpenAuthentication(false);
    private void Register_Click(object sender, RoutedEventArgs e) => App.MainWindow.OpenAuthentication(true);
}
