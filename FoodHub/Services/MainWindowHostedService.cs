using Microsoft.Extensions.Hosting;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace FoodHub.Services;

/// <summary>Exposes the one main window without giving pages responsibility for host lifetime.</summary>
public interface IMainWindowService
{
    /// <summary>The window created during host startup.</summary>
    MainWindow Window { get; }
    /// <summary>Activates the main window after application launch preparation.</summary>
    void Activate();
    /// <summary>Queues the final native close after host disposal has completed.</summary>
    void CompleteShutdown();
}

/// <summary>Owns main-window creation and close monitoring on the WinUI dispatcher.</summary>
public sealed class MainWindowHostedService(Func<MainWindow> createWindow, AppearanceSettings appearance,
    IHostApplicationLifetime lifetime) : IHostedService, IMainWindowService
{
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private MainWindow? _window;
    private bool _stopped;
    /// <inheritdoc/>
    public MainWindow Window => _window ?? throw new InvalidOperationException("The main window has not started.");
    /// <summary>Loads appearance asynchronously before constructing any native window or page.</summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_window is not null) return;
        await appearance.LoadAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await OnDispatcherAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_window is not null) return;
            _window = createWindow();
            _window.Closed += WindowClosed;
        });
    }
    /// <inheritdoc/>
    public void Activate() { VerifyDispatcher(); if (!_stopped) Window.Activate(); }
    /// <summary>Defers native closure until App finishes the one host-shutdown task.</summary>
    private void WindowClosed(object sender, WindowEventArgs args)
    {
        args.Handled = true;
        lifetime.StopApplication();
    }
    /// <summary>Detaches monitoring while keeping the dispatcher and main window alive for service disposal.</summary>
    public Task StopAsync(CancellationToken cancellationToken) => OnDispatcherAsync(() =>
    {
        if (_stopped) return;
        _stopped = true;
        if (_window is not null)
        {
            _window.Closed -= WindowClosed;
            _window.AuthenticationWindow?.Close();
        }
    });
    /// <summary>Marshals hosted-service callbacks even when another hosted service resumed the host on a worker thread.</summary>
    private Task OnDispatcherAsync(Action action)
    {
        if (_dispatcher.HasThreadAccess) { action(); return Task.CompletedTask; }
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcher.TryEnqueue(() =>
        {
            try { action(); completion.SetResult(); }
            catch (Exception ex) { completion.SetException(ex); }
        })) completion.SetException(new InvalidOperationException("The UI dispatcher has stopped."));
        return completion.Task;
    }
    /// <inheritdoc/>
    public void CompleteShutdown()
    {
        VerifyDispatcher();
        if (_window is not null) _dispatcher.TryEnqueue(() => _window.Close());
    }
    /// <summary>Fails early if a future caller attempts to construct or dispose UI state on a worker thread.</summary>
    private void VerifyDispatcher()
    {
        if (!_dispatcher.HasThreadAccess) throw new InvalidOperationException("Window lifetime requires the UI dispatcher.");
    }
}