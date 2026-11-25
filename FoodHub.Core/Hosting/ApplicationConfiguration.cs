using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace FoodHub.Core.Hosting;

/// <summary>Resolves the consuming application's build configuration into a hosting environment.</summary>
/// <remarks>
/// Build configurations and hosting environments are separate .NET concepts. This desktop convention
/// maps Debug to Development and Release to Production; custom configurations retain their names.
/// It reads the consumer's assembly, so a separately compiled Core package cannot choose the app's environment.
/// No process environment variables are read or changed.
/// </remarks>
public static class DesktopEnvironment
{
    /// <summary>Reads the SDK-generated configuration attribute from the supplied assembly.</summary>
    /// <param name="applicationAssembly">The executable assembly, or the factory assembly when running EF tools.</param>
    /// <returns>The environment selected by the assembly's build configuration.</returns>
    /// <exception cref="InvalidOperationException">Configuration metadata is missing or cannot name a settings file.</exception>
    public static string Resolve(Assembly applicationAssembly)
    {
        ArgumentNullException.ThrowIfNull(applicationAssembly);
        var configuration = applicationAssembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration;
        if (string.IsNullOrWhiteSpace(configuration))
            throw new InvalidOperationException("The application assembly must include AssemblyConfigurationAttribute. Enable SDK assembly-info generation.");
        return FromConfiguration(configuration);
    }

    /// <summary>Maps a build configuration to a safe, conventional environment name.</summary>
    /// <param name="configuration">The MSBuild configuration, such as Debug, Release, or Staging.</param>
    /// <returns>Development, Production, or the unchanged custom name.</returns>
    /// <exception cref="InvalidOperationException">The name is empty or unsafe for a settings filename.</exception>
    public static string FromConfiguration(string configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration) || configuration.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || configuration.Contains('/') || configuration.Contains('\\') || configuration is "." or "..")
            throw new InvalidOperationException("The build configuration must be a valid environment filename component.");
        if (configuration.Equals("Debug", StringComparison.OrdinalIgnoreCase)) return Environments.Development;
        if (configuration.Equals("Release", StringComparison.OrdinalIgnoreCase)) return Environments.Production;
        return configuration;
    }
}

/// <summary>Central paths for desktop configuration and writable application data.</summary>
public sealed class ApplicationPaths
{
    /// <summary>Captures paths without creating directories or reading files.</summary>
    /// <param name="installDirectory">Content root; defaults to the executable's base directory.</param>
    /// <param name="userDirectory">Writable root; Windows adapters supply package LocalFolder when appropriate.</param>
    public ApplicationPaths(string? installDirectory = null, string? userDirectory = null)
    {
        InstallDirectory = Path.GetFullPath(installDirectory ?? AppContext.BaseDirectory);
        UserDirectory = Path.GetFullPath(userDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FoodHub"));
    }

    /// <summary>Gets the bundled content directory, independent of the working directory.</summary>
    public string InstallDirectory { get; }
    /// <summary>Gets the active per-Windows-user writable directory.</summary>
    public string UserDirectory { get; }
    /// <summary>Gets the general local overrides file, also containing the durable session-reset marker.</summary>
    public string SettingsFile => Path.Combine(UserDirectory, "appsettings.json");
}

/// <summary>Reports a safe loading failure without exposing configuration values or exception payloads.</summary>
/// <param name="Error">Null after a successful load; otherwise safe feedback for Settings.</param>
public sealed record ApplicationConfigurationStatus(string? Error);

/// <summary>Loads application-wide defaults and local overrides for desktop and command-line consumers.</summary>
/// <remarks>
/// Providers are added from lowest to highest priority: base JSON, environment JSON, then local JSON.
/// The returned configuration is a launch snapshot: no filesystem watchers or environment-variable providers
/// are installed. A malformed or inaccessible file returns an empty configuration and a safe diagnostic;
/// silently falling back could otherwise connect an account workflow to an unintended database.
/// </remarks>
public static class ApplicationConfiguration
{
    /// <summary>Loads the settings files without constructing a host or opening SQL.</summary>
    /// <param name="paths">Bundled and writable settings locations.</param>
    /// <param name="environment">The resolved host environment, used only to select bundled JSON.</param>
    /// <returns>A configuration root owned by the caller, and a safe error or null.</returns>
    public static (IConfigurationRoot Configuration, string? Error) Load(ApplicationPaths paths, string environment)
    {
        // Validate before interpolating the environment into a path. Do not remap an already resolved name.
        _ = DesktopEnvironment.FromConfiguration(environment);
        try
        {
            var configuration = new ConfigurationBuilder().SetBasePath(paths.InstallDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: false)
                .AddJsonFile(paths.SettingsFile, optional: true, reloadOnChange: false)
                .Build();
            return (configuration, null);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or FormatException or JsonException)
        {
            return (new ConfigurationBuilder().Build(), "Application settings could not be read. Repair or reset them in Settings, then restart FoodHub.");
        }
    }
}