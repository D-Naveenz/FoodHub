using Dhara.Extensions.WinUI.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FoodHub.Core.Services;
using FoodHub.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FoodHub.ViewModels;

/// <summary>Exposes local storage inspection only when the host environment is Development.</summary>
public sealed class DevelopmentSettingsViewModel : ObservableObject, IDisposable
{
    private readonly ILocalStorageFolderLauncher _launcher;
    private readonly ILogger<DevelopmentSettingsViewModel> _logger;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _cancellation;
    private bool _disposed;
    private bool _isOpening;
    private string _message = "";
    /// <summary>Runtime environment gate, independent of build configuration and package identity.</summary>
    public bool IsDevelopment { get; }
    /// <summary>The active file storage root; native preferences are stored separately.</summary>
    public string StoragePath { get; }
    /// <summary>Prevents duplicate Explorer launches.</summary>
    public bool IsOpening { get => _isOpening; private set { SetProperty(ref _isOpening, value); OpenFolderCommand.NotifyCanExecuteChanged(); } }
    /// <summary>Safe folder-launch feedback, without file contents or exception payloads.</summary>
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    /// <summary>Opens the active storage folder; disabled outside Development.</summary>
    public AsyncRelayCommand OpenFolderCommand { get; }
    /// <summary>Captures the host environment and storage root for one Settings page.</summary>
    public DevelopmentSettingsViewModel(IHostEnvironment environment, ILocalStorageService storage,
        ILocalStorageFolderLauncher launcher, ILogger<DevelopmentSettingsViewModel> logger)
    {
        IsDevelopment = environment.IsDevelopment(); StoragePath = storage.RootDirectory;
        _launcher = launcher; _logger = logger; _cancellation = _lifetime.Token;
        OpenFolderCommand = new(OpenAsync, () => IsDevelopment && !IsOpening && !_disposed);
    }
    /// <summary>Guards even programmatic execution and reports expected Windows launcher failures.</summary>
    private async Task OpenAsync()
    {
        if (!IsDevelopment || IsOpening || _disposed) return;
        IsOpening = true; Message = "";
        try
        {
            if (!await _launcher.OpenAsync(_cancellation) && !_disposed) Message = "Windows could not open the local storage folder.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            _logger.LogWarning("Local storage folder launch failed: {Category}", ex.GetType().Name);
            if (!_disposed) Message = "Could not open local storage. Check access to the application data folder.";
        }
        finally { IsOpening = false; }
    }
    /// <summary>Cancels pending launch work when the page unloads.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _lifetime.Cancel(); _lifetime.Dispose();
        OpenFolderCommand.NotifyCanExecuteChanged();
    }
}