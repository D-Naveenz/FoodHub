using FoodHub.Core.Hosting;
using FoodHub.Core.Services;
using System.IO;
using FoodHub.Pages;
using FoodHub.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using System.ComponentModel;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.
namespace FoodHub;

/// <summary>Customer dashboard shell and the owner of the single modal account window.</summary>
public sealed partial class MainWindow : Window
{
    private bool _syncingSelection;
    private bool _closing;
    private readonly long _paneOpenChangedToken;
    private Type? _pendingDestination;
    private WeakReference<Control>? _returnFocus;
    /// <summary>Shared observable account state.</summary>
    public SessionState Session { get; }
    /// <summary>The one live account window, or null.</summary>
    public AuthenticationWindow? AuthenticationWindow { get; private set; }

    /// <summary>Initializes the title bar, guest dashboard, and account-state synchronization.</summary>
    public MainWindow(SessionState session, AppearanceSettings appearance, ApplicationPaths paths)
    {
        Session = session;
        InitializeComponent();
        // Observe the actual pane state, which changes before its animation completes.
        // This covers both the title-bar toggle and automatic layout changes on resize.
        _paneOpenChangedToken = NavView.RegisterPropertyChangedCallback(NavigationView.IsPaneOpenProperty,
            (_, _) => UpdatePane());
        UpdatePane();
        ExtendsContentIntoTitleBar = true; SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon(Path.Combine(paths.InstallDirectory, "Assets", "AppIcon.ico"));
        Session.PropertyChanged += SessionChanged;
        Closed += (_, _) =>
        {
            _closing = true; Session.PropertyChanged -= SessionChanged;
            NavView.UnregisterPropertyChangedCallback(NavigationView.IsPaneOpenProperty, _paneOpenChangedToken);
            AuthenticationWindow?.Close();
        };
        Navigate(typeof(HomePage));
        appearance.Apply(this);
        _ = UpdateAvatarAsync();
    }

    /// <summary>Routes all customer navigation through the authentication gate.</summary>
    public void Navigate(Type page)
    {
        if (RequiresAccount(page) && !Session.IsAuthenticated)
        {
            _pendingDestination = page;
            Navigate(typeof(ProfilePage)); OpenAuthentication(false); return;
        }
        if (NavFrame.CurrentSourcePageType != page) NavFrame.Navigate(page);
    }

    /// <summary>Opens or activates the account window and disables native input on its owner.</summary>
    public void OpenAuthentication(bool registration)
    {
        if (AuthenticationWindow is { } existing) { existing.Activate(); return; }
        var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(Content.XamlRoot) as Control;
        if (focused is not null) _returnFocus = new(focused);
        var ownerHwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        try
        {
            var window = new AuthenticationWindow(this, registration);
            AuthenticationWindow = window;
            window.Closed += (_, _) =>
            {
                AuthenticationWindow = null;
                NativeWindows.EnableWindow(ownerHwnd, true);
                if (_closing) return;
                Activate();
                if (window.Succeeded)
                {
                    var destination = _pendingDestination ?? typeof(ProfilePage);
                    _pendingDestination = null; Navigate(destination);
                }
                else _pendingDestination = null;
                if (_returnFocus?.TryGetTarget(out var control) == true && control.IsLoaded) control.Focus(FocusState.Programmatic);
                _returnFocus = null;
            };
            NativeWindows.EnableWindow(ownerHwnd, false);
            window.Activate();
        }
        catch
        {
            NativeWindows.EnableWindow(ownerHwnd, true);
            AuthenticationWindow = null;
            throw;
        }
    }

    private static bool RequiresAccount(Type? page) => page == typeof(OrdersPage) || page == typeof(CheckoutPage)
        || page == typeof(ConfirmationPage) || page == typeof(OrderDetailsPage);

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args) => NavView.IsPaneOpen = !NavView.IsPaneOpen;
    private void TitleBar_BackRequested(TitleBar sender, object args)
    {
        if (NavFrame.CanGoBack) NavFrame.GoBack();
    }
    private void Profile_Click(object sender, RoutedEventArgs e) => Navigate(typeof(ProfilePage));
    /// <summary>Shows the bound profile name immediately on expansion, and hides it in the compact pane.</summary>
    private void UpdatePane()
    {
        if (ProfileName is not null) ProfileName.Visibility = NavView.IsPaneOpen ? Visibility.Visible : Visibility.Collapsed;
    }
    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_syncingSelection) return;
        if (args.IsSettingsSelected) Navigate(typeof(SettingsPage));
        else if (args.SelectedItem is NavigationViewItem item)
            Navigate(item.Tag?.ToString() switch
            {
                "home" => typeof(HomePage),
                "menu" => typeof(MenuPage),
                "basket" => typeof(BasketPage),
                "orders" => typeof(OrdersPage),
                "about" => typeof(AboutPage),
                _ => typeof(HomePage)
            });
    }

    private void Frame_Navigated(object sender, NavigationEventArgs e)
    {
        SynchronizeSelection();
        // NavigationView finishes its own selection event after a guest route is redirected.
        // Reapply the actual page selection on the next UI turn so Orders cannot remain highlighted on Profile.
        DispatcherQueue.TryEnqueue(SynchronizeSelection);
    }

    private void SynchronizeSelection()
    {
        var page = NavFrame.CurrentSourcePageType;
        _syncingSelection = true;
        try
        {
            var tag = page == typeof(HomePage) ? "home" : page == typeof(MenuPage) || page == typeof(ItemDetailsPage) ? "menu"
                : page == typeof(BasketPage) || page == typeof(CheckoutPage) ? "basket"
                : RequiresAccount(page) ? "orders" : page == typeof(AboutPage) ? "about" : null;
            NavView.SelectedItem = page == typeof(SettingsPage) ? NavView.SettingsItem
                : NavView.MenuItems.Concat(NavView.FooterMenuItems).OfType<NavigationViewItem>().FirstOrDefault(i => (string?)i.Tag == tag);
        }
        finally { _syncingSelection = false; }
    }

    private async void SessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        SessionFeedback.IsOpen = !string.IsNullOrEmpty(Session.Message);
        RetryButton.Visibility = Session.CanRetry ? Visibility.Visible : Visibility.Collapsed;
        if (e.PropertyName != nameof(Session.Profile)) return;
        if (!Session.IsAuthenticated)
        {
            NavFrame.BackStack.Clear(); NavFrame.ForwardStack.Clear();
            if (RequiresAccount(NavFrame.CurrentSourcePageType)) Navigate(typeof(ProfilePage));
            // Navigating also creates a history entry; remove the protected page it just replaced.
            NavFrame.BackStack.Clear(); NavFrame.ForwardStack.Clear();
        }
        await UpdateAvatarAsync();
    }

    private async Task UpdateAvatarAsync()
    {
        var profile = Session.Profile;
        var picture = profile is null ? new BitmapImage(new Uri("ms-appx:///Assets/UI/user.png"))
            : await ProfileImages.FromBytesAsync(profile.Photo);
        if (ReferenceEquals(profile, Session.Profile)) Avatar.ProfilePicture = picture;
    }

    private async void Retry_Click(object sender, RoutedEventArgs e)
    {
        RetryButton.IsEnabled = false;
        try { await Session.RefreshAsync(); }
        finally { RetryButton.IsEnabled = true; }
    }
}