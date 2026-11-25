using FoodHub.Core.Hosting;
using FoodHub.Core.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using System.Text.Json.Nodes;

namespace FoodHub.Core.Services;

/// <summary>A safe configuration failure without connection details.</summary>
public sealed class DatabaseConfigurationException(string message) : InvalidOperationException(message);

/// <summary>An immutable database configuration snapshot for one launch.</summary>
public sealed class DatabaseSettings
{
    /// <summary>Captures values once so saving never changes active operations.</summary>
    /// <param name="configuration">The effective general configuration after all JSON layers are loaded.</param>
    /// <param name="loadError">A safe general loading error; non-null prevents database fallback.</param>
    public DatabaseSettings(IConfiguration configuration, string? loadError = null)
    {
        ConnectionString = configuration.GetConnectionString("FoodHub") ?? "";
        LoadError = loadError;
        ResetSessionOnStartup = bool.TryParse(configuration["FoodHub:ResetSessionOnStartup"], out var reset) && reset;
    }
    /// <summary>Active connection; never write this value to logs.</summary>
    public string ConnectionString { get; }
    /// <summary>Safe feedback when configuration could not be read.</summary>
    public string? LoadError { get; }
    /// <summary>Whether remembered authentication must be removed before restoration.</summary>
    public bool ResetSessionOnStartup { get; }
    /// <summary>Validates settings before any SQL operation.</summary>
    /// <returns>A normalized connection string containing a server and database.</returns>
    /// <exception cref="DatabaseConfigurationException">Loading failed or the connection is missing or malformed.</exception>
    public string RequireConnectionString()
    {
        if (LoadError is not null) throw new DatabaseConfigurationException(LoadError);
        if (string.IsNullOrWhiteSpace(ConnectionString))
            throw new DatabaseConfigurationException("Set up the account database in Settings, then restart FoodHub.");
        try
        {
            var builder = new SqlConnectionStringBuilder(ConnectionString);
            if (string.IsNullOrWhiteSpace(builder.DataSource) || string.IsNullOrWhiteSpace(builder.InitialCatalog)) throw new ArgumentException();
            return builder.ConnectionString;
        }
        catch (ArgumentException)
        { throw new DatabaseConfigurationException("The database configuration is invalid. Correct it in Settings and restart FoodHub."); }
    }
    /// <summary>Creates a context for one operation; never migrates automatically.</summary>
    /// <returns>A context owned by the caller; creating it does not open the connection.</returns>
    /// <exception cref="DatabaseConfigurationException">The launch configuration is unusable.</exception>
    public FoodHub_DevContext Open() => new(new DbContextOptionsBuilder<FoodHub_DevContext>()
        .UseSqlServer(RequireConnectionString()).Options);
}

/// <summary>Adapts shared application configuration to parameterless contexts and EF design-time tools.</summary>
/// <remarks>Provider loading belongs to ApplicationConfiguration; this adapter only creates database snapshots.</remarks>
public sealed class DatabaseConfiguration : IDesignTimeDbContextFactory<FoodHub_DevContext>
{
    private static readonly Lazy<DatabaseSettings> Default = new(() => LoadSettings(
        System.Reflection.Assembly.GetEntryAssembly() ?? typeof(DatabaseConfiguration).Assembly));

    /// <summary>Creates a database snapshot using the selected consumer's build environment.</summary>
    /// <param name="applicationAssembly">The administration executable or EF design-time factory assembly.</param>
    /// <returns>A launch snapshot; no SQL connection is opened.</returns>
    public static DatabaseSettings LoadSettings(System.Reflection.Assembly applicationAssembly)
    {
        var (configuration, error) = ApplicationConfiguration.Load(new ApplicationPaths(), DesktopEnvironment.Resolve(applicationAssembly));
        using (configuration as IDisposable) return new DatabaseSettings(configuration, error);
    }

    /// <summary>Gets the validated connection for a parameterless context.</summary>
    public static string ConnectionString => Default.Value.RequireConnectionString();
    /// <summary>Creates a short-lived context from the parameterless consumer's launch snapshot.</summary>
    public static FoodHub_DevContext Open() => Default.Value.Open();
    /// <summary>Creates a context using the factory assembly's configuration without applying migrations.</summary>
    /// <param name="args">EF arguments; application-setting command-line overrides are not supported.</param>
    /// <returns>A context that EF tooling owns and disposes.</returns>
    public FoodHub_DevContext CreateDbContext(string[] args) => LoadSettings(typeof(DatabaseConfiguration).Assembly).Open();
}

