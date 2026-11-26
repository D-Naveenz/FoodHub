using Dhara.Extensions.WinUI;
using Dhara.Extensions.WinUI.Storage;

namespace FoodHub.WinUI.Test;

/// <summary>Exercises real Windows LocalSettings in uniquely named containers, never the customer's runtime settings.</summary>
[TestClass]
[TestCategory("WindowsStorage")]
[DoNotParallelize]
public sealed class NativeLocalSettingsTests
{
    private ApplicationDataContext _context = null!;
    private string _name = "";
    /// <summary>Uses the package context when packaged, or a separate test publisher/product when unpackaged.</summary>
    [TestInitialize]
    public void Initialize()
    {
        _name = "Tests_" + Guid.NewGuid().ToString("N");
        _context = new ApplicationDataContext("Dhara.Tests", "LocalSettingsTests", new AppRuntimeEnvironment());
    }
    /// <summary>Removes only this test's uniquely named settings container and releases the native context.</summary>
    [TestCleanup]
    public void Cleanup()
    {
        if (_context is null) return;
        try { _context.DeleteSettings(_name); }
        finally { _context.Dispose(); }
    }
    /// <summary>Scalar preferences and grouped values survive closing/reopening both native adapter and facade.</summary>
    [TestMethod]
    public async Task NativeValuesPersistAcrossInstances()
    {
        using (var adapter = new WindowsLocalSettingsContainer(_context.OpenSettings(_name)))
        using (var store = new LocalSettingsStore(adapter))
        {
            await store.WriteAsync("Appearance.Theme", "Dark");
            await store.UpdateGroupAsync("Database", group =>
            {
                group["ConnectionString"] = "Server=fictional;Database=Test";
                group["ResetSessionOnStartup"] = true;
            });
        }
        using var reopenedAdapter = new WindowsLocalSettingsContainer(_context.OpenSettings(_name));
        using var reopened = new LocalSettingsStore(reopenedAdapter);
        Assert.AreEqual("Dark", reopened.Read("Appearance.Theme", "System"));
        var database = reopened.ReadGroup("Database");
        Assert.AreEqual("Server=fictional;Database=Test", database["ConnectionString"]);
        Assert.AreEqual(true, database["ResetSessionOnStartup"]);
        await reopened.UpdateGroupAsync("Database", group => group.Remove("ConnectionString"));
        Assert.IsTrue((bool)reopened.ReadGroup("Database")["ResetSessionOnStartup"]);
        Assert.AreEqual("Dark", reopened.Read("Appearance.Theme", "System"));
    }
    /// <summary>Concurrent feature writes preserve each other; removing an override leaves other native values intact.</summary>
    [TestMethod]
    public async Task ConcurrentNativeUpdatesAndRemovalAreIsolated()
    {
        using var adapter = new WindowsLocalSettingsContainer(_context.OpenSettings(_name));
        using var store = new LocalSettingsStore(adapter);
        await Task.WhenAll(Enumerable.Range(0, 12).Select(index => Task.Run(() => store.UpdateGroupAsync("Related", group => group["Value" + index] = index))));
        Assert.AreEqual(12, store.ReadGroup("Related").Count);
        await store.WriteAsync("Unrelated", "keep");
        await store.RemoveAsync("Related");
        Assert.AreEqual(0, store.ReadGroup("Related").Count);
        Assert.AreEqual("keep", store.Read("Unrelated", ""));
    }
    /// <summary>Cancellation before commit preserves the previously committed native group.</summary>
    [TestMethod]
    public async Task CancelledNativeCommitPreservesPreviousGroup()
    {
        using var adapter = new WindowsLocalSettingsContainer(_context.OpenSettings(_name));
        using var store = new LocalSettingsStore(adapter);
        await store.UpdateGroupAsync("Related", group => group["Value"] = "keep");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => store.UpdateGroupAsync("Related", group => group["Value"] = "lost", cancellation: cancellation.Token));
        Assert.AreEqual("keep", store.ReadGroup("Related")["Value"]);
    }
    /// <summary>Invalid saved types are reported distinctly and explicit repair replaces only the requested group.</summary>
    [TestMethod]
    public async Task InvalidNativeGroupRequiresExplicitRepair()
    {
        using var adapter = new WindowsLocalSettingsContainer(_context.OpenSettings(_name));
        using var store = new LocalSettingsStore(adapter);
        await store.WriteAsync("Database", "wrong shape");
        await store.WriteAsync("Unrelated", 5);
        Assert.Throws<LocalSettingsValueException>(() => store.ReadGroup("Database"));
        await store.UpdateGroupAsync("Database", group => group["Reset"] = true, repairInvalid: true);
        Assert.AreEqual(true, store.ReadGroup("Database")["Reset"]);
        Assert.AreEqual(5, store.Read("Unrelated", 0));
    }
    /// <summary>Typed groups preserve non-JSON types and reject unsupported objects before replacing saved data.</summary>
    [TestMethod]
    public async Task GroupTypesRoundTripAndFailedEncodingPreservesPreviousValue()
    {
        using var adapter = new WindowsLocalSettingsContainer(_context.OpenSettings(_name));
        using var store = new LocalSettingsStore(adapter);
        var identifier = Guid.NewGuid();
        var timestamp = DateTimeOffset.UtcNow;
        await store.UpdateGroupAsync("Typed", group =>
        {
            group["Id"] = identifier;
            group["When"] = timestamp;
            group["Count"] = long.MaxValue;
            group["Bytes"] = new byte[] { 1, 2, 3 };
        });
        var saved = store.ReadGroup("Typed");
        Assert.AreEqual(identifier, saved["Id"]);
        Assert.AreEqual(timestamp, saved["When"]);
        Assert.AreEqual(long.MaxValue, saved["Count"]);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, (byte[])saved["Bytes"]);
        await Assert.ThrowsAsync<IOException>(() => store.UpdateGroupAsync("Typed", group => group["Unsupported"] = new object()));
        Assert.AreEqual(identifier, store.ReadGroup("Typed")["Id"]);
        Assert.IsFalse(store.ReadGroup("Typed").ContainsKey("Unsupported"));
        const string markerText = "\u001eDhara.Group/1:arbitrary user string";
        await store.WriteAsync("Text", markerText);
        Assert.AreEqual(markerText, store.Read("Text", ""));
    }

    /// <summary>Corrupt native group data blocks silent fallback and can be repaired by an explicit group update.</summary>
    [TestMethod]
    public async Task CorruptEncodedGroupRequiresExplicitRepair()
    {
        using (var native = _context.OpenSettings(_name))
            native.Values["Database"] = "\u001eDhara.Group/1:{invalid";
        using var adapter = new WindowsLocalSettingsContainer(_context.OpenSettings(_name));
        using var store = new LocalSettingsStore(adapter);
        Assert.Throws<LocalSettingsValueException>(() => store.ReadGroup("Database"));
        await Assert.ThrowsAsync<LocalSettingsValueException>(() => store.UpdateGroupAsync("Database", group => group["Reset"] = true));
        await store.UpdateGroupAsync("Database", group => group["Reset"] = true, repairInvalid: true);
        Assert.AreEqual(true, store.ReadGroup("Database")["Reset"]);
    }
}