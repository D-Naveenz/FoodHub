using FoodHub.Core.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FoodHub.WinUI.Test;

/// <summary>Owns one uniquely named disposable database on the explicitly configured test server.</summary>
internal sealed class SqlTestDatabase : IAsyncDisposable
{
    private readonly string _name = "FoodHub_Test_" + Guid.NewGuid().ToString("N");
    private readonly string _adminConnection;
    private bool _created;

    private SqlTestDatabase(string connection)
    {
        var builder = new SqlConnectionStringBuilder(connection) { InitialCatalog = "master", Pooling = false };
        _adminConnection = builder.ConnectionString;
        builder.InitialCatalog = _name;
        ConnectionString = builder.ConnectionString;
    }

    /// <summary>Connection to this generated database, never to FoodHub_Dev.</summary>
    public string ConnectionString { get; }

    /// <summary>Creates an empty isolated database; SQL errors fail the test rather than silently skipping it.</summary>
    public static async Task<SqlTestDatabase> CreateAsync(string connection)
    {
        var database = new SqlTestDatabase(connection);
        await database.ExecuteAdminAsync($"CREATE DATABASE [{database._name}]");
        database._created = true;
        return database;
    }

    /// <summary>Creates a short-lived EF context for this test database only.</summary>
    public FoodHub_DevContext Open() => new(new DbContextOptionsBuilder<FoodHub_DevContext>()
        .UseSqlServer(ConnectionString).Options);

    /// <summary>Removes only the exact generated database after all owned contexts have been disposed.</summary>
    public async ValueTask DisposeAsync()
    {
        if (!_created) return;
        // Validate the final identifier before executing cleanup. Never derive a drop target from the supplied connection string.
        if (!_name.StartsWith("FoodHub_Test_", StringComparison.Ordinal)
            || !Guid.TryParseExact(_name["FoodHub_Test_".Length..], "N", out _))
            throw new InvalidOperationException("Unsafe test cleanup target.");
        await ExecuteAdminAsync($"ALTER DATABASE [{_name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_name}];");
        _created = false;
    }

    private async Task ExecuteAdminAsync(string sql)
    {
        await using var connection = new SqlConnection(_adminConnection);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
        await command.ExecuteNonQueryAsync();
    }
}
