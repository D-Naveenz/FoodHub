using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FoodHub.Core.Services;
using FoodHub.Services;

namespace FoodHub.ViewModels;

/// <summary>Owns form state and async submission; window code handles only window behavior.</summary>
public partial class AuthenticationViewModel : ObservableObject, IDisposable
{
    private readonly AccountService _accounts;
    private readonly SessionState _session;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _cancellation;
    private bool _disposed;
    /// <summary>Observable email form state.</summary>
    private string _email = "";
    /// <summary>Observable Email form value.</summary>
    public string Email { get => _email; set { SetProperty(ref _email, value); } }
    /// <summary>Observable display name form state.</summary>
    private string _displayName = "";
    /// <summary>Observable DisplayName form value.</summary>
    public string DisplayName { get => _displayName; set { SetProperty(ref _displayName, value); } }
    /// <summary>Observable full name form state.</summary>
    private string _fullName = "";
    /// <summary>Observable FullName form value.</summary>
    public string FullName { get => _fullName; set { SetProperty(ref _fullName, value); } }
    /// <summary>Observable phone form state.</summary>
    private string _phone = "";
    /// <summary>Observable Phone form value.</summary>
    public string Phone { get => _phone; set { SetProperty(ref _phone, value); } }
    /// <summary>Observable remember form state.</summary>
    private bool _remember;
    /// <summary>Observable Remember form value.</summary>
    public bool Remember { get => _remember; set { SetProperty(ref _remember, value); } }
    /// <summary>Observable is registration form state.</summary>
    private bool _isRegistration;
    /// <summary>Observable IsRegistration form value.</summary>
    public bool IsRegistration { get => _isRegistration; set { SetProperty(ref _isRegistration, value); } }
    /// <summary>Observable is busy form state.</summary>
    private bool _isBusy;
    /// <summary>Observable IsBusy form value.</summary>
    public bool IsBusy { get => _isBusy; set { SetProperty(ref _isBusy, value); } }
    /// <summary>Observable message form state.</summary>
    private string _message = "";
    /// <summary>Observable Message form value.</summary>
    public string Message { get => _message; set { SetProperty(ref _message, value); } }
    /// <summary>Raised only after a live form successfully publishes a new session.</summary>
    public event EventHandler? Authenticated;
    /// <summary>Controls created in XAML do not retain passwords in the view model.</summary>
    public AsyncRelayCommand SubmitCommand { get; }
    /// <summary>Switches between forms without creating another native window.</summary>
    public RelayCommand SwitchCommand { get; }
    /// <summary>Supplies the current password fields transiently at submission.</summary>
    public Func<(string Password, string Confirmation)> ReadPasswords { private get; set; } = () => ("", "");
    /// <summary>Clears sensitive UI fields after submitting or switching forms.</summary>
    public Action ClearPasswords { private get; set; } = () => { };

    /// <summary>Creates the form for either entry route.</summary>
    public AuthenticationViewModel(AccountService accounts, SessionState session, bool registration)
    {
        _accounts = accounts; _session = session; _cancellation = _lifetime.Token; IsRegistration = registration;
        SubmitCommand = new(SubmitAsync, () => !IsBusy);
        SwitchCommand = new(() =>
        {
            // Clear the outgoing page before changing the mode: navigation attaches the incoming page's password fields.
            ClearPasswords();
            Message = "";
            IsRegistration = !IsRegistration;
        }, () => !IsBusy);
    }

    /// <summary>Submits one form, ignores obsolete results and clears PasswordBox values on every exit.</summary>
    private async Task SubmitAsync()
    {
        IsBusy = true; SubmitCommand.NotifyCanExecuteChanged(); SwitchCommand.NotifyCanExecuteChanged(); Message = "";
        var generation = _session.Generation;
        try
        {
            var (password, confirmation) = ReadPasswords();
            var result = IsRegistration
                ? await _accounts.RegisterAsync(new(DisplayName, FullName, Email, Phone, password, confirmation, Remember), _cancellation)
                : await _accounts.LoginAsync(Email, password, Remember, _cancellation);
            if (_cancellation.IsCancellationRequested || generation != _session.Generation) return;
            if (!result.Succeeded) { Message = result.Message; return; }
            await _session.AcceptAsync(result.Value!, Remember);
            if (!_cancellation.IsCancellationRequested) Authenticated?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) { }
        finally
        {
            ClearPasswords(); IsBusy = false; SubmitCommand.NotifyCanExecuteChanged(); SwitchCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>Cancels requests and suppresses late results after the window closes.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        ClearPasswords();
        ReadPasswords = static () => ("", "");
        ClearPasswords = static () => { };
    }
}