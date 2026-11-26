using Dhara.Extensions.WinUI.Storage;
using Dhara.Extensions.WinUI.Hosting;
using Dhara.Extensions.WinUI.Windowing;
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
    private readonly IWindowRegistry _windows;
    private readonly long _paneOpenChangedToken;
    private Type? _pendingDestination;
    private object? _pendingParameter;
    private readonly BasketState _basket;
    private WeakReference<Control>? _returnFocus;
    /// <summary>Shared observable account state.</summary>
    public SessionState Session { get; }
    /// <summary>The one live account window, or null.</summary>
    public AuthenticationWindow? AuthenticationWindow { get; private set; }

    /// <summary>Initializes the title bar, guest dashboard, and account-state synchronization.</summary>
    /// <param name="session">The customer's observable account state.</param>
    /// <param name="appearance">FoodHub's persisted appearance adapter.</param>
    /// <param name="paths">The bundled content paths used for application-specific assets.</param>
    /// <param name="windows">The reusable registry used to track owned windows and suppress navigation during shutdown.</param>
    /// <param name="basket">Shared preview quantities used for the navigation badge.</param>
    /// <param name="checkout">Launch-lifetime placement coordinator; constructing it observes account boundaries before restoration.</param>
    public MainWindow(SessionState session, AppearanceSettings appearance, ApplicationDataPaths paths, IWindowRegistry windows,
        BasketState basket, CheckoutCoordinator checkout)
    {
        _basket = basket;
        Session = session;
        _windows = windows;
        InitializeComponent();
        // Observe the actual pane state, which changes before its animation completes.
        // This covers both the title-bar toggle and automatic layout changes on resize.
        _paneOpenChangedToken = NavView.RegisterPropertyChangedCallback(NavigationView.IsPaneOpenProperty,
            (_, _) => UpdatePane());
        UpdatePane();
        ExtendsContentIntoTitleBar = true; SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon(Path.Combine(paths.ContentRoot, "Assets", "AppIcon.ico"));
        Session.PropertyChanged += SessionChanged;
        basket.PropertyChanged += BasketChanged;
        BasketChanged(null, new(null));
        Closed += (_, _) =>
        {
            // Programmatic Close can be intercepted and delivered again after asynchronous host cleanup.
            if (_closing) return;
            _closing = true; Session.PropertyChanged -= SessionChanged;
            basket.PropertyChanged -= BasketChanged;
            NavView.UnregisterPropertyChangedCallback(NavigationView.IsPaneOpenProperty, _paneOpenChangedToken);
            AuthenticationWindow?.Close();
        };
        Navigate(typeof(HomePage));
        appearance.Apply(this);
        _ = UpdateAvatarAsync();
    }

    /// <summary>Routes all customer navigation through the authentication gate.</summary>
    /// <param name="page">The destination page type.</param>
    /// <param name="parameter">An item snapshot or order identity retained across modal sign-in.</param>
    public void Navigate(Type page, object? parameter = null)
    {
        if (RequiresAccount(page) && !Session.IsAuthenticated)
        {
            _pendingDestination = page;
            _pendingParameter = parameter;
            OpenAuthentication(false);
            DispatcherQueue.TryEnqueue(SynchronizeSelection);
            return;
        }
        if (NavFrame.CurrentSourcePageType != page || parameter is not null) NavFrame.Navigate(page, parameter);
    }

    /// <summary>Opens or activates the account window and disables native input on its owner.</summary>
    public void OpenAuthentication(bool registration)
    {
        if (AuthenticationWindow is { } existing) { existing.Activate(); return; }
        var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(Content.XamlRoot) as Control;
        if (focused is not null) _returnFocus = new(focused);
        try
        {
            var window = new AuthenticationWindow(this, registration);
            AuthenticationWindow = window;
            _windows.Register(window);
            window.Closed += (_, _) =>
            {
                AuthenticationWindow = null;
                this.SetInputEnabled(true);
                if (_closing || _windows.IsShuttingDown) return;
                Activate();
                if (window.Succeeded)
                {
                    var destination = _pendingDestination ?? typeof(ProfilePage);
                    var parameter = _pendingParameter;
                    _pendingDestination = null; _pendingParameter = null; Navigate(destination, parameter);
                }
                else { _pendingDestination = null; _pendingParameter = null; }
                if (_returnFocus?.TryGetTarget(out var control) == true && control.IsLoaded) control.Focus(FocusState.Programmatic);
                _returnFocus = null;
            };
            this.SetInputEnabled(false);
            window.Activate();
        }
        catch
        {
            this.SetInputEnabled(true);
            AuthenticationWindow = null;
            throw;
        }
    }

    private static bool RequiresAccount(Type? page) => page == typeof(OrdersPage) || page == typeof(CheckoutPage)
        || page == typeof(ConfirmationPage) || page == typeof(OrderDetailsPage);

    /// <summary>Returns through the shell so page behavior and private navigation remain coordinated.</summary>
    public void GoBack() { if (NavFrame.CanGoBack) NavFrame.GoBack(); else Navigate(typeof(MenuPage)); }

    /// <summary>Updates the shell's always-accessible basket unit count.</summary>
    private void BasketChanged(object? sender, PropertyChangedEventArgs e)
    {
        BasketBadge.Value = _basket.Count;
        BasketBadge.Visibility = _basket.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

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
