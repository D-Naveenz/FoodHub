namespace Dhara.Extensions.WinUI.Storage;

/// <summary>Owns the Windows application-data context for the current Windows user.</summary>
/// <remarks>
/// Packaged processes use their real package identity. Unpackaged processes use an explicit publisher/product
/// pair through Windows App SDK; a console process needs runtime initialization but no XAML Application.
/// Disposing the context releases handles and never deletes stored data.
/// </remarks>
public sealed class ApplicationDataContext : IDisposable
{
    private readonly Microsoft.Windows.Storage.ApplicationData _data;
    /// <summary>Opens the native data context without creating a window or dispatcher.</summary>
    /// <param name="publisher">The unpackaged application's publisher name.</param>
    /// <param name="product">The unpackaged application's product name.</param>
    /// <param name="runtime">The process's actual package identity.</param>
    public ApplicationDataContext(string publisher, string product, IAppRuntimeEnvironment runtime)
    {
        ValidateName(publisher);
        ValidateName(product);
        _data = runtime.IsPackaged ? Microsoft.Windows.Storage.ApplicationData.GetDefault()
            : Microsoft.Windows.Storage.ApplicationData.GetForUnpackaged(publisher, product);
    }
    /// <summary>Gets the native local file directory; it is distinct from the host's content root.</summary>
    public string LocalPath => _data.LocalPath;
    /// <summary>Opens a named LocalSettings container, keeping unrelated application values separate.</summary>
    /// <param name="name">The container name; tests should use a unique generated name.</param>
    /// <returns>A container owned by the caller.</returns>
    public Microsoft.Windows.Storage.ApplicationDataContainer OpenSettings(string name) => _data.LocalSettings.CreateContainer(
        name, Microsoft.Windows.Storage.ApplicationDataCreateDisposition.Always);
    /// <summary>Deletes only a specified child container; intended for explicit reset/testing, never automatic startup.</summary>
    /// <param name="name">The exact child container to remove.</param>
    public void DeleteSettings(string name) => _data.LocalSettings.DeleteContainer(name);
    /// <summary>Validates publisher/product names before they are used by the native unpackaged data store.</summary>
    internal static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Use a nonempty publisher or product name without path components.", nameof(name));
    }
    /// <inheritdoc/>
    public void Dispose() => _data.Dispose();
}