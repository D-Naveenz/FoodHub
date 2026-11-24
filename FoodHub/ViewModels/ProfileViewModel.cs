using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FoodHub.Core.Services;
using FoodHub.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace FoodHub.ViewModels;

/// <summary>Editable customer state and commands, scoped to one profile-page lifetime.</summary>
public partial class ProfileViewModel : ObservableObject, IDisposable
{
    private readonly AccountService _accounts;
    private readonly SessionState _session;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _cancellation;
    private bool _disposed;
    /// <summary>Observable display name form state.</summary>
    private string _displayName = "";
    /// <summary>Observable DisplayName form value.</summary>
    public string DisplayName { get => _displayName; set { SetProperty(ref _displayName, value); } }
    /// <summary>Observable full name form state.</summary>
    private string _fullName = "";
    /// <summary>Observable FullName form value.</summary>
    public string FullName { get => _fullName; set { SetProperty(ref _fullName, value); } }
    /// <summary>Observable email form state.</summary>
    private string _email = "";
    /// <summary>Observable Email form value.</summary>
    public string Email { get => _email; set { SetProperty(ref _email, value); } }
    /// <summary>Observable phone form state.</summary>
    private string _phone = "";
    /// <summary>Observable Phone form value.</summary>
    public string Phone { get => _phone; set { SetProperty(ref _phone, value); } }
    /// <summary>Observable is busy form state.</summary>
    private bool _isBusy;
    /// <summary>Observable IsBusy form value.</summary>
    public bool IsBusy { get => _isBusy; set { SetProperty(ref _isBusy, value); } }
    /// <summary>Observable message form state.</summary>
    private string _message = "";
    /// <summary>Observable Message form value.</summary>
    public string Message { get => _message; set { SetProperty(ref _message, value); } }
    /// <summary>Observable selected address form state.</summary>
    private AddressDetails? _selectedAddress;
    /// <summary>Observable SelectedAddress form value.</summary>
    public AddressDetails? SelectedAddress { get => _selectedAddress; set { if (SetProperty(ref _selectedAddress, value)) OnSelectedAddressChanged(value); } }
    /// <summary>Observable label form state.</summary>
    private string _label = "";
    /// <summary>Observable Label form value.</summary>
    public string Label { get => _label; set { SetProperty(ref _label, value); } }
    /// <summary>Observable line1 form state.</summary>
    private string _line1 = "";
    /// <summary>Observable Line1 form value.</summary>
    public string Line1 { get => _line1; set { SetProperty(ref _line1, value); } }
    /// <summary>Observable line2 form state.</summary>
    private string _line2 = "";
    /// <summary>Observable Line2 form value.</summary>
    public string Line2 { get => _line2; set { SetProperty(ref _line2, value); } }
    /// <summary>Observable city form state.</summary>
    private string _city = "";
    /// <summary>Observable City form value.</summary>
    public string City { get => _city; set { SetProperty(ref _city, value); } }
    /// <summary>Observable postal code form state.</summary>
    private string _postalCode = "";
    /// <summary>Observable PostalCode form value.</summary>
    public string PostalCode { get => _postalCode; set { SetProperty(ref _postalCode, value); } }
    /// <summary>Saved addresses projected without tracked entities.</summary>
    public ObservableCollection<AddressDetails> Addresses { get; } = [];
    /// <summary>Saves editable profile basics.</summary>
    public AsyncRelayCommand SaveBasicsCommand { get; }
    /// <summary>Creates or updates the selected address.</summary>
    public AsyncRelayCommand SaveAddressCommand { get; }
    /// <summary>Deletes a selected, unused address.</summary>
    public AsyncRelayCommand DeleteAddressCommand { get; }
    /// <summary>Starts a blank address form.</summary>
    public RelayCommand NewAddressCommand { get; }
    /// <summary>Revokes and clears the current session.</summary>
    public AsyncRelayCommand SignOutCommand { get; }
    /// <summary>Opens a Windows-owned image picker, then saves normalized bytes.</summary>
    public AsyncRelayCommand PhotoCommand { get; }
    /// <summary>Removes the account image.</summary>
    public AsyncRelayCommand RemovePhotoCommand { get; }
    /// <summary>Reloads only after validating the current session.</summary>
    public AsyncRelayCommand RefreshCommand { get; }

