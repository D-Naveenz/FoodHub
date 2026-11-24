using FoodHub.Services;
using System.Text;

// Exercise the actual Windows-protected storage code without touching the user's saved FoodHub session.
var directory = Path.Combine(Path.GetTempPath(), "FoodHub-session-tests-" + Guid.NewGuid().ToString("N"));
var path = Path.Combine(directory,"session.dat");
var assertions = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + message);
    Console.WriteLine("PASS: " + message); assertions++;
}
try
{
    var storage = new SessionStorage(directory);
    Check(await storage.ReadAsync() is null,"first launch has no stored token");
    var token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    await storage.WriteAsync(token);
    Check(!Encoding.UTF8.GetString(await File.ReadAllBytesAsync(path)).Contains(token,StringComparison.Ordinal),"remembered token is encrypted on disk");
    Check(await new SessionStorage(directory).ReadAsync() == token,"a new app instance decrypts a remembered token for this Windows user");
    await storage.WriteAsync(null);
    Check(!File.Exists(path) && await storage.ReadAsync() is null,"memory-only sign-in/sign-out clears prior persisted state");
    await File.WriteAllBytesAsync(path,[1,2,3,4,5]);
    Check(await storage.ReadAsync() is null && !File.Exists(path),"corrupt protected storage becomes Guest and is removed");
    await File.WriteAllBytesAsync(path,new byte[20000]);
    Check(await storage.ReadAsync() is null && !File.Exists(path),"oversized protected storage is rejected before decryption");
    var saving = storage.WriteAsync(token); var removing = storage.WriteAsync(null);
    await Task.WhenAll(saving,removing);
    Check(await storage.ReadAsync() is null,"serialized sign-out cannot be undone by an earlier pending write");
    Console.WriteLine($"{assertions} Windows storage assertions passed.");
}
finally
{
    // Delete only the exact, generated isolated test files; never recurse through user app data.
    if (File.Exists(path)) File.Delete(path);
    if (File.Exists(path+".tmp")) File.Delete(path+".tmp");
    if (Directory.Exists(directory)) Directory.Delete(directory);
}
