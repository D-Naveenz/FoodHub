using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Dhara.Extensions.WinUI.Storage;

/// <summary>Reports invalid saved value types separately from native storage access failures.</summary>
/// <param name="message">Safe feedback that contains no saved payload.</param>
public sealed class LocalSettingsValueException(string message) : IOException(message);

/// <summary>Provides the small atomic value boundary used by a local settings store.</summary>
/// <remarks>
/// A group is represented as an IReadOnlyDictionary of native setting values. Implementations must read or
/// replace the whole group as one value. Applications normally use WindowsLocalSettingsContainer; isolated
/// logic tests can supply an in-memory container without accessing the real user's settings.
/// </remarks>
public interface ILocalSettingsContainer
{
    /// <summary>Reads one value, returning null when no value exists.</summary>
    /// <param name="key">The setting key.</param>
    /// <returns>A native scalar or a read-only dictionary representing a group.</returns>
    object? Read(string key);
    /// <summary>Atomically replaces one scalar or complete group.</summary>
    /// <param name="key">The setting key.</param>
    /// <param name="value">A supported native value or a read-only group dictionary.</param>
    void Write(string key, object value);
    /// <summary>Removes one value without changing unrelated settings.</summary>
    /// <param name="key">The setting key.</param>
    void Remove(string key);
}

/// <summary>Persists small runtime preferences independently of read-only host configuration files.</summary>
public interface ILocalSettingsStore
{
    /// <summary>Reads a value with a caller-supplied fallback for an absent setting.</summary>
    /// <typeparam name="T">The expected native setting type.</typeparam>
    /// <param name="key">The setting key.</param>
    /// <param name="fallback">The application/configuration default used only when the key is absent.</param>
    /// <returns>The saved value or fallback.</returns>
    /// <exception cref="LocalSettingsValueException">A present value has an incompatible type.</exception>
    T Read<T>(string key, T fallback);
    /// <summary>Reads an atomic group, returning an empty dictionary if it is absent.</summary>
    /// <param name="key">The group key.</param>
    /// <returns>A detached snapshot that cannot modify stored values.</returns>
    IReadOnlyDictionary<string, object> ReadGroup(string key);
    /// <summary>Persists one supported native setting value.</summary>
    /// <typeparam name="T">The native value type; null is represented by removing the key instead.</typeparam>
    /// <param name="key">The setting key.</param>
    /// <param name="value">The value to save.</param>
    /// <param name="cancellation">Cancels before the synchronous native commit starts.</param>
    /// <returns>A task completing after the native write succeeds.</returns>
    Task WriteAsync<T>(string key, T value, CancellationToken cancellation = default) where T : notnull;
    /// <summary>Removes one override, allowing the caller to use its configuration fallback.</summary>
    /// <param name="key">The setting key.</param>
    /// <param name="cancellation">Cancels before the native removal starts.</param>
    /// <returns>A task completing after removal.</returns>
    Task RemoveAsync(string key, CancellationToken cancellation = default);
    /// <summary>Reads, edits and atomically replaces a related group while serializing this process's writers.</summary>
    /// <param name="key">The group key.</param>
    /// <param name="update">A synchronous edit to a detached dictionary; remove entries to remove group values.</param>
    /// <param name="repairInvalid">Allows an explicit repair action to replace a value with an invalid group shape.</param>
    /// <param name="cancellation">Cancels waiting or the operation before commit.</param>
    /// <returns>A task completing after the edited group is committed.</returns>
    Task UpdateGroupAsync(string key, Action<IDictionary<string, object>> update, bool repairInvalid = false, CancellationToken cancellation = default);
}

