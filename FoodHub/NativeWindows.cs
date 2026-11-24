using System.Runtime.InteropServices;

namespace FoodHub;

/// <summary>Minimal Win32 interop needed for owned modal windows in WinUI.</summary>
internal static class NativeWindows
{
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr64(nint hwnd, int index, nint value);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(nint hwnd, int index, int value);
    /// <summary>Sets the native owner, keeping the child above its main window.</summary>
    internal static void SetOwner(nint child, nint owner)
    {
        if (nint.Size == 8) SetWindowLongPtr64(child, -8, owner);
        else SetWindowLong32(child, -8, owner.ToInt32());
    }
    /// <summary>Enables/disables native input to the owner, including its title bar.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnableWindow(nint hwnd, [MarshalAs(UnmanagedType.Bool)] bool enabled);
    /// <summary>Gets physical DPI for sizing in effective pixels.</summary>
    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(nint hwnd);
}
