using FoodHub.Services;
using System.Text;

namespace FoodHub.WinUI.Test;

/// <summary>Exercises the actual Windows-protected storage implementation without touching the user's saved session.</summary>
[TestClass]
[TestCategory("WindowsStorage")]
[OSCondition(OperatingSystems.Windows)]
public sealed class SessionStorageTests
{
    private string _directory = "";
    private string _path = "";
    private SessionStorage _storage = null!;
    private static string Token() => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    /// <summary>Each case receives a distinct directory, independent of the customer app's local data.</summary>
    [TestInitialize]
    public void CreateIsolatedStorage()
    {
        _directory = Path.Combine(Path.GetTempPath(), "FoodHub-session-tests-" + Guid.NewGuid().ToString("N"));
        _path = Path.Combine(_directory, "session.dat");
        _storage = new SessionStorage(_directory);
    }

    /// <summary>Deletes only the known test files and empty generated directory, never user app data.</summary>
    [TestCleanup]
    public void RemoveIsolatedStorage()
    {
        if (File.Exists(_path)) File.Delete(_path);
        if (File.Exists(_path + ".tmp")) File.Delete(_path + ".tmp");
        if (Directory.Exists(_directory)) Directory.Delete(_directory);
    }

    /// <summary>A first launch without saved credentials starts as Guest.</summary>
    [TestMethod]
    public async Task FirstLaunchHasNoToken() => Assert.IsNull(await _storage.ReadAsync());

    /// <summary>The disk payload must not contain the raw bearer token.</summary>
    [TestMethod]
    public async Task RememberedTokenIsEncryptedOnDisk()
    {
        var token = Token();
        await _storage.WriteAsync(token);
        Assert.IsFalse(Encoding.UTF8.GetString(await File.ReadAllBytesAsync(_path)).Contains(token, StringComparison.Ordinal));
    }

    /// <summary>A new storage instance for the same Windows user can restore the encrypted token.</summary>
    [TestMethod]
    public async Task NewInstanceRestoresRememberedToken()
    {
        var token = Token();
        await _storage.WriteAsync(token);
        Assert.AreEqual(token, await new SessionStorage(_directory).ReadAsync());
    }

    /// <summary>Memory-only sign-in and sign-out both remove an earlier remembered token.</summary>
    [TestMethod]
    public async Task ClearingSessionRemovesPersistedToken()
    {
        await _storage.WriteAsync(Token());
        await _storage.WriteAsync(null);
        Assert.IsFalse(File.Exists(_path));
        Assert.IsNull(await _storage.ReadAsync());
    }

    /// <summary>Corrupt encrypted data is discarded and cannot restore an account.</summary>
    [TestMethod]
    public async Task CorruptStorageIsDiscarded()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllBytesAsync(_path, [1, 2, 3, 4, 5]);
        Assert.IsNull(await _storage.ReadAsync());
        Assert.IsFalse(File.Exists(_path));
    }

    /// <summary>Excessively large payloads are rejected before decryption.</summary>
    [TestMethod]
    public async Task OversizedStorageIsRejected()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllBytesAsync(_path, new byte[20000], TestContext.CancellationToken);
        Assert.IsNull(await _storage.ReadAsync());
        Assert.IsFalse(File.Exists(_path));
    }

    /// <summary>A previously pending save cannot repersist a token after sign-out completes.</summary>
    [TestMethod]
    public async Task SignOutFollowsEarlierPendingWrite()
    {
        var saving = _storage.WriteAsync(Token());
        var removing = _storage.WriteAsync(null);
        await Task.WhenAll(saving, removing);
        Assert.IsNull(await _storage.ReadAsync());
    }

    public TestContext TestContext { get; set; }
}
