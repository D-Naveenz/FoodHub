using System.Diagnostics;
using Dhara.Extensions.WinUI.Threading;
using Dhara.Extensions.WinUI.Windowing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace Dhara.Extensions.WinUI.Hosting;

/// <summary>Provides optional ordered application startup work around native main-window creation and activation.</summary>
/// <remarks>
/// Participants run sequentially in registration order on the UI dispatcher. They must honor cancellation and
/// keep long-running work asynchronous. Recoverable domain errors belong in the participant; unhandled errors
/// abort startup and trigger graceful host cleanup. Normal ongoing work belongs in separate hosted services.
/// </remarks>
public interface IWinUIStartupParticipant
{
    /// <summary>Initializes state needed by window constructors, such as the selected appearance.</summary>
    /// <param name="cancellation">Cancelled when launch or the application is stopping.</param>
    /// <returns>A task completing before the main window is resolved from DI.</returns>
    Task BeforeWindowCreatedAsync(CancellationToken cancellation) => Task.CompletedTask;
    /// <summary>Prepares application state after host startup and before the main window is activated.</summary>
    /// <param name="cancellation">Cancelled when launch or the application is stopping.</param>
    /// <returns>A task completing before the window becomes active.</returns>
    Task BeforeWindowActivatedAsync(CancellationToken cancellation) => Task.CompletedTask;
    /// <summary>Performs work that may update the visible window, such as restoring an account session.</summary>
    /// <param name="args">The original WinUI launch arguments.</param>
    /// <param name="cancellation">Cancelled when launch or the application is stopping.</param>
    /// <returns>A task completing when initial application startup has finished.</returns>
    Task AfterWindowActivatedAsync(LaunchActivatedEventArgs args, CancellationToken cancellation) => Task.CompletedTask;
}

/// <summary>Connects a generated WinUI App to its Generic Host without requiring an App interface or base class.</summary>
/// <typeparam name="TMainWindow">The consumer's main-window type.</typeparam>
/// <remarks>The controller owns the built host's lifecycle; callers must not start, stop, or dispose the host independently.</remarks>
public interface IWinUIApplicationLifetime<TMainWindow> where TMainWindow : Window
{
    /// <summary>Gets the main window after it has been created on the dispatcher.</summary>
    /// <exception cref="InvalidOperationException">Startup has not created a main window.</exception>
    TMainWindow MainWindow { get; }
    /// <summary>Starts the host and activates one main window, reusing the initial launch task on repeated calls.</summary>
    /// <param name="args">The framework's original launch arguments.</param>
    /// <param name="cancellation">Cancels the initial launch cooperatively.</param>
    /// <returns>A task completing after postactivation startup participants finish.</returns>
    Task StartAsync(LaunchActivatedEventArgs args, CancellationToken cancellation = default);
    /// <summary>Requests graceful shutdown, sharing one task across repeated close and host-stop requests.</summary>
    /// <returns>A task completing after host cleanup and final native closure have been queued.</returns>
    Task RequestShutdownAsync();
}

/// <summary>Creates the generic main window and keeps it alive until the host and its services have stopped.</summary>
internal sealed class WinUIWindowHostedService<TMainWindow>(IServiceProvider services, IUIThreadDispatcher dispatcher,
    WindowRegistry windows, IHostApplicationLifetime lifetime, IEnumerable<IWinUIStartupParticipant> participants) : IHostedService
    where TMainWindow : Window
{
    private TMainWindow? _window;
    private bool _allowClose;
    /// <summary>Gets the initialized main window, failing clearly before startup has allocated it.</summary>
    internal TMainWindow Window => _window ?? throw new InvalidOperationException("The main window has not started.");
    /// <summary>Gets the window if allocated, including after partially failed startup.</summary>
    internal TMainWindow? CreatedWindow => _window;

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken) => dispatcher.InvokeAsync(async () =>
    {
        if (_window is not null) return;
        foreach (var participant in participants)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await participant.BeforeWindowCreatedAsync(cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
        _window = services.GetRequiredService<TMainWindow>();
        _window.AppWindow.Closing += Closing;
        _window.Closed += Closed;
        windows.Register(_window);
    }, cancellationToken);

    /// <summary>Cancels user closure while the UI dispatcher is still needed by host shutdown.</summary>
    private void Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose) return;
        args.Cancel = true;
        lifetime.StopApplication();
    }

    /// <summary>Also handles programmatic Window.Close, which may bypass AppWindow's user-close notification.</summary>
    private void Closed(object sender, WindowEventArgs args)
    {
        if (_allowClose) return;
        args.Handled = true;
        lifetime.StopApplication();
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => dispatcher.InvokeAsync(() =>
        windows.CloseSecondaryWindows(_window), cancellationToken);

    /// <summary>Detaches interception and closes after the current native close callback has unwound.</summary>
    internal bool QueueFinalClose(Action exit) => dispatcher.TryEnqueue(() =>
    {
        _allowClose = true;
        if (_window is null) { exit(); return; }
        _window.AppWindow.Closing -= Closing;
        _window.Closed -= Closed;
        _window.Close();
    });
}

