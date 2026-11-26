using FoodHub.Core.Models;
using FoodHub.Core.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FoodHub.Core.Hosting;

/// <summary>Creates SQL Server contexts for EF automation without starting the application or native storage.</summary>
/// <remarks>
/// Design-time commands use explicit development targets, independently of the application's JSON and per-user
/// runtime preferences. Empty arguments select localhost/FoodHub_Dev. Arguments after EF's -- separator may
/// override --server and --database. The --reset flag additionally enforces the disposable-development policy.
/// Creating a context does not open SQL, apply migrations or delete data; EF's requested command does that.
/// </remarks>
/// <seealso href="https://learn.microsoft.com/ef/core/cli/dbcontext-creation"/>
public sealed class FoodHubDesignTimeContextFactory : IDesignTimeDbContextFactory<FoodHub_DevContext>
{
    /// <summary>Builds Windows-authentication options for the explicit automation target.</summary>
    /// <param name="args">Optional --server value, --database value and --reset flag.</param>
    /// <returns>A context owned by the calling EF tool.</returns>
    /// <exception cref="ArgumentException">An argument is unknown, repeated, missing or empty.</exception>
    /// <exception cref="InvalidOperationException">A reset requests an unapproved target.</exception>
    public FoodHub_DevContext CreateDbContext(string[] args)
    {
        var server = "localhost";
        var database = "FoodHub_Dev";
        var reset = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index++)
        {
            var option = args[index];
            if (!seen.Add(option)) throw new ArgumentException("A database tooling argument was repeated.", nameof(args));
            if (option == "--reset") { reset = true; continue; }
            if (option is not ("--server" or "--database") || ++index >= args.Length
                || string.IsNullOrWhiteSpace(args[index]) || args[index].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("Use --server value, --database value and optionally --reset.", nameof(args));
            if (option == "--server") server = args[index];
            else database = args[index];
        }
        // The provider's builder quotes values, so semicolons in input cannot inject connection options.
        var connection = new SqlConnectionStringBuilder
        {
            DataSource = server, InitialCatalog = database, IntegratedSecurity = true,
            TrustServerCertificate = true, ConnectTimeout = 5
        }.ConnectionString;
        if (reset) DevelopmentDatabase.ValidateResetTarget(connection);
        return new FoodHub_DevContext(new DbContextOptionsBuilder<FoodHub_DevContext>().UseSqlServer(connection).Options);
    }
}