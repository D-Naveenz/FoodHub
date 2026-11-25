using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace Dhara.Extensions.WinUI.Windowing;

/// <summary>Provides native ownership, sizing and WinRT picker interoperability for initialized WinUI windows.</summary>
/// <remarks>Call these methods on the window's UI thread. They do not implement application-specific modality or navigation.</remarks>
public static class WindowExtensions
{
    /// <summary>Gets the native window handle used by Windows interoperability APIs.</summary>
    /// <param name="window">The initialized WinUI window.</param>
    /// <returns>The native HWND.</returns>
    public static nint GetWindowHandle(this Window window) => WinRT.Interop.WindowNative.GetWindowHandle(window);

    /// <summary>Assigns a native owner, keeping the child above its owner without disabling owner input.</summary>
    /// <param name="window">The child window.</param>
    /// <param name="owner">The owner window.</param>
    /// <exception cref="Win32Exception">Windows rejected the ownership change.</exception>
    public static void SetOwner(this Window window, Window owner)
    {
        Marshal.SetLastPInvokeError(0);
        var previous = nint.Size == 8 ? Native.SetWindowLongPtr64(window.GetWindowHandle(), -8, owner.GetWindowHandle())
            : new nint(Native.SetWindowLong32(window.GetWindowHandle(), -8, owner.GetWindowHandle().ToInt32()));
        // A zero return can also mean the previous owner was null; only a nonzero error indicates failure.
        if (previous == 0 && Marshal.GetLastPInvokeError() != 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    /// <summary>Enables or disables native input, including the title bar.</summary>
    /// <param name="window">The window whose input state changes.</param>
    /// <param name="enabled">Whether native input should be accepted.</param>
    public static void SetInputEnabled(this Window window, bool enabled) => Native.EnableWindow(window.GetWindowHandle(), enabled);

    /// <summary>Centers a window over its owner at an effective-pixel size, constrained to the owner's monitor work area.</summary>
    /// <param name="window">The initialized child window.</param>
    /// <param name="owner">The owner whose position and monitor determine placement.</param>
    /// <param name="width">Requested width in effective pixels.</param>
    /// <param name="height">Requested height in effective pixels.</param>
    /// <exception cref="ArgumentOutOfRangeException">A requested size is nonpositive or not finite.</exception>
    public static void CenterOnOwner(this Window window, Window owner, double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Window dimensions must be positive and finite.");
        var area = DisplayArea.GetFromWindowId(owner.AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var dpi = Native.GetDpiForWindow(owner.GetWindowHandle());
        if (dpi == 0) throw new Win32Exception("The owner's window DPI could not be determined.");
        var scale = dpi / 96.0;
        var actualWidth = (int)Math.Clamp(width * scale, 1, area.Width);
        var actualHeight = (int)Math.Clamp(height * scale, 1, area.Height);
        var position = owner.AppWindow.Position;
        var size = owner.AppWindow.Size;
        window.AppWindow.MoveAndResize(new RectInt32(
            Math.Clamp(position.X + (size.Width - actualWidth) / 2, area.X, area.X + area.Width - actualWidth),
            Math.Clamp(position.Y + (size.Height - actualHeight) / 2, area.Y, area.Y + area.Height - actualHeight), actualWidth, actualHeight));
    }

    /// <summary>Initializes a WinRT picker or other IInitializeWithWindow object with a desktop owner.</summary>
    /// <param name="window">The native owner for the WinRT UI object.</param>
    /// <param name="uiObject">A WinRT object supporting IInitializeWithWindow, such as FileOpenPicker.</param>
    public static void InitializeOwnedUI(this Window window, object uiObject) =>
        WinRT.Interop.InitializeWithWindow.Initialize(uiObject, window.GetWindowHandle());

    /// <summary>Preserves the architecture-specific Win32 calls used by owned modal windows.</summary>
    private static class Native
    {
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        internal static extern nint SetWindowLongPtr64(nint hwnd, int index, nint value);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        internal static extern int SetWindowLong32(nint hwnd, int index, int value);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnableWindow(nint hwnd, [MarshalAs(UnmanagedType.Bool)] bool enabled);
        [DllImport("user32.dll")]
        internal static extern uint GetDpiForWindow(nint hwnd);
    }
}