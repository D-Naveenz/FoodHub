using System.IO;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;
using Windows.Storage.Streams;

namespace FoodHub.Services;

/// <summary>Persists only a Windows-user-protected bearer token, never passwords or profile data.</summary>
public sealed class SessionStorage
{
    private readonly string _path;

    /// <summary>Uses per-user app data by default; a separate directory permits isolated storage tests.</summary>
    public SessionStorage(string? directory = null) => _path = Path.Combine(directory ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FoodHub"), "session.dat");
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Reads and decrypts the token. Corrupt or foreign-user data is discarded.</summary>
    public async Task<string?> ReadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (!File.Exists(_path)) return null;
            if (new FileInfo(_path).Length > 16384) { File.Delete(_path); return null; }
            var bytes = await File.ReadAllBytesAsync(_path);
            var clear = await new DataProtectionProvider().UnprotectAsync(CryptographicBuffer.CreateFromByteArray(bytes));
            var token = CryptographicBuffer.ConvertBinaryToString(BinaryStringEncoding.Utf8, clear);
            if (token.Length == 64 && token.All(Uri.IsHexDigit)) return token;
            File.Delete(_path);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            // A failed decryption cannot authenticate a user. Never log the protected payload.
            try { File.Delete(_path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
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
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            if (token is null) { File.Delete(_path); return; }
            var clear = CryptographicBuffer.ConvertStringToBinary(token, BinaryStringEncoding.Utf8);
            IBuffer protectedData = await new DataProtectionProvider("LOCAL=user").ProtectAsync(clear);
            CryptographicBuffer.CopyToByteArray(protectedData, out var bytes);
            await File.WriteAllBytesAsync(_path + ".tmp", bytes);
            File.Move(_path + ".tmp", _path, true);
        }
        finally { _gate.Release(); }
    }
}
