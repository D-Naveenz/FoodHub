using System.Text;

namespace FoodHub.Core.Services;

/// <summary>Persists small application files independently of FoodHub accounts and Windows UI APIs.</summary>
public interface ILocalStorageService
{
    /// <summary>The active packaged or unpackaged writable root.</summary>
    string RootDirectory { get; }
    /// <summary>Resolves one application-owned filename inside the storage root.</summary>
    string GetFilePath(string fileName);
    /// <summary>Reads a file, or returns null when it has not been created.</summary>
    Task<byte[]?> ReadAsync(string fileName, CancellationToken cancellation = default);
    /// <summary>Atomically replaces a file after all bytes have been written.</summary>
    Task WriteAsync(string fileName, ReadOnlyMemory<byte> bytes, CancellationToken cancellation = default);
    /// <summary>Deletes a file if present.</summary>
    Task DeleteAsync(string fileName, CancellationToken cancellation = default);
}

/// <summary>A filesystem implementation usable by desktop services, EF tools, and isolated tests.</summary>
public sealed class LocalStorageService : ILocalStorageService
{
    /// <summary>Captures a root without performing I/O or depending on the current working directory.</summary>
    public LocalStorageService(string rootDirectory) => RootDirectory = Path.GetFullPath(rootDirectory);
    /// <inheritdoc/>
    public string RootDirectory { get; }
    /// <inheritdoc/>
    public string GetFilePath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName is "." or ".." || Path.GetFileName(fileName) != fileName
            || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Use one application filename, without directory components.", nameof(fileName));
        return Path.Combine(RootDirectory, fileName);
    }
    /// <inheritdoc/>
    public async Task<byte[]?> ReadAsync(string fileName, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        try { return await File.ReadAllBytesAsync(GetFilePath(fileName), cancellation); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
    /// <inheritdoc/>
    public async Task WriteAsync(string fileName, ReadOnlyMemory<byte> bytes, CancellationToken cancellation = default)
    {
        var path = GetFilePath(fileName);
        cancellation.ThrowIfCancellationRequested();
        Directory.CreateDirectory(RootDirectory);
        // Unique temporary names avoid collisions between different writers; replacement stays on the same volume.
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, cancellation);
            cancellation.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    /// <inheritdoc/>
    public Task DeleteAsync(string fileName, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        try { File.Delete(GetFilePath(fileName)); }
        catch (DirectoryNotFoundException) { /* Deleting an absent file is an idempotent operation. */ }
        return Task.CompletedTask;
    }
}

/// <summary>Imports only legacy settings once; remembered authentication is never copied into packaged storage.</summary>
public static class LegacySettingsMigration
{
    /// <summary>Copies missing settings before configuration is built; failures leave migration retryable.</summary>
    public static void Import(string legacyDirectory, ILocalStorageService destination)
    {
        if (string.Equals(Path.GetFullPath(legacyDirectory), destination.RootDirectory, StringComparison.OrdinalIgnoreCase)) return;
        var marker = destination.GetFilePath("settings-migrated.v1");
        if (File.Exists(marker)) return;
        Directory.CreateDirectory(destination.RootDirectory);
        foreach (var fileName in new[] { "appsettings.json", "theme.txt" })
        {
            var source = Path.Combine(legacyDirectory, fileName);
            var target = destination.GetFilePath(fileName);
            if (!File.Exists(source) || File.Exists(target)) continue;
            var temporary = target + ".migration.tmp";
            try
            {
                File.Copy(source, temporary, overwrite: true);
                File.Move(temporary, target, overwrite: false);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        // A marker prevents deleted/reset settings from being re-imported on later launches.
        var markerTemporary = marker + ".tmp";
        try
        {
            File.WriteAllText(markerTemporary, "Settings imported; authentication deliberately excluded.", Encoding.UTF8);
            File.Move(markerTemporary, marker, overwrite: true);
        }
        finally { if (File.Exists(markerTemporary)) File.Delete(markerTemporary); }
    }
}