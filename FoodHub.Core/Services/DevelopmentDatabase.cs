using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using FoodHub.Core.Models;

namespace FoodHub.Core.Services;

/// <summary>Explicit tooling for the disposable local development database; never used during app startup.</summary>
public static class DevelopmentDatabase
{
    /// <summary>Rejects destructive requests outside the one approved localhost/FoodHub_Dev target.</summary>
    public static void ValidateResetTarget(string connectionString)
    {
        var target = new SqlConnectionStringBuilder(connectionString);
        if (!string.Equals(target.DataSource, "localhost", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(target.InitialCatalog, "FoodHub_Dev", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(target.AttachDBFilename) || target.UserInstance)
            throw new InvalidOperationException("Reset is allowed only for Server=localhost;Database=FoodHub_Dev.");
    }
    /// <summary>Drops and rebuilds the approved development target without backups or demo data.</summary>
    public static Task ResetAsync(string connectionString, CancellationToken cancellation = default)
    {
        ValidateResetTarget(connectionString);
        return RecreateAsync(connectionString, cancellation);
    }
    /// <summary>The isolated SQL test harness can exercise recreation using its own generated database.</summary>
    internal static async Task RecreateAsync(string connectionString, CancellationToken cancellation = default)
    {
        await using var db = new FoodHub_DevContext(new DbContextOptionsBuilder<FoodHub_DevContext>().UseSqlServer(connectionString).Options);
        await db.Database.EnsureDeletedAsync(cancellation);
        await db.Database.MigrateAsync(cancellation);
    }
}