/// <summary>Persists next-launch settings atomically without changing the active database.</summary>
/// <param name="settings">The general JSON writer shared with appearance and other settings features.</param>
/// <param name="active">The immutable launch snapshot used by current workflows.</param>
/// <remarks>
/// A saved connection never changes an existing context or authenticated workflow. The next launch removes
/// remembered authentication before consuming the reset marker, preventing restoration against another database.
/// </remarks>
public sealed class DatabaseSettingsStore(LocalSettingsStore settings, DatabaseSettings active)
{
    /// <summary>Survives Settings navigation until restart.</summary>
    public bool RestartRequired { get; private set; }
    /// <summary>Saved candidate shown when the user returns to Settings before restarting.</summary>
    public string? PendingConnectionString { get; private set; }
    /// <summary>Builds Windows-authentication settings using SQL's connection-string builder.</summary>
    /// <param name="server">The SQL Server host or named instance.</param>
    /// <param name="database">The target database name.</param>
    /// <param name="trustCertificate">Whether to trust a local server's certificate without chain validation.</param>
    /// <returns>A Windows-authentication connection string with a bounded connection timeout.</returns>
    /// <exception cref="DatabaseConfigurationException">Server or database is blank.</exception>
    public static string BuildConnectionString(string server, string database, bool trustCertificate)
    {
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(database))
            throw new DatabaseConfigurationException("Enter a server and database name.");
        return new SqlConnectionStringBuilder { DataSource = server.Trim(), InitialCatalog = database.Trim(),
            IntegratedSecurity = true, TrustServerCertificate = trustCertificate, ConnectTimeout = 5 }.ConnectionString;
    }
    /// <summary>Saves an override, or removes it to use defaults; no SQL availability is required.</summary>
    /// <param name="connectionString">The next-launch connection, or null to remove only the local override.</param>
    /// <param name="cancellation">Cancels waiting or persistence before the file is replaced.</param>
    /// <returns>A task completing after settings and the durable reset marker are saved together.</returns>
    /// <exception cref="DatabaseConfigurationException">The supplied connection is invalid.</exception>
    /// <remarks>This explicit repair action may replace malformed JSON; valid unrelated settings are retained.</remarks>
    public async Task SaveAsync(string? connectionString, CancellationToken cancellation = default)
    {
        if (connectionString is not null)
        {
            var candidate = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["ConnectionStrings:FoodHub"] = connectionString }).Build();
            using (candidate as IDisposable) _ = new DatabaseSettings(candidate).RequireConnectionString();
        }
        await settings.UpdateAsync(json =>
        {
            if (json["ConnectionStrings"] is not JsonObject) json["ConnectionStrings"] = new JsonObject();
            var connections = (JsonObject)json["ConnectionStrings"]!;
            if (connectionString is null) connections.Remove("FoodHub");
            else connections["FoodHub"] = connectionString;
            if (json["FoodHub"] is not JsonObject) json["FoodHub"] = new JsonObject();
            // Reset on next launch, including after switching back to defaults.
            json["FoodHub"]!["ResetSessionOnStartup"] = true;
        }, repairMalformed: true, cancellation);
        // Publish pending state only after persistence succeeds; the active snapshot remains unchanged.
        PendingConnectionString = connectionString;
        RestartRequired = true;
    }
    /// <summary>Deletes remembered authentication before consuming its durable reset marker.</summary>
    /// <param name="removeToken">Deletes the protected local token; failures must propagate to suppress restoration.</param>
    /// <returns>A task completing after token deletion and marker persistence, or immediately if no reset is pending.</returns>
    /// <exception cref="DatabaseConfigurationException">Application settings could not be loaded safely.</exception>
    public async Task PrepareSessionAsync(Func<Task> removeToken)
    {
        if (active.LoadError is not null) throw new DatabaseConfigurationException(active.LoadError);
        if (!active.ResetSessionOnStartup) return;
        await removeToken(); // Failure leaves the marker intact and suppresses restoration.
        await settings.UpdateAsync(json =>
        {
            if (json["FoodHub"] is not JsonObject) json["FoodHub"] = new JsonObject();
            json["FoodHub"]!["ResetSessionOnStartup"] = false;
        });
    }
}