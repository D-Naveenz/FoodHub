using Microsoft.UI.Dispatching;

namespace Dhara.Extensions.WinUI.Threading;

/// <summary>Runs native UI work on an explicitly captured dispatcher rather than whichever thread resolves a service.</summary>
public interface IUIThreadDispatcher
{
    /// <summary>Gets whether the current thread owns the UI queue.</summary>
    bool HasThreadAccess { get; }
    /// <summary>Queues a callback even when already on the UI thread; false means the queue is shutting down.</summary>
    /// <param name="action">The callback, which must handle its own exceptions.</param>
    /// <returns>Whether the queue accepted the callback.</returns>
    bool TryEnqueue(Action action);
    /// <summary>Runs synchronous UI work and observes its completion, exception, or cancellation.</summary>
    /// <param name="action">Work to run on the UI thread.</param>
    /// <param name="cancellation">Suppresses queued work that has not started.</param>
    /// <returns>A task representing the callback's completion.</returns>
    Task InvokeAsync(Action action, CancellationToken cancellation = default);
    /// <summary>Runs asynchronous UI work, retaining the dispatcher's synchronization context across awaits.</summary>
    /// <param name="action">The asynchronous callback; cancellation after it starts remains cooperative.</param>
    /// <param name="cancellation">Suppresses work cancelled before execution.</param>
    /// <returns>A task representing the entire callback, including asynchronous continuations.</returns>
    Task InvokeAsync(Func<Task> action, CancellationToken cancellation = default);
}

/// <summary>Adapts a queue captured on the application's UI thread without creating or owning a message loop.</summary>
/// <param name="queue">The existing WinUI dispatcher queue.</param>
public sealed class UIThreadDispatcher(DispatcherQueue queue) : IUIThreadDispatcher
{
    /// <inheritdoc/>
    public bool HasThreadAccess => queue.HasThreadAccess;
    /// <inheritdoc/>
    public bool TryEnqueue(Action action) => queue.TryEnqueue(() => action());
    /// <inheritdoc/>
    public Task InvokeAsync(Action action, CancellationToken cancellation = default) => InvokeAsync(() =>
    {
        action();
        return Task.CompletedTask;
    }, cancellation);

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">The queue rejected the callback because it is shutting down.</exception>
    public Task InvokeAsync(Func<Task> action, CancellationToken cancellation = default)
    {
        if (cancellation.IsCancellationRequested) return Task.FromCanceled(cancellation);
        if (HasThreadAccess) return action();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;
        var registration = cancellation.Register(() =>
        {
            // Cancellation may win before dispatch; once execution starts, the callback owns cooperative cancellation.
            if (Interlocked.CompareExchange(ref started, 2, 0) == 0) completion.TrySetCanceled(cancellation);
        });
        if (!TryEnqueue(async () =>
        {
            if (Interlocked.CompareExchange(ref started, 1, 0) != 0) return;
            try
            {
                cancellation.ThrowIfCancellationRequested();
                await action();
                completion.TrySetResult();
            }
            catch (OperationCanceledException ex) { completion.TrySetCanceled(ex.CancellationToken); }
            catch (Exception ex) { completion.TrySetException(ex); }
        })) completion.TrySetException(new InvalidOperationException("The UI dispatcher has stopped."));
        return ObserveCompletionAsync(completion.Task, registration);
    }

    /// <summary>Releases the cancellation subscription whether dispatch completes, is rejected, or is cancelled.</summary>
    private static async Task ObserveCompletionAsync(Task completion, CancellationTokenRegistration registration)
    {
        try { await completion.ConfigureAwait(false); }
        finally { registration.Dispose(); }
    }
}