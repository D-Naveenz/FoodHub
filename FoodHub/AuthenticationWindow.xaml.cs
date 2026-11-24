using FoodHub.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.ComponentModel;
using Windows.Graphics;
using Windows.System;

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
        ViewModel = new(App.Services, registration);
        InitializeComponent();
        App.Services.Appearance.Apply(this);
        ViewModel.ReadPasswords = () => (Password.Password, Confirmation.Password);
        ViewModel.ClearPasswords = () => { Password.Password = ""; Confirmation.Password = ""; };
        ViewModel.Authenticated += (_, _) => { Succeeded = true; Close(); };
        ViewModel.PropertyChanged += FormChanged;
        Closed += (_, _) => { ViewModel.PropertyChanged -= FormChanged; ViewModel.Dispose(); };
        UpdateForm();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        NativeWindows.SetOwner(hwnd, WinRT.Interop.WindowNative.GetWindowHandle(owner));
        var area = DisplayArea.GetFromWindowId(owner.AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var scale = NativeWindows.GetDpiForWindow(hwnd) / 96.0;
        var width = Math.Min((int)(900 * scale), area.Width);
        var height = Math.Min((int)(680 * scale), area.Height);
        var position = owner.AppWindow.Position; var size = owner.AppWindow.Size;
        AppWindow.MoveAndResize(new RectInt32(Math.Clamp(position.X + (size.Width - width) / 2, area.X, area.X + area.Width - width),
            Math.Clamp(position.Y + (size.Height - height) / 2, area.Y, area.Y + area.Height - height), width, height));
        if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.IsMaximizable = false;
        Root.Loaded += (_, _) => EmailBox.Focus(FocusState.Programmatic);
    }

    private void FormChanged(object? sender, PropertyChangedEventArgs e) => UpdateForm();

    private void UpdateForm()
    {
        var registration = ViewModel.IsRegistration;
        RegistrationFields.Visibility = Confirmation.Visibility = PasswordHint.Visibility = registration ? Visibility.Visible : Visibility.Collapsed;
        Heading.Text = registration ? "Create your account" : "Welcome back";
        SubmitButton.Content = registration ? "Register" : "Log in";
        SwitchButton.Content = registration ? "Already registered? Log in" : "Create an account";
        Feedback.IsOpen = !string.IsNullOrEmpty(ViewModel.Message);
        BusyIndicator.Visibility = ViewModel.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        foreach (var field in RegistrationFields.Children.OfType<Control>()) field.IsEnabled = !ViewModel.IsBusy;
        EmailBox.IsEnabled = Password.IsEnabled = Confirmation.IsEnabled = RememberBox.IsEnabled = !ViewModel.IsBusy;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    private void Password_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && !ViewModel.IsRegistration && ViewModel.SubmitCommand.CanExecute(null))
        { ViewModel.SubmitCommand.Execute(null); e.Handled = true; }
    }
}
