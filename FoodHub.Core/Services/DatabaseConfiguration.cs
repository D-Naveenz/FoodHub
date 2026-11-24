using FoodHub.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FoodHub.Core.Services;

/// <summary>Shares external configuration between the app, tests, and EF tools.</summary>
public sealed class DatabaseConfiguration : IDesignTimeDbContextFactory<FoodHub_DevContext>
{
    /// <summary>Reads configuration without storing credentials in source control.</summary>
    public static string ConnectionString => Environment.GetEnvironmentVariable("FOODHUB_CONNECTION_STRING")
        ?? throw new InvalidOperationException("Set FOODHUB_CONNECTION_STRING before connecting to SQL Server.");

    /// <summary>Creates a short-lived context with external connection options.</summary>
    public static FoodHub_DevContext Open() => new(new DbContextOptionsBuilder<FoodHub_DevContext>()
        .UseSqlServer(ConnectionString).Options);

    /// <summary>Creates the EF design-time context; never applies migrations automatically.</summary>
    public FoodHub_DevContext CreateDbContext(string[] args) => Open();
}
