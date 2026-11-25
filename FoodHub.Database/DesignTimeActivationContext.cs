using System.ComponentModel;
using System.Runtime.InteropServices;

namespace FoodHub.Database;

/// <summary>Temporarily activates the startup executable's generated native manifest inside EF's tool process.</summary>
/// <remarks>
/// The Windows App SDK generates WinRT registrations in the executable manifest. EF loads the startup
/// assembly into its own process, whose manifest lacks those registrations. This synchronous, thread-bound
/// scope uses the standard Win32 activation-context API; no XAML application or message loop is involved.
/// Dispose on the creating thread before returning to EF. Normal executable startup needs no such scope.
/// </remarks>
/// <seealso href="https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-createactctxw"/>
internal sealed class DesignTimeActivationContext : IDisposable
{
    private readonly nint _handle;
    private readonly nuint _cookie;
    private readonly bool _uninitialize;

    /// <summary>Loads resource 1 (the application manifest) from the self-contained startup executable.</summary>
    /// <param name="executable">Absolute path to the build output's application executable.</param>
    /// <exception cref="Win32Exception">The manifest cannot be loaded or activated.</exception>
    public DesignTimeActivationContext(string executable)
    {
        var result = RoInitialize(1); // EF does not construct a WinUI STA; storage supports MTA callers.
        _uninitialize = result >= 0;
        if (result < 0 && result != unchecked((int)0x80010106)) Marshal.ThrowExceptionForHR(result);
        var description = new ActivationContext
        {
            Size = Marshal.SizeOf<ActivationContext>(),
            Flags = 0x008 | 0x004, // Resource name and assembly directory are supplied explicitly.
            Source = executable,
            AssemblyDirectory = Path.GetDirectoryName(executable),
            ResourceName = 1
        };
        _handle = CreateActCtx(ref description);
        if (_handle == -1)
        {
            var error = Marshal.GetLastWin32Error();
            if (_uninitialize) RoUninitialize();
            throw new Win32Exception(error);
        }
        if (!ActivateActCtx(_handle, out _cookie))
        {
            var error = Marshal.GetLastWin32Error();
            ReleaseActCtx(_handle);
            if (_uninitialize) RoUninitialize();
            throw new Win32Exception(error);
        }
    }

    /// <summary>Restores the tool thread's previous context and releases the manifest handle.</summary>
    public void Dispose()
    {
        DeactivateActCtx(0, _cookie);
        ReleaseActCtx(_handle);
        if (_uninitialize) RoUninitialize();
    }

    /// <summary>Matches Win32 ACTCTXW; Windows reads only the fields selected by Flags.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ActivationContext
    {
        public int Size;
        public uint Flags;
        public string Source;
        public ushort ProcessorArchitecture;
        public ushort LanguageId;
        public string? AssemblyDirectory;
        public nint ResourceName;
        public string? ApplicationName;
        public nint Module;
    }

    [DllImport("combase.dll")]
    private static extern int RoInitialize(uint initializationType);
    [DllImport("combase.dll")]
    private static extern void RoUninitialize();
    [DllImport("kernel32.dll", EntryPoint = "CreateActCtxW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateActCtx(ref ActivationContext context);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ActivateActCtx(nint context, out nuint cookie);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeactivateActCtx(uint flags, nuint cookie);
    [DllImport("kernel32.dll")]
    private static extern void ReleaseActCtx(nint context);
}