/// <summary>Serializes launch and shutdown while preserving WinUI thread affinity and host ownership.</summary>
internal sealed class WinUIApplicationLifetime<TMainWindow> : IWinUIApplicationLifetime<TMainWindow> where TMainWindow : Window
{
    private readonly IHost _host;
    private readonly IUIThreadDispatcher _dispatcher;
    private readonly WinUIWindowHostedService<TMainWindow> _windows;
    private readonly WindowRegistry _registry;
    private readonly WinUIApplicationContext _application;
    private readonly WinUIApplicationOptions _options;
    private readonly IWinUIStartupParticipant[] _participants;
    private readonly ILogger<WinUIApplicationLifetime<TMainWindow>> _logger;
    private readonly CancellationTokenSource _stopping = new();
    private readonly CancellationTokenRegistration _hostStopping;
    private Task? _launchTask;
    private Task? _shutdownTask;
    private bool _closing;

    /// <summary>Subscribes to host-stop signals without starting services or resolving the main window.</summary>
    public WinUIApplicationLifetime(IHost host, IUIThreadDispatcher dispatcher, WinUIWindowHostedService<TMainWindow> windows,
        WindowRegistry registry, WinUIApplicationContext application, WinUIApplicationOptions options,
        IEnumerable<IWinUIStartupParticipant> participants, IHostApplicationLifetime lifetime,
        ILogger<WinUIApplicationLifetime<TMainWindow>> logger)
    {
        _host = host;
        _dispatcher = dispatcher;
        _windows = windows;
        _registry = registry;
        _application = application;
        _options = options;
        _participants = participants.ToArray();
        _logger = logger;
        // Always enqueue: StopApplication may run within StartAsync or a native closing callback.
        _hostStopping = lifetime.ApplicationStopping.Register(() =>
        {
            if (!_dispatcher.TryEnqueue(() => _ = RequestShutdownAsync()))
                Trace.TraceError("WinUI shutdown could not be queued because the UI dispatcher has stopped.");
        });
    }

    /// <inheritdoc/>
    public TMainWindow MainWindow => _windows.Window;

    /// <inheritdoc/>
    public Task StartAsync(LaunchActivatedEventArgs args, CancellationToken cancellation = default) => _dispatcher.InvokeAsync(() =>
    {
        if (_closing) return Task.FromException(new InvalidOperationException("The WinUI application is stopping."));
        return _launchTask ??= LaunchAsync(args, cancellation);
    });

    /// <summary>Starts host services before activation, then performs visible application initialization.</summary>
    private async Task LaunchAsync(LaunchActivatedEventArgs args, CancellationToken cancellation)
    {
        using var launch = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _stopping.Token);
        try
        {
            await _host.StartAsync(launch.Token);
            foreach (var participant in _participants)
            {
                launch.Token.ThrowIfCancellationRequested();
                await participant.BeforeWindowActivatedAsync(launch.Token);
            }
            launch.Token.ThrowIfCancellationRequested();
            MainWindow.Activate();
            foreach (var participant in _participants)
            {
                launch.Token.ThrowIfCancellationRequested();
                await participant.AfterWindowActivatedAsync(args, launch.Token);
            }
        }
        catch (Exception ex)
        {
            // Schedule, rather than await, shutdown: shutdown itself waits for this launch task to unwind.
            _logger.LogError("WinUI startup failed: {Category}", ex.GetType().Name);
            _dispatcher.TryEnqueue(() => _ = RequestShutdownAsync());
            throw;
        }
    }

    /// <inheritdoc/>
    public Task RequestShutdownAsync() => _dispatcher.InvokeAsync(() => _shutdownTask ??= ShutdownAsync());

    /// <summary>Cancels launch, stops services, disposes the host and queues final closure within a bounded budget.</summary>
    private async Task ShutdownAsync()
    {
        _closing = true;
        using var timeout = new CancellationTokenSource(_options.ShutdownTimeout);
        try
        {
            _stopping.Cancel();
            if (_launchTask is not null)
                try { await _launchTask.WaitAsync(timeout.Token); }
                catch (Exception ex) { _logger.LogDebug("WinUI launch unwound during shutdown: {Category}", ex.GetType().Name); }
            // Close owned windows while their view models and services are still usable, including failed-startup paths.
            try { _registry.CloseSecondaryWindows(_windows.CreatedWindow); }
            catch (Exception ex) { _logger.LogError("WinUI secondary-window cleanup failed: {Category}", ex.GetType().Name); }
            await _host.StopAsync(timeout.Token).WaitAsync(timeout.Token);
        }
        catch (Exception ex) { _logger.LogError("WinUI host stop failed: {Category}", ex.GetType().Name); }
        finally
        {
            _hostStopping.Dispose();
            try
            {
                if (_host is IAsyncDisposable asynchronous) await asynchronous.DisposeAsync().AsTask().WaitAsync(timeout.Token);
                else _host.Dispose();
            }
            catch (Exception ex) { Trace.TraceError("WinUI host disposal failed: {0}", ex.GetType().Name); }
            _stopping.Dispose();
            if (!_windows.QueueFinalClose(_application.Exit))
                Trace.TraceError("WinUI final closure could not be queued because the UI dispatcher has stopped.");
        }
    }
}