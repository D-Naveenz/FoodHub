using FoodHub.Services;
using FoodHub.ViewModels;
using FoodHub.Pages.Account;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using System.ComponentModel;
using Windows.Graphics;

namespace FoodHub;

/// <summary>Separate account window. Its owner manages modality and lifetime.</summary>
public sealed partial class AuthenticationWindow : Window
{
    /// <summary>Account form state and commands.</summary>
    public AuthenticationViewModel ViewModel { get; }
    /// <summary>Indicates whether the owner should resume a protected navigation request.</summary>
    public bool Succeeded { get; private set; }

    /// <summary>Creates the branded login/registration window centered over its owner.</summary>
    public AuthenticationWindow(MainWindow owner, bool registration)
    {
        ViewModel = App.GetService<ViewModelFactory>().CreateAuthentication(registration);
        InitializeComponent();
        App.GetService<AppearanceSettings>().Apply(this);
        ViewModel.Authenticated += (_, _) => { Succeeded = true; Close(); };
        ViewModel.PropertyChanged += FormChanged;
        Closed += (_, _) => { ViewModel.PropertyChanged -= FormChanged; ViewModel.Dispose(); };
        NavigateForm(animate: false);
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        NativeWindows.SetOwner(hwnd, WinRT.Interop.WindowNative.GetWindowHandle(owner));
        var area = DisplayArea.GetFromWindowId(owner.AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var scale = NativeWindows.GetDpiForWindow(hwnd) / 96.0;
        var width = Math.Min((int)(900 * scale), area.Width);
        var height = Math.Min((int)(600 * scale), area.Height);
        var position = owner.AppWindow.Position; var size = owner.AppWindow.Size;
        AppWindow.MoveAndResize(new RectInt32(Math.Clamp(position.X + (size.Width - width) / 2, area.X, area.X + area.Width - width),
            Math.Clamp(position.Y + (size.Height - height) / 2, area.Y, area.Y + area.Height - height), width, height));
        if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.IsMaximizable = false;
    }

    /// <summary>Only a form change navigates the frame; typing and busy/error updates stay within the current page.</summary>
    private void FormChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AuthenticationViewModel.IsRegistration)) NavigateForm(animate: true);
    }

    /// <summary>Slides between sibling forms without retaining password-bearing pages in navigation history.</summary>
    private void NavigateForm(bool animate)
    {
        var registration = ViewModel.IsRegistration;
        var page = registration ? typeof(RegistrationPage) : typeof(LoginPage);
        if (FormFrame.CurrentSourcePageType == page) return;
        NavigationTransitionInfo transition = animate
            ? new SlideNavigationTransitionInfo
            {
                Effect = registration ? SlideNavigationTransitionEffect.FromRight : SlideNavigationTransitionEffect.FromLeft
            }
            : new SuppressNavigationTransitionInfo();
        FormFrame.Navigate(page, ViewModel, transition);
        FormFrame.BackStack.Clear();
        FormFrame.ForwardStack.Clear();
    }
}