    /// <summary>Creates commands against the shared session instead of accepting customer IDs.</summary>
    public ProfileViewModel(AccountService accounts, SessionState session)
    {
        _accounts = accounts; _session = session; _cancellation = _lifetime.Token;
        SaveBasicsCommand = new(() => Run(() => session.ExecuteAsync((t, ct) => accounts.UpdateBasicsAsync(t,
            new(DisplayName, FullName, Phone), ct), _cancellation)), CanEdit);
        SaveAddressCommand = new(() => Run(() => session.ExecuteAsync((t, ct) => accounts.SaveAddressAsync(t,
            new(SelectedAddress?.Id, Label, Line1, Line2, City, PostalCode), ct), _cancellation)), CanEdit);
        DeleteAddressCommand = new(() => Run(() => session.ExecuteAsync((t, ct) => accounts.DeleteAddressAsync(t,
            SelectedAddress!.Id, ct), _cancellation)), () => CanEdit() && SelectedAddress is not null);
        NewAddressCommand = new(() => { SelectedAddress = null; ClearAddress(); }, CanEdit);
        SignOutCommand = new(() => Run(session.SignOutAsync), () => !IsBusy);
        PhotoCommand = new(() => Run(ChoosePhotoAsync), CanEdit);
        RemovePhotoCommand = new(() => Run(() => session.ExecuteAsync((t, ct) => accounts.UpdatePhotoAsync(t, null, ct), _cancellation)), CanEdit);
        RefreshCommand = new(() => Run(() => session.RefreshAsync(_cancellation)), () => !IsBusy);
        session.PropertyChanged += SessionChanged;
        LoadProfile();
    }

    private bool CanEdit() => !IsBusy && _session.IsAuthenticated;
    /// <summary>Normalizes a selected image and discards it if the page or account changed.</summary>
    private async Task ChoosePhotoAsync()
    {
        var generation = _session.Generation;
        var png = await ProfileImages.PickAsync(App.MainWindow);
        if (png is null || _cancellation.IsCancellationRequested || generation != _session.Generation) return;
        await _session.ExecuteAsync((t, ct) => _accounts.UpdatePhotoAsync(t, png, ct), _cancellation);
    }

    /// <summary>Keeps commands disabled during asynchronous work and translates image errors for the form.</summary>
    private async Task Run(Func<Task> action)
    {
        if (IsBusy) return;
        IsBusy = true; NotifyCommands(); Message = "";
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is ArgumentException or System.IO.IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        { if (!_cancellation.IsCancellationRequested) Message = ex is ArgumentException ? ex.Message : "The image could not be opened. Choose a valid PNG or JPEG."; }
        finally { IsBusy = false; NotifyCommands(); }
    }

    private void SessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionState.Profile)) { LoadProfile(); NotifyCommands(); }
    }

    /// <summary>Replaces editable state with the validated profile, clearing private fields after session loss.</summary>
    private void LoadProfile()
    {
        var profile = _session.Profile;
        DisplayName = profile?.DisplayName ?? ""; FullName = profile?.FullName ?? "";
        Email = profile?.Email ?? ""; Phone = profile?.Phone ?? "";
        Addresses.Clear(); SelectedAddress = null; ClearAddress();
        if (profile is not null) foreach (var address in profile.Addresses) Addresses.Add(address);
    }

    private void OnSelectedAddressChanged(AddressDetails? value)
    {
        Label = value?.Label ?? ""; Line1 = value?.Line1 ?? ""; Line2 = value?.Line2 ?? "";
        City = value?.City ?? ""; PostalCode = value?.PostalCode ?? "";
        DeleteAddressCommand?.NotifyCanExecuteChanged();
    }

    private void ClearAddress() { Label = ""; Line1 = ""; Line2 = ""; City = ""; PostalCode = ""; }
    private void NotifyCommands()
    {
        SaveBasicsCommand.NotifyCanExecuteChanged(); SaveAddressCommand.NotifyCanExecuteChanged(); DeleteAddressCommand.NotifyCanExecuteChanged();
        NewAddressCommand.NotifyCanExecuteChanged(); SignOutCommand.NotifyCanExecuteChanged(); PhotoCommand.NotifyCanExecuteChanged();
        RemovePhotoCommand.NotifyCanExecuteChanged(); RefreshCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Detaches session notifications and cancels in-flight page work.</summary>
    public void Dispose()
    {
        _session.PropertyChanged -= SessionChanged;
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        // Continuations use the captured token rather than the disposed source.
    }
}