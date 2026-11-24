using FoodHub.Core.Services;
using System.IO;
using Windows.Storage;
using Windows.System;

namespace FoodHub.Services;

/// <summary>Chooses the real Windows-user storage root without leaking Windows types into Core.</summary>
public static class WindowsLocalStorage
{
    /// <summary>Uses package LocalFolder only when package identity is present; access failures propagate.</summary>
    public static ILocalStorageService Create(IAppRuntimeEnvironment runtime)
    {
        var root = runtime.IsPackaged ? ApplicationData.Current.LocalFolder.Path : new ApplicationPaths().UserDirectory;
        return new LocalStorageService(root);
    }
}

/// <summary>Opens the active storage directory without exposing arbitrary shell commands.</summary>
public interface ILocalStorageFolderLauncher
{
    /// <summary>Returns whether Windows successfully opened the folder.</summary>
    Task<bool> OpenAsync(CancellationToken cancellation = default);
}

/// <summary>Launches File Explorer for the storage instance used by the running application.</summary>
public sealed class LocalStorageFolderLauncher(ILocalStorageService storage) : ILocalStorageFolderLauncher
{
    /// <inheritdoc/>
    public async Task<bool> OpenAsync(CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        Directory.CreateDirectory(storage.RootDirectory);
        var folder = await StorageFolder.GetFolderFromPathAsync(storage.RootDirectory).AsTask(cancellation);
        return await Launcher.LaunchFolderAsync(folder).AsTask(cancellation);
    }
}