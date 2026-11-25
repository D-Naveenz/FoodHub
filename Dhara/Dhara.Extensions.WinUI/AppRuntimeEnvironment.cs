using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting;

namespace Dhara.Extensions.WinUI;

/// <summary>Reports deployment identity separately from build configuration and host environment.</summary>
public interface IAppRuntimeEnvironment
{
    /// <summary>True when this process has Windows package identity.</summary>
    bool IsPackaged { get; }
}

/// <summary>Queries Windows package identity without calling package-only storage APIs.</summary>
public sealed class AppRuntimeEnvironment : IAppRuntimeEnvironment
{
    /// <summary>Captures the process identity; unexpected native errors remain visible.</summary>
    public AppRuntimeEnvironment() => IsPackaged = DetectPackageIdentity();
    /// <inheritdoc/>
    public bool IsPackaged { get; }
    /// <summary>A missing buffer indicates identity exists; NO_PACKAGE means unpackaged.</summary>
    internal static bool DetectPackageIdentity()
    {
        uint length = 0;
        var result = GetCurrentPackageFullName(ref length, IntPtr.Zero);
        return result switch
        {
            122 => true, // ERROR_INSUFFICIENT_BUFFER: Windows has a package name to return.
            15700 => false, // APPMODEL_ERROR_NO_PACKAGE.
            _ => throw new Win32Exception(result)
        };
    }
    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, IntPtr packageFullName);
}

/// <summary>Convenience helpers for callers already holding the Generic Host environment.</summary>
public static class HostEnvironmentExtensions
{
    /// <summary>Reports process package identity, independently of Development or Production.</summary>
    public static bool IsPackaged(this IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return AppRuntimeEnvironment.DetectPackageIdentity();
    }
}