using Dhara.Extensions.WinUI.Storage;

namespace FoodHub.WinUI.Test;

/// <summary>Isolates logic tests from the Windows user's native settings while allowing controlled access failures.</summary>
internal sealed class SettingsTestContainer : ILocalSettingsContainer
{
    /// <summary>Gets raw values used only to seed deliberately invalid persisted shapes in tests.</summary>
    internal Dictionary<string, object> Values { get; } = [];
    /// <summary>Gets or sets whether reads should fail like an inaccessible native store.</summary>
    internal bool FailReads { get; set; }
    /// <summary>Gets or sets whether commits should fail before changing stored values.</summary>
    internal bool FailWrites { get; set; }
    /// <inheritdoc/>
    public object? Read(string key)
    {
        if (FailReads) throw new IOException("Expected isolated read failure.");
        return Values.GetValueOrDefault(key);
    }
    /// <inheritdoc/>
    public void Write(string key, object value)
    {
        if (FailWrites) throw new IOException("Expected isolated write failure.");
        Values[key] = value;
    }
    /// <inheritdoc/>
    public void Remove(string key)
    {
        if (FailWrites) throw new IOException("Expected isolated write failure.");
        Values.Remove(key);
    }
}