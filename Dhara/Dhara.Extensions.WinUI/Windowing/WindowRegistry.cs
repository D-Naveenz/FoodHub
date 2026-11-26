using Dhara.Extensions.WinUI.Threading;
using Microsoft.UI.Xaml;

namespace Dhara.Extensions.WinUI.Windowing;

/// <summary>Tracks explicitly registered windows for theme application and orderly application shutdown.</summary>
/// <remarks>All access is dispatcher-bound. Closed windows unregister themselves; this is not a global window discovery service.</remarks>
public interface IWindowRegistry
{
    /// <summary>Gets a snapshot of live registered windows in registration order.</summary>
    IReadOnlyList<Window> Windows { get; }
    /// <summary>Gets whether the application has begun closing its registered windows.</summary>
    bool IsShuttingDown { get; }
    /// <summary>Notifies theme adapters when a window is registered after its content has been initialized.</summary>
    event Action<Window>? WindowRegistered;
    /// <summary>Registers a window once and automatically removes it when it closes.</summary>
    /// <param name="window">The initialized main or secondary window.</param>
    void Register(Window window);
}

/// <summary>Keeps strong references only while registered windows are live.</summary>
/// <param name="dispatcher">The owning UI dispatcher.</param>
public sealed class WindowRegistry(IUIThreadDispatcher dispatcher) : IWindowRegistry
{
    private readonly List<Window> _windows = [];
    /// <inheritdoc/>
    public IReadOnlyList<Window> Windows { get { VerifyAccess(); return _windows.ToArray(); } }
    /// <inheritdoc/>
    public bool IsShuttingDown { get; private set; }
    /// <inheritdoc/>
    public event Action<Window>? WindowRegistered;

    /// <inheritdoc/>
    public void Register(Window window)
    {
        VerifyAccess();
        if (IsShuttingDown) throw new InvalidOperationException("Windows cannot be registered during shutdown.");
        if (_windows.Contains(window)) return;
        _windows.Add(window);
        window.Closed += WindowClosed;
        WindowRegistered?.Invoke(window);
    }

    /// <summary>Closes secondary windows while application dependencies are still available.</summary>
    internal void CloseSecondaryWindows(Window? mainWindow)
    {
        VerifyAccess();
        IsShuttingDown = true;
        List<Exception>? failures = null;
        foreach (var window in _windows.ToArray())
        {
            if (ReferenceEquals(window, mainWindow)) continue;
            try { window.Close(); }
            catch (Exception ex) { (failures ??= []).Add(ex); }
        }
        if (failures is not null) throw new AggregateException("One or more secondary windows could not be closed.", failures);
    }

    /// <summary>Releases the registry reference after native closure has been allowed to complete.</summary>
    private void WindowClosed(object sender, WindowEventArgs args)
    {
        // Main-window interception is attached before registry registration, so handled close attempts remain live.
        if (args.Handled) return;
        var window = (Window)sender;
        window.Closed -= WindowClosed;
        _windows.Remove(window);
    }

    /// <summary>Fails clearly instead of accessing thread-affine native objects from a worker.</summary>
    private void VerifyAccess()
    {
        if (!dispatcher.HasThreadAccess) throw new InvalidOperationException("Window registration requires the UI dispatcher.");
    }
}

/// <summary>Applies a runtime theme to registered windows without owning configuration or persistence.</summary>
public sealed class WindowThemeService : IDisposable
{
    private readonly IWindowRegistry _windows;
    private readonly IUIThreadDispatcher _dispatcher;
    /// <summary>Subscribes to newly registered windows so later secondary windows receive the current theme.</summary>
    /// <param name="windows">The application's live-window registry.</param>
    /// <param name="dispatcher">The owning UI dispatcher.</param>
    public WindowThemeService(IWindowRegistry windows, IUIThreadDispatcher dispatcher)
    {
        _windows = windows;
        _dispatcher = dispatcher;
        _windows.WindowRegistered += Apply;
    }
    /// <summary>Gets the current theme; Default follows Windows settings.</summary>
    public ElementTheme Theme { get; private set; }
    /// <summary>Changes runtime appearance for all registered windows; the caller persists any preference separately.</summary>
    /// <param name="theme">The defined WinUI theme to apply.</param>
    public void SetTheme(ElementTheme theme)
    {
        if (!_dispatcher.HasThreadAccess) throw new InvalidOperationException("Theme changes require the UI dispatcher.");
        if (!Enum.IsDefined(theme)) throw new ArgumentOutOfRangeException(nameof(theme));
        Theme = theme;
        foreach (var window in _windows.Windows) Apply(window);
    }
    /// <summary>Applies the selected theme to a window's initialized root element.</summary>
    /// <param name="window">A window with an optional FrameworkElement root.</param>
    public void Apply(Window window)
    {
        if (!_dispatcher.HasThreadAccess) throw new InvalidOperationException("Theme changes require the UI dispatcher.");
        if (window.Content is FrameworkElement root) root.RequestedTheme = Theme;
    }

    /// <summary>Detaches registry notifications when the owning host is disposed.</summary>
    public void Dispose() => _windows.WindowRegistered -= Apply;
}