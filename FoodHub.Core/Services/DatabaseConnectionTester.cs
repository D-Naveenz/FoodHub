using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FoodHub.Core.Services;

/// <summary>Read-only checks for the candidate database, independent of the active connection.</summary>
public sealed class DatabaseConnectionTester(ILogger<DatabaseConnectionTester> logger)
{
    /// <summary>Checks connectivity and migration history with a bounded deadline; never changes SQL.</summary>
    public async Task<string> TestAsync(string connectionString, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            // Also bound individual SQL operations if the server ignores connection cancellation.
            var candidate = new SqlConnectionStringBuilder(connectionString) { ConnectTimeout = 5 };
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["ConnectionStrings:FoodHub"] = candidate.ConnectionString }).Build();
            using var configurationLifetime = configuration as IDisposable;
            await using var db = new DatabaseSettings(configuration).Open();
            db.Database.SetCommandTimeout(5);
            await db.Database.OpenConnectionAsync(deadline.Token);
            var required = db.Database.GetMigrations().ToArray();
            var applied = (await db.Database.GetAppliedMigrationsAsync(deadline.Token)).ToHashSet(StringComparer.Ordinal);
            return required.All(applied.Contains)
                ? "Connection succeeded. Required account migrations are present."
                : "Connected, but required account migrations are missing. Apply migrations before signing in.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return "The connection check timed out. Check SQL Server availability."; }
        catch (DatabaseConfigurationException ex) { return ex.Message; }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or ArgumentException or TimeoutException)
        {
            logger.LogWarning("Database connection check failed: {Category} ({Code})", ex.GetType().Name,
                ex is SqlException sql ? sql.Number : 0);
            return ex is SqlException { Number: 208 or 207 }
                ? "Connected, but the account schema is unavailable. Apply the required migrations."
                : "Could not connect. Check the server, database, Windows permissions, and certificate settings.";
        }
    }
}