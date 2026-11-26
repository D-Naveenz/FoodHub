using FoodHub.Core.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FoodHub.Core.Services;

/// <summary>Shared safe failure mapping and session lookup for ordering operations.</summary>
internal static class OrderingOperations
{
    /// <summary>Looks up an active opaque token; the returned customer is the only permitted owner.</summary>
    internal static async Task<Customer?> OwnerAsync(FoodHub_DevContext db, string token, DateTime now, CancellationToken ct)
    {
        if (token.Length != 64 || !token.All(Uri.IsHexDigit)) return null;
        var hash = AccountRules.HashToken(token);
        return await db.UserSessions.AsNoTracking().Where(s => s.TokenHash == hash && s.RevokedAtUtc == null && s.ExpiresAtUtc > now)
            .Select(s => s.User.Customer).SingleOrDefaultAsync(ct);
    }

    /// <summary>Reads required pricing, rather than silently inventing a fee during an outage.</summary>
    internal static async Task<StorePrices> PricesAsync(FoodHub_DevContext db, CancellationToken ct) =>
        await db.StoreSettings.AsNoTracking().Where(s => s.StoreSettingsId == 1)
            .Select(s => new StorePrices(s.CurrencyCode, s.DeliveryFee)).SingleOrDefaultAsync(ct)
        ?? throw new DatabaseConfigurationException("Store prices are missing. Apply the required database setup.");

    /// <summary>Converts expected failures to safe results. Write callers mark only submitted failures uncertain.</summary>
    internal static async Task<OrderingResult<T>> Run<T>(Func<Task<OrderingResult<T>>> action)
    {
        try { return await action(); }
        catch (DatabaseConfigurationException ex) { return new(default, OrderingError.Configuration, ex.Message); }
        catch (SqlException ex) when (ex.Number is 208 or 207 or 2812 or 2715)
        { return new(default, OrderingError.Configuration, "The ordering schema is unavailable. Apply the required migrations."); }
        catch (SqlException ex) when (ex.Number is >= 51001 and <= 51006)
        {
            var message = ex.Number switch
            {
                51001 => "Add at least one item before placing an order.",
                51002 => "Choose one of your saved delivery addresses.",
                51003 => "Store prices are not configured.",
                51004 => "An item is unavailable. Refresh and update your basket.",
                51005 => "This order could not be found for your account.",
                _ => "The order has changed or cannot move to that stage. Refresh its status."
            };
            return new(default, OrderingError.Conflict, message);
        }
        catch (Exception ex) when (ex is SqlException or TimeoutException or InvalidOperationException)
        { return new(default, OrderingError.Database, "Cannot load ordering data. Check Settings and SQL Server, then retry."); }
    }
}
