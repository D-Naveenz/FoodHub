using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Dhara.Extensions.WinUI.Hosting;

/// <summary>Resolves the consuming application's build configuration into a hosting environment.</summary>
/// <remarks>
/// Build configurations and hosting environments are separate .NET concepts. This desktop convention
/// maps Debug to Development and Release to Production; custom configurations retain their names.
/// It reads the consumer's assembly, so a separately compiled library cannot choose the app's environment.
/// No process environment variables are read or changed.
/// </remarks>
public static class ApplicationEnvironment
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

/// <summary>Reports a safe loading failure without exposing configuration values or exception payloads.</summary>
/// <param name="Error">Null after a successful load; otherwise safe feedback for Settings.</param>
public sealed record ApplicationConfigurationStatus(string? Error);

/// <summary>Loads host-owned JSON defaults for desktop and command-line consumers.</summary>
/// <remarks>
/// Providers are added from lowest to highest priority: base JSON, then environment JSON. Runtime preferences belong to LocalSettings and never enter this root.
/// File watching is opt-in; environment-variable providers are never installed. This service only reads files and exposes no persistence API. A malformed or inaccessible file returns an empty configuration and a safe diagnostic;
/// silently falling back could otherwise direct a workflow to an unintended service. The returned root owns
/// provider resources and watchers and must be disposed by its host or caller. A successful initial status does
/// not describe later reload events; consumers choose whether and when to refresh their own snapshots.
/// </remarks>
/// <seealso href="https://learn.microsoft.com/en-us/dotnet/core/extensions/configuration-providers"/>
public static class ApplicationConfiguration
{
    /// <summary>Loads the settings files without constructing a host or opening SQL.</summary>
    /// <param name="contentRoot">The executable content directory, independent of the working directory.</param>
    /// <param name="environment">The resolved host environment, used only to select bundled JSON.</param>
    /// <param name="reloadOnChange">Whether framework JSON providers watch external changes; defaults to false.</param>
    /// <returns>A configuration root owned by the caller, and a safe error or null.</returns>
    public static (IConfigurationRoot Configuration, string? Error) Load(string contentRoot, string environment, bool reloadOnChange = false)
    {
        // Validate before interpolating the environment into a path. Do not remap an already resolved name.
        _ = ApplicationEnvironment.FromConfiguration(environment);
        try
        {
            var configuration = new ConfigurationBuilder().SetBasePath(contentRoot)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: reloadOnChange)
                .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: reloadOnChange)
                .Build();
            return (configuration, null);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or FormatException or JsonException)
        {
            return (new ConfigurationBuilder().Build(), "Host configuration could not be read. Correct the application configuration files externally, then restart.");
        }
    }
}