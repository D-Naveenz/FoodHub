using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FoodHub.Core.Services;

/// <summary>Distinguishes a usable database, an incomplete schema, and a failed check.</summary>
public enum ConnectionCheckStatus
{
    /// <summary>The connection works and all required migrations are present.</summary>
    Success,
    /// <summary>The connection works but the schema needs attention.</summary>
    Warning,
    /// <summary>The connection or configuration could not be checked successfully.</summary>
    Error
}

/// <summary>Returns safe feedback together with its outcome, independently of UI controls.</summary>
/// <param name="Status">The outcome used to choose the feedback presentation.</param>
/// <param name="Message">Safe customer-facing feedback without connection details.</param>
public sealed record ConnectionCheckResult(ConnectionCheckStatus Status, string Message);

/// <summary>Read-only checks for the candidate database, independent of the active connection.</summary>
public sealed class DatabaseConnectionTester(ILogger<DatabaseConnectionTester> logger)
{
    /// <summary>Checks connectivity and migration history with a bounded deadline; never changes SQL.</summary>
    public async Task<ConnectionCheckResult> TestAsync(string connectionString, CancellationToken cancellation = default)
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
                ? new(ConnectionCheckStatus.Success, "Connection succeeded. Required account migrations are present.")
                : new(ConnectionCheckStatus.Warning, "Connected, but required account migrations are missing. Apply migrations before signing in.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return new(ConnectionCheckStatus.Error, "The connection check timed out. Check SQL Server availability."); }
        catch (DatabaseConfigurationException ex) { return new(ConnectionCheckStatus.Error, ex.Message); }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or ArgumentException or TimeoutException)
        {
            logger.LogWarning("Database connection check failed: {Category} ({Code})", ex.GetType().Name,
                ex is SqlException sql ? sql.Number : 0);
            return ex is SqlException { Number: 208 or 207 }
                ? new(ConnectionCheckStatus.Warning, "Connected, but the account schema is unavailable. Apply the required migrations.")
                : new(ConnectionCheckStatus.Error, "Could not connect. Check the server, database, Windows permissions, and certificate settings.");
        }
    }
}