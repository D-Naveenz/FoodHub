using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

namespace FoodHub.Core.Services;

/// <summary>Coordinates all read-modify-write operations on the general local settings file.</summary>
/// <remarks>
/// Register one instance per application host and share it between settings features. The semaphore prevents
/// an appearance save and a database save from overwriting each other's changes in this process. Storage
/// writes a temporary file and atomically replaces the destination. This is not a cross-process lock.
/// Updating the file does not reload the host configuration; each feature controls when its values apply.
/// </remarks>
/// <param name="storage">The application's active package-aware or unpackaged storage adapter.</param>
public sealed class LocalSettingsStore(ILocalStorageService storage) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>Persists a synchronous edit while preserving unrelated JSON properties.</summary>
    /// <param name="update">Edits the latest local JSON object while holding the shared writer gate.</param>
    /// <param name="repairMalformed">True only for an explicit repair action that may replace malformed content.</param>
    /// <param name="cancellation">Cancels waiting or storage I/O before replacement.</param>
    /// <returns>A task completing after the edited object has been atomically persisted.</returns>
    /// <exception cref="IOException">Existing JSON is malformed and repair was not explicitly requested, or storage fails.</exception>
    /// <exception cref="UnauthorizedAccessException">The writable directory is inaccessible.</exception>
    /// <exception cref="OperationCanceledException">The operation was cancelled.</exception>
    public async Task UpdateAsync(Action<JsonObject> update, bool repairMalformed = false, CancellationToken cancellation = default)
    {
        await _gate.WaitAsync(cancellation);
        try
        {
            var bytes = await storage.ReadAsync("appsettings.json", cancellation);
            var json = new JsonObject(new JsonNodeOptions { PropertyNameCaseInsensitive = true });
            if (bytes is not null)
            {
                try
                {
                    // Use the same validation rules as the configuration provider, including duplicate keys.
                    // Otherwise valid JSON with conflicting keys could load badly yet resist explicit repair.
                    using var content = new MemoryStream(bytes);
                    using var validation = new ConfigurationBuilder().AddJsonStream(content).Build() as IDisposable;
                    json = JsonNode.Parse(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'),
                        new JsonNodeOptions { PropertyNameCaseInsensitive = true }, DocumentOptions) as JsonObject
                        ?? throw new JsonException();
                }
                catch (Exception ex) when (ex is JsonException or FormatException)
                {
                    if (!repairMalformed) throw new IOException("Application settings are malformed. Repair or reset them in Settings first.");
                    // Only explicit database Save/Reset is allowed to discard an unreadable document.
                }
            }
            update(json);
            await storage.WriteAsync("appsettings.json", Encoding.UTF8.GetBytes(
                json.ToJsonString(new JsonSerializerOptions { WriteIndented = true })), cancellation);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Releases the writer gate when the owning host is disposed.</summary>
    public void Dispose() => _gate.Dispose();
}