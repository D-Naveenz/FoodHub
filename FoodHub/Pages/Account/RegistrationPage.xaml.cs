using FoodHub.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using System.ComponentModel;
using Windows.System;

namespace FoodHub.Pages.Account;

/// <summary>Owns the registration form's controls while sharing account state with the other form.</summary>
public sealed partial class RegistrationPage : Page
{
    /// <summary>The account-window view model supplied by frame navigation.</summary>
    public AuthenticationViewModel ViewModel { get; private set; } = null!;

    /// <summary>Creates the form; navigation supplies its shared state before it is loaded.</summary>
    public RegistrationPage()
    {
        InitializeComponent();
        Loaded += (_, _) => DisplayNameBox.Focus(FocusState.Programmatic);
    }

    /// <summary>Connects this page's bindings and transient password fields to the shared workflow.</summary>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel = (AuthenticationViewModel)e.Parameter;
        Bindings.Update();
        ViewModel.ReadPasswords = () => (Password.Password, Confirmation.Password);
        ViewModel.ClearPasswords = ClearPasswordFields;
        ViewModel.PropertyChanged += FormChanged;
        UpdateForm();
    }

    /// <summary>Erases passwords and detaches outgoing controls before the next page takes over.</summary>
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ViewModel.PropertyChanged -= FormChanged;
        ClearPasswordFields();
        ViewModel.ReadPasswords = static () => ("", "");
        ViewModel.ClearPasswords = static () => { };
        base.OnNavigatedFrom(e);
    }

    private void FormChanged(object? sender, PropertyChangedEventArgs e) => UpdateForm();

    /// <summary>Keeps validation/loading feedback local to the form and blocks edits during submission.</summary>
    private void UpdateForm()
    {
        Feedback.IsOpen = !string.IsNullOrEmpty(ViewModel.Message);
        BusyIndicator.Visibility = ViewModel.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        foreach (var field in Fields.Children.OfType<Control>()) field.IsEnabled = !ViewModel.IsBusy;
        SwitchLink.IsEnabled = !ViewModel.IsBusy;
    }

    private void ClearPasswordFields() { Password.Password = ""; Confirmation.Password = ""; }

    private void Login_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SwitchCommand.CanExecute(null)) ViewModel.SwitchCommand.Execute(null);
    }

    private void Confirmation_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && ViewModel.SubmitCommand.CanExecute(null))
        {
            ViewModel.SubmitCommand.Execute(null);
            e.Handled = true;
        }
    }
}
