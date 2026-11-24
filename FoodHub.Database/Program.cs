using FoodHub.Core.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Data;

// Explicit administrative entry point. The desktop application never runs migrations on startup.
try
{
    var mode = args.FirstOrDefault();
    if (mode is not ("migrate" or "baseline" or "verify" or "describe"))
    {
        Console.WriteLine("Usage: FoodHub.Database migrate | verify | baseline <SQL-Server-backup-path>");
        Console.WriteLine("Set FOODHUB_CONNECTION_STRING. verify/baseline also require FOODHUB_BASELINE_REFERENCE (a separate database migrated to Baseline only).");
        return 2;
    }
    var target = DatabaseConfiguration.ConnectionString;
    if (mode == "describe") { Console.WriteLine(await BaselineVerifier.DescribeAsync(target)); return 0; }
    await using var db = DatabaseConfiguration.Open();
    var migrations = db.Database.GetMigrations().ToArray();
    var baseline = migrations.Single(m => m.EndsWith("_Baseline", StringComparison.Ordinal));
    if (mode == "migrate")
    {
        // EF refuses a legacy populated database naturally; it must pass explicit baseline adoption first.
        await db.Database.MigrateAsync();
        Console.WriteLine("Migrations applied. No demonstration data was loaded.");
        return 0;
    }
    var reference = Environment.GetEnvironmentVariable("FOODHUB_BASELINE_REFERENCE")
        ?? throw new InvalidOperationException("Set FOODHUB_BASELINE_REFERENCE to a separate clean Baseline database.");
    var targetBuilder = new SqlConnectionStringBuilder(target);
    var referenceBuilder = new SqlConnectionStringBuilder(reference);
    if (string.Equals(targetBuilder.DataSource, referenceBuilder.DataSource, StringComparison.OrdinalIgnoreCase)
        && string.Equals(targetBuilder.InitialCatalog, referenceBuilder.InitialCatalog, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Target and reference must be different databases.");
    await using (var referenceContext = new FoodHub.Core.Models.FoodHub_DevContext(
        new DbContextOptionsBuilder<FoodHub.Core.Models.FoodHub_DevContext>().UseSqlServer(reference).Options))
    {
        var applied = (await referenceContext.Database.GetAppliedMigrationsAsync()).ToArray();
        if (applied.Length != 1 || applied[0] != baseline)
            throw new InvalidOperationException("Reference must be migrated to Baseline only.");
    }
    await BaselineVerifier.VerifyAsync(target, reference);
    Console.WriteLine("Physical baseline schema matches (including procedures and table types).");
    if (mode == "verify") return 0;
    if (args.Length != 2 || string.IsNullOrWhiteSpace(args[1])) throw new InvalidOperationException("Supply a backup destination on the SQL Server machine.");
    if ((await db.Database.GetAppliedMigrationsAsync()).Any()) throw new InvalidOperationException("This database already has migration history.");
    await using var connection = new SqlConnection(target);
    await connection.OpenAsync();
    // Backup is completed before recording history; paths are SQL parameters, not shell-built text.
    await using (var backup = new SqlCommand("BACKUP DATABASE " + Quote(targetBuilder.InitialCatalog) + " TO DISK = @path WITH COPY_ONLY, CHECKSUM;", connection) { CommandTimeout = 300 })
    {
        backup.Parameters.AddWithValue("@path", args[1]); await backup.ExecuteNonQueryAsync();
    }
    await using (var verifyBackup = new SqlCommand("RESTORE VERIFYONLY FROM DISK = @path WITH CHECKSUM;", connection) { CommandTimeout = 300 })
    {
        verifyBackup.Parameters.AddWithValue("@path", args[1]); await verifyBackup.ExecuteNonQueryAsync();
    }
    // Repeat immediately before adoption. Run with other schema-changing tools stopped.
    await BaselineVerifier.VerifyAsync(target, reference);
    await using var transaction = await db.Database.BeginTransactionAsync();
    var history = db.GetService<IHistoryRepository>();
    await db.Database.ExecuteSqlRawAsync(history.GetCreateIfNotExistsScript());
    await db.Database.ExecuteSqlRawAsync(history.GetInsertScript(new HistoryRow(baseline, "10.0.15")));
    await transaction.CommitAsync();
    Console.WriteLine("Backup verified and baseline recorded. Run migrate to add accounts.");
    return 0;
}
catch (Exception ex)
{
    // Do not print connection strings or database parameter payloads.
    Console.Error.WriteLine(ex is InvalidOperationException ? ex.Message : $"Database operation failed ({ex.GetType().Name}). Review server diagnostics.");
    return 1;
}

/// <summary>Quotes a database identifier separately from parameterized values.</summary>
static string Quote(string name) => "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";
