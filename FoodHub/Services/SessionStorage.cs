using Dhara.Extensions.WinUI.Storage;
using Dhara.Extensions.WinUI.Hosting;
using System.IO;
using FoodHub.Core.Services;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;
using Windows.Storage.Streams;

namespace FoodHub.Services;

/// <summary>Persists only a Windows-user-protected bearer token, never passwords or profile data.</summary>
public sealed class SessionStorage
{
    private readonly ILocalStorageService _storage;
    private string FilePath => _storage.GetFilePath("session.dat");

    /// <summary>Uses the app's active storage service; encryption remains scoped to the Windows user.</summary>
    public SessionStorage(ILocalStorageService storage) => _storage = storage;
    /// <summary>Creates isolated file storage for storage tests and explicit tooling callers.</summary>
    public SessionStorage(string directory) : this(new LocalStorageService(directory)) { }
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Reads and decrypts the token. Corrupt or foreign-user data is discarded.</summary>
    public async Task<string?> ReadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (!File.Exists(FilePath)) return null;
            if (new FileInfo(FilePath).Length > 16384) { await _storage.DeleteAsync("session.dat"); return null; }
            var bytes = await _storage.ReadAsync("session.dat");
            if (bytes is null) return null;
            var clear = await new DataProtectionProvider().UnprotectAsync(CryptographicBuffer.CreateFromByteArray(bytes));
            var token = CryptographicBuffer.ConvertBinaryToString(BinaryStringEncoding.Utf8, clear);
            if (token.Length == 64 && token.All(Uri.IsHexDigit)) return token;
            await _storage.DeleteAsync("session.dat");
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            // A failed decryption cannot authenticate a user. Never log the protected payload.
            try { await _storage.DeleteAsync("session.dat"); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return null;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Serializes writes/removals so an old asynchronous write cannot undo sign-out.</summary>
    public async Task WriteAsync(string? token)
    {
        await _gate.WaitAsync();
        try
        {
            if (token is null) { await _storage.DeleteAsync("session.dat"); return; }
            var clear = CryptographicBuffer.ConvertStringToBinary(token, BinaryStringEncoding.Utf8);
            IBuffer protectedData = await new DataProtectionProvider("LOCAL=user").ProtectAsync(clear);
            CryptographicBuffer.CopyToByteArray(protectedData, out var bytes);
            await _storage.WriteAsync("session.dat", bytes);
        }
        finally { _gate.Release(); }
    }
}