/// <summary>Serializes local preference writes without ever writing or reloading appsettings files.</summary>
/// <param name="container">The native value boundary, or an isolated test implementation.</param>
/// <remarks>
/// Register one store per application. Individual native values/groups are atomic; the semaphore coordinates
/// read-modify-write operations within this process, not competing processes. Reads return detached snapshots.
/// Native setting types and size limits apply. Cancellation is checked before commit; a synchronous native
/// write already in progress cannot be interrupted. The caller owns the container's lifetime separately.
/// </remarks>
public sealed class LocalSettingsStore(ILocalSettingsContainer container) : ILocalSettingsStore, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    /// <inheritdoc/>
    public T Read<T>(string key, T fallback)
    {
        var value = container.Read(key);
        if (value is null) return fallback;
        if (value is T typed) return typed;
        throw new LocalSettingsValueException("A saved local setting has an invalid type.");
    }
    /// <inheritdoc/>
    public IReadOnlyDictionary<string, object> ReadGroup(string key)
    {
        var value = container.Read(key);
        if (value is null) return new ReadOnlyDictionary<string, object>(new Dictionary<string, object>());
        if (value is IReadOnlyDictionary<string, object> group)
            return new ReadOnlyDictionary<string, object>(new Dictionary<string, object>(group));
        throw new LocalSettingsValueException("A saved local settings group has an invalid type.");
    }
    /// <inheritdoc/>
    public Task WriteAsync<T>(string key, T value, CancellationToken cancellation = default) where T : notnull =>
        CommitAsync(() => container.Write(key, value), cancellation);
    /// <inheritdoc/>
    public Task RemoveAsync(string key, CancellationToken cancellation = default) => CommitAsync(() => container.Remove(key), cancellation);
    /// <inheritdoc/>
    public Task UpdateGroupAsync(string key, Action<IDictionary<string, object>> update, bool repairInvalid = false, CancellationToken cancellation = default) =>
        CommitAsync(() =>
        {
            Dictionary<string, object> group;
            try { group = new(ReadGroup(key)); }
            catch (LocalSettingsValueException) when (repairInvalid) { group = []; }
            update(group);
            cancellation.ThrowIfCancellationRequested();
            container.Write(key, new ReadOnlyDictionary<string, object>(group));
        }, cancellation);

    /// <summary>Holds the writer gate only across a short native value operation; no asynchronous UI work runs inside it.</summary>
    private async Task CommitAsync(Action commit, CancellationToken cancellation)
    {
        await _gate.WaitAsync(cancellation);
        try { cancellation.ThrowIfCancellationRequested(); commit(); }
        finally { _gate.Release(); }
    }
    /// <summary>Releases the process-local writer gate when the owning host is disposed.</summary>
    public void Dispose() => _gate.Dispose();
}

/// <summary>Adapts Windows LocalSettings to scalar values and atomically stored typed groups.</summary>
/// <remarks>
/// Groups are encoded as one native string value because Windows App SDK containers do not accept the older
/// Windows.Storage.ApplicationDataCompositeValue type. No settings file is created. Group members support
/// strings, Boolean, numeric primitives, Char, Guid, DateTimeOffset and TimeSpan, plus arrays of those types.
/// Native value size limits still apply. The adapter owns and disposes its lazily opened container.
/// </remarks>
public sealed class WindowsLocalSettingsContainer : ILocalSettingsContainer, IDisposable
{
    private const string GroupPrefix = "\u001eDhara.Group/1:";
    private const string StringPrefix = "\u001eDhara.String/1:";
    private readonly Lazy<Microsoft.Windows.Storage.ApplicationDataContainer> _container;
    private static readonly IReadOnlyDictionary<string, Type> GroupTypes = CreateGroupTypes();

    /// <summary>Wraps an already opened container and takes ownership of it.</summary>
    /// <param name="container">An application-owned or uniquely named test container.</param>
    public WindowsLocalSettingsContainer(Microsoft.Windows.Storage.ApplicationDataContainer container)
        : this(() => container) { _ = _container.Value; }

