using CommunityToolkit.Mvvm.ComponentModel;
using FoodHub.Core.Services;
using Microsoft.UI.Xaml;

namespace FoodHub.Services;

/// <summary>Observable session shared by all windows; guards against late results from an obsolete account.</summary>
public sealed class SessionState : ObservableObject, IDisposable
{
    private readonly AccountService _accounts;
    private readonly SessionStorage _storage;
    private readonly DispatcherTimer _expiryTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private SessionTicket? _ticket;
    private string? _token;
    private string? _pendingSignOutToken;
    private DateTime _expiry;
    private int _generation;
    private string _message = "";

    /// <summary>Creates the session coordinator and checks fixed expiry while the app remains open.</summary>
    public SessionState(AccountService accounts, SessionStorage storage)
    {
        _accounts = accounts; _storage = storage;
        _expiryTimer.Tick += CheckExpiry;
        _expiryTimer.Start();
    }

    /// <summary>Clears an expired session on the UI dispatcher.</summary>
    private async void CheckExpiry(object? sender, object e)
    {
        if (_token is not null && DateTime.UtcNow >= _expiry)
        {
            var generation = _generation + 1;
            await ClearAsync();
            if (_generation == generation) Message = "Your session has ended. Please sign in again.";
        }
    }

    /// <summary>Fresh profile only; null after expiry or database failure.</summary>
    public ProfileDetails? Profile => _ticket?.Profile;
    /// <summary>True only when validated private state is available.</summary>
    public bool IsAuthenticated => Profile is not null;
    /// <summary>Display name or Guest, suitable for navigation.</summary>
    public string DisplayName => Profile?.DisplayName ?? "Guest";
    /// <summary>Version used to reject asynchronous results from an old session.</summary>
    public int Generation => _generation;
    /// <summary>Safe status feedback shown by the shell.</summary>
    public string Message { get => _message; set => SetProperty(ref _message, value); }
    /// <summary>Whether a retained token can be retried following an unavailable database.</summary>
    public bool CanRetry => _token is not null && !IsAuthenticated;
    /// <summary>Allows retrying cleanup/revocation without making the account authenticated again.</summary>
    public bool CanSignOut => _token is not null || _pendingSignOutToken is not null;

    /// <summary>Restores a remembered token only after validation against SQL Server.</summary>
    public async Task RestoreAsync(CancellationToken cancellation = default)
    {
        var generation = _generation;
        var token = await _storage.ReadAsync();
        if (generation != _generation || token is null) return;
        _token = token;
        // The database supplies the real expiry; allow retry if it is temporarily unavailable.
        _expiry = DateTime.MaxValue;
        await RefreshAsync(cancellation);
    }

    /// <summary>Publishes a successful sign-in and persists only when requested.</summary>
    public async Task AcceptAsync(SessionTicket ticket, bool remember)
    {
        _generation++;
        _pendingSignOutToken = null;
        _token = ticket.Token; _expiry = ticket.ExpiresAtUtc;
        _ticket = ticket; Message = ""; Notify();
        try { await _storage.WriteAsync(remember ? ticket.Token : null); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        { Message = "Signed in for this visit, but Windows could not save the session."; }
    }

    /// <summary>Revalidates the current token; failures hide private state without deleting a retryable token.</summary>
    public Task RefreshAsync(CancellationToken cancellation = default) => ExecuteAsync((token, ct) => _accounts.ValidateAsync(token, ct), cancellation);

    /// <summary>Runs an owned operation and discards results after account changes.</summary>
    public async Task ExecuteAsync(Func<string, CancellationToken, Task<AccountResult<SessionTicket>>> operation, CancellationToken cancellation = default)
    {
        if (_token is null) return;
        var generation = _generation; var token = _token;
        var result = await operation(token, cancellation);
        if (generation != _generation || cancellation.IsCancellationRequested) return;
        if (result.Succeeded)
        {
            _ticket = result.Value; _expiry = result.Value!.ExpiresAtUtc; Message = ""; Notify();
        }
        else if (result.Error == AccountError.Expired)
        {
            await ClearAsync();
            if (_generation == generation + 1) Message = result.Message;
        }
        else
        {
            if (result.Error is AccountError.Database or AccountError.Configuration) { _ticket = null; Notify(); }
            Message = result.Message;
        }
    }

    /// <summary>Clears local private state immediately, then attempts remote revocation.</summary>
    public async Task SignOutAsync()
    {
        var token = _token ?? _pendingSignOutToken;
        _pendingSignOutToken = token;
        var generation = _generation + 1;
        var removed = await ClearAsync();
        if (token is null) return;
        var result = await _accounts.LogoutAsync(token);
        if (_generation != generation) return;
        if (result.Succeeded && removed) { _pendingSignOutToken = null; Notify(); }
        if (!result.Succeeded) Message = removed
            ? "Signed out locally. The database could not revoke this session; it will expire automatically."
            : "Private state was cleared, but saved session removal and database revocation failed. Retry sign-out before closing the app.";
    }

    /// <summary>Clears private memory before waiting for protected-storage removal.</summary>
    private async Task<bool> ClearAsync()
    {
        var generation = ++_generation;
        _ticket = null; _token = null; Message = ""; Notify();
        try { await _storage.WriteAsync(null); return true; }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            if (_generation == generation) Message = "Local session data could not be removed. Check access to your FoodHub app-data folder.";
            return false;
        }
    }

    /// <summary>Stops dispatcher work and detaches the timer when the host closes.</summary>
    public void Dispose()
    {
        _generation++;
        _expiryTimer.Stop();
        _expiryTimer.Tick -= CheckExpiry;
    }

    /// <summary>Updates every session-derived property so both windows show the same account.</summary>
    private void Notify()
    {
        OnPropertyChanged(nameof(Profile)); OnPropertyChanged(nameof(IsAuthenticated));
        OnPropertyChanged(nameof(DisplayName)); OnPropertyChanged(nameof(CanRetry)); OnPropertyChanged(nameof(CanSignOut));
    }
}