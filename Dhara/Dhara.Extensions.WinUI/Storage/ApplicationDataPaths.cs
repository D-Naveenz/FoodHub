using Windows.System;

namespace Dhara.Extensions.WinUI.Storage;

/// <summary>Resolves bundled content and writable application data without depending on an application's services.</summary>
/// <remarks>
/// Package identity, not build configuration, selects LocalFolder. Unpackaged applications use a named folder
/// in LocalApplicationData/publisher/product. Resolving paths opens the native data context without creating application files or granting package identity.
/// A caller can override roots for isolated tests, portable deployments, or another deliberate storage policy.
/// </remarks>
public sealed class ApplicationDataPaths
{
    /// <summary>Captures the storage locations for one application instance.</summary>
    /// <param name="publisherName">The explicit unpackaged publisher identity.</param>
    /// <param name="applicationDataName">One directory name, unique to the consuming application.</param>
    /// <param name="runtime">The actual process package identity.</param>
    /// <param name="contentRoot">Optional bundled-content root; defaults to the executable directory.</param>
    /// <param name="localRoot">Optional explicit writable root, primarily useful for isolated tests.</param>
    /// <exception cref="ArgumentException">The application data name is empty or contains path components.</exception>
    public ApplicationDataPaths(string publisherName, string applicationDataName, IAppRuntimeEnvironment runtime,
        string? contentRoot = null, string? localRoot = null)
    {
        ApplicationDataContext.ValidateName(publisherName);
        ApplicationDataContext.ValidateName(applicationDataName);
        ContentRoot = Path.GetFullPath(contentRoot ?? AppContext.BaseDirectory);
        if (localRoot is not null) RootDirectory = Path.GetFullPath(localRoot);
        else
        {
            using var context = new ApplicationDataContext(publisherName, applicationDataName, runtime);
            RootDirectory = Path.GetFullPath(context.LocalPath);
        }
    }

    /// <summary>Gets the absolute bundled-content directory, independent of the working directory.</summary>
    public string ContentRoot { get; }
    /// <summary>Gets the absolute writable root for this process and Windows user.</summary>
    public string RootDirectory { get; }
}

/// <summary>Opens the configured writable application directory using Windows Explorer.</summary>
public interface ILocalStorageFolderLauncher
{
    /// <summary>Creates the root if necessary and asks Windows to display it.</summary>
    /// <param name="cancellation">Cancels before launch or while resolving the folder.</param>
    /// <returns>True when Windows accepted the launch request.</returns>
    Task<bool> OpenAsync(CancellationToken cancellation = default);
}

/// <summary>Launches the active root, without exposing arbitrary shell commands.</summary>
/// <param name="paths">The same paths used by the consuming application's storage adapter.</param>
public sealed class LocalStorageFolderLauncher(ApplicationDataPaths paths) : ILocalStorageFolderLauncher
{
    /// <inheritdoc/>
    public async Task<bool> OpenAsync(CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        Directory.CreateDirectory(paths.RootDirectory);
        var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(paths.RootDirectory).AsTask(cancellation);
        return await Launcher.LaunchFolderAsync(folder).AsTask(cancellation);
    }
}