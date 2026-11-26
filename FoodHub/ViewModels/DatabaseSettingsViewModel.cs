using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FoodHub.Core.Services;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;

namespace FoodHub.ViewModels;

/// <summary>Owns database setup fields and cancellable read-only connection checks for one page.</summary>
public sealed class DatabaseSettingsViewModel : ObservableObject, IDisposable
{
    private readonly DatabaseSettingsStore _store;
    private readonly DatabaseConnectionTester _tester;
    private readonly ILogger<DatabaseSettingsViewModel> _logger;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _cancellation;
    private CancellationTokenSource? _check;
    private bool _disposed;
    private string _server = "";
    private string _database = "";
    private bool _trustCertificate;
    private bool _isBusy;
    private string _message = "";
    private InfoBarSeverity _messageSeverity = InfoBarSeverity.Informational;
    /// <summary>SQL Server host or named instance.</summary>
    public string Server { get => _server; set => SetProperty(ref _server, value); }
    /// <summary>Existing FoodHub database to use after restart.</summary>
    public string Database { get => _database; set => SetProperty(ref _database, value); }
    /// <summary>Allows an untrusted certificate for a local development server.</summary>
    public bool TrustCertificate { get => _trustCertificate; set => SetProperty(ref _trustCertificate, value); }
    /// <summary>Disables edits during one test/save operation.</summary>
    public bool IsBusy { get => _isBusy; private set { SetProperty(ref _isBusy, value); NotifyCommands(); } }
    /// <summary>Safe operation or configuration feedback.</summary>
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    /// <summary>Native semantic styling for the current feedback, independent of its wording.</summary>
    public InfoBarSeverity MessageSeverity { get => _messageSeverity; private set => SetProperty(ref _messageSeverity, value); }
    /// <summary>Saved changes remain pending until the app restarts.</summary>
    public bool RestartRequired => _store.RestartRequired;
    /// <summary>Tests the edited values without saving them.</summary>
    public AsyncRelayCommand TestCommand { get; }
    /// <summary>Saves edited values even when SQL is offline.</summary>
    public AsyncRelayCommand SaveCommand { get; }
    /// <summary>Removes the per-user connection override.</summary>
    public AsyncRelayCommand ResetCommand { get; }
    /// <summary>Cancels only the current connection check.</summary>
    public RelayCommand CancelCommand { get; }

    /// <summary>Reads the active or pending snapshot and creates page-owned commands.</summary>
    public DatabaseSettingsViewModel(DatabaseSettings active, DatabaseSettingsStore store,
        DatabaseConnectionTester tester, ILogger<DatabaseSettingsViewModel> logger)
    {
        _store = store; _tester = tester; _logger = logger; _cancellation = _lifetime.Token;
        SetFeedback(active.LoadError ?? "", active.LoadError is null ? InfoBarSeverity.Informational : InfoBarSeverity.Error);
        var connection = store.RestartRequired ? store.PendingConnectionString : active.ConnectionString;
        try
        {
            var builder = new SqlConnectionStringBuilder(connection ?? "");
            Server = builder.DataSource; Database = builder.InitialCatalog; TrustCertificate = builder.TrustServerCertificate;
            if (active.LoadError is null && !string.IsNullOrEmpty(connection) && !builder.IntegratedSecurity)
                SetFeedback("The active connection uses SQL authentication. This form saves Windows authentication settings.", InfoBarSeverity.Informational);
        }
        catch (ArgumentException) { SetFeedback("The saved configuration is invalid. Enter valid database settings or use defaults.", InfoBarSeverity.Error); }
        TestCommand = new(() => RunAsync(TestAsync), () => !IsBusy);
        SaveCommand = new(() => RunAsync(() => _store.SaveAsync(BuildConnection(), _cancellation)), () => !IsBusy);
        ResetCommand = new(() => RunAsync(async () =>
        {
            await _store.SaveAsync(null, _cancellation);
            Server = ""; Database = ""; TrustCertificate = false;
        }), () => !IsBusy);
        CancelCommand = new(() => _check?.Cancel(), () => _check is not null);
    }

    /// <summary>Builds a candidate connection; edited values never enter active services.</summary>
    private string BuildConnection() => DatabaseSettingsStore.BuildConnectionString(Server, Database, TrustCertificate);
    /// <summary>Bounds and cancels SQL work independently of saving.</summary>
    private async Task TestAsync()
    {
        var candidate = BuildConnection();
        using var check = CancellationTokenSource.CreateLinkedTokenSource(_cancellation);
        _check = check; CancelCommand.NotifyCanExecuteChanged();
        try
        {
            var result = await _tester.TestAsync(candidate, check.Token);
            // Classify by the service outcome, so changing message wording cannot change its styling.
            var severity = result.Status switch
            {
                ConnectionCheckStatus.Success => InfoBarSeverity.Success,
                ConnectionCheckStatus.Warning => InfoBarSeverity.Warning,
                _ => InfoBarSeverity.Error
            };
            SetFeedback(result.Message, severity);
        }
        finally { _check = null; CancelCommand.NotifyCanExecuteChanged(); }
    }
    /// <summary>Serializes page work and presents safe errors without logging connection details.</summary>
    private async Task RunAsync(Func<Task> action)
    {
        if (IsBusy || _disposed) return;
        IsBusy = true; SetFeedback("", InfoBarSeverity.Informational);
        try
        {
            await action();
            if (RestartRequired && string.IsNullOrEmpty(Message)) SetFeedback("Saved. Restart FoodHub to apply the database configuration.", InfoBarSeverity.Success);
        }
        catch (OperationCanceledException) { if (!_disposed) SetFeedback("Connection check cancelled.", InfoBarSeverity.Informational); }
        catch (DatabaseConfigurationException ex) { SetFeedback(ex.Message, InfoBarSeverity.Error); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or ArgumentException)
        {
            _logger.LogWarning("Database settings operation failed: {Category}", ex.GetType().Name);
            SetFeedback("Could not save database settings. Check access to FoodHub local application settings.", InfoBarSeverity.Error);
        }
        finally { IsBusy = false; OnPropertyChanged(nameof(RestartRequired)); }
    }
    /// <summary>Sets the semantic presentation before publishing new message text to the view.</summary>
    private void SetFeedback(string message, InfoBarSeverity severity)
    {
        MessageSeverity = severity;
        Message = message;
    }
    /// <summary>Refreshes command availability after each operation.</summary>
    private void NotifyCommands()
    {
        TestCommand?.NotifyCanExecuteChanged(); SaveCommand?.NotifyCanExecuteChanged(); ResetCommand?.NotifyCanExecuteChanged();
    }
    /// <summary>Cancels pending checks when navigation removes the page.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _lifetime.Cancel(); _lifetime.Dispose();
    }
}