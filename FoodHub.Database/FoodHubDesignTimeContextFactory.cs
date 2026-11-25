using FoodHub.Core.Models;
using FoodHub.Core.Services;
using Microsoft.EntityFrameworkCore.Design;

namespace FoodHub.Database;

/// <summary>Creates EF design-time contexts in the executable that owns Windows runtime initialization and defaults.</summary>
/// <remarks>Use FoodHub.Database as the EF startup project. No WinUI Application or message loop is constructed.</remarks>
public sealed class FoodHubDesignTimeContextFactory : IDesignTimeDbContextFactory<FoodHub_DevContext>
{
    /// <summary>Loads host defaults and native local overrides using this executable's build metadata.</summary>
    /// <param name="args">EF arguments; runtime setting changes are not accepted as command-line overrides.</param>
    /// <returns>A short-lived context owned by EF tooling; no SQL connection is opened here.</returns>
    public FoodHub_DevContext CreateDbContext(string[] args)
    {
        // EF runs inside its own executable, so its native manifest and resource directory belong
        // to the tool. Use our SDK-generated manifest and self-contained resources only while reading
        // settings. This work is synchronous: activation contexts must stay on their creating thread.
        var assembly = typeof(FoodHubDesignTimeContextFactory).Assembly;
        var directory = Path.GetDirectoryName(assembly.Location)!;
        var previousDirectory = Environment.CurrentDirectory;
        const string baseDirectoryVariable = "MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY";
        const string processVariable = "MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY_PID";
        var previousBase = Environment.GetEnvironmentVariable(baseDirectoryVariable);
        var previousProcess = Environment.GetEnvironmentVariable(processVariable);
        try
        {
            Environment.SetEnvironmentVariable(baseDirectoryVariable, directory + Path.DirectorySeparatorChar);
            Environment.SetEnvironmentVariable(processVariable,
                Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            WinRT.ComWrappersSupport.InitializeComWrappers();
            using var activation = new DesignTimeActivationContext(Path.Combine(directory, "FoodHub.Database.exe"));
            // Native resource lookups also need the startup output as the current directory in the EF process.
            Environment.CurrentDirectory = directory;
            return DatabaseConfiguration.LoadSettings(assembly, directory).Open();
        }
        finally
        {
            Environment.CurrentDirectory = previousDirectory;
            Environment.SetEnvironmentVariable(baseDirectoryVariable, previousBase);
            Environment.SetEnvironmentVariable(processVariable, previousProcess);
        }
    }
}