    /// <summary>Defers opening native settings until an operation can report access failures safely.</summary>
    /// <param name="openContainer">Opens a container whose ownership transfers to this adapter.</param>
    public WindowsLocalSettingsContainer(Func<Microsoft.Windows.Storage.ApplicationDataContainer> openContainer)
    {
        ArgumentNullException.ThrowIfNull(openContainer);
        _container = new(openContainer);
    }

    /// <inheritdoc/>
    public object? Read(string key) => Access(() =>
    {
        if (!_container.Value.Values.TryGetValue(key, out var value)) return null;
        if (value is not string text) return value;
        // Escaping makes every caller-supplied string round-trip, including our internal format markers.
        if (text.StartsWith(StringPrefix, StringComparison.Ordinal)) return text[StringPrefix.Length..];
        if (!text.StartsWith(GroupPrefix, StringComparison.Ordinal)) return text;
        return (object)DecodeGroup(text[GroupPrefix.Length..]);
    });

    /// <inheritdoc/>
    public void Write(string key, object value) => Access<object?>(() =>
    {
        object stored = value;
        if (value is IReadOnlyDictionary<string, object> group)
        {
            var encoded = new Dictionary<string, GroupValue>();
            foreach (var item in group)
            {
                var type = item.Value?.GetType();
                if (type is null || !GroupTypes.ContainsKey(type.Name))
                    throw new ArgumentException("A local settings group contains an unsupported value type.");
                encoded.Add(item.Key, new(type.Name, JsonSerializer.SerializeToElement(item.Value, type)));
            }
            stored = GroupPrefix + JsonSerializer.Serialize(encoded);
        }
        else if (value is string text && (text.StartsWith(GroupPrefix, StringComparison.Ordinal) || text.StartsWith(StringPrefix, StringComparison.Ordinal)))
            stored = StringPrefix + text;
        // One native assignment commits the complete group only after serialization has succeeded.
        _container.Value.Values[key] = stored;
        return null;
    });

    /// <inheritdoc/>
    public void Remove(string key) => Access<object?>(() => { _container.Value.Values.Remove(key); return null; });

    /// <summary>Decodes only known value types; saved data can never select arbitrary CLR types.</summary>
    private static IReadOnlyDictionary<string, object> DecodeGroup(string payload)
    {
        try
        {
            var encoded = JsonSerializer.Deserialize<Dictionary<string, GroupValue>>(payload)
                ?? throw new JsonException();
            var group = new Dictionary<string, object>();
            foreach (var item in encoded)
            {
                if (item.Value is null || !GroupTypes.TryGetValue(item.Value.Type, out var type)) throw new JsonException();
                group.Add(item.Key, item.Value.Value.Deserialize(type) ?? throw new JsonException());
            }
            return new ReadOnlyDictionary<string, object>(group);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
        { throw new LocalSettingsValueException("A saved local settings group has an invalid format."); }
    }

    /// <summary>Defines a closed, stable set of supported group types, including their one-dimensional arrays.</summary>
    private static IReadOnlyDictionary<string, Type> CreateGroupTypes()
    {
        Type[] scalarTypes = [typeof(string), typeof(bool), typeof(byte), typeof(sbyte), typeof(short), typeof(ushort),
            typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(char),
            typeof(Guid), typeof(DateTimeOffset), typeof(TimeSpan)];
        return scalarTypes.Concat(scalarTypes.Select(type => type.MakeArrayType())).ToDictionary(type => type.Name);
    }

    /// <summary>Stores explicit type information so JSON numbers retain their original native types.</summary>
    private sealed record GroupValue(string Type, JsonElement Value);

    /// <summary>Reports native failures without exposing stored values or platform exception payloads.</summary>
    private static T Access<T>(Func<T> action)
    {
        try { return action(); }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or ArgumentException or InvalidCastException)
        { throw new IOException("Local application settings could not be accessed."); }
    }

    /// <summary>Releases an opened native container without opening an unused one or deleting persisted settings.</summary>
    public void Dispose()
    {
        if (_container.IsValueCreated) _container.Value.Dispose();
    }
}