using Dhara.Extensions.WinUI.Storage;
using Dhara.Extensions.WinUI.Hosting;
using FoodHub.Core.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace FoodHub.Core.Services;

/// <summary>A safe configuration failure without connection details.</summary>
public sealed class DatabaseConfigurationException(string message) : InvalidOperationException(message);

/// <summary>An immutable database configuration snapshot for one launch.</summary>
public sealed class DatabaseSettings
{
    /// <summary>Captures values once so saving never changes active operations.</summary>
    /// <param name="configuration">The effective general configuration after all JSON layers are loaded.</param>
    /// <param name="localSettings">Optional runtime overrides, read independently from host configuration.</param>
    /// <param name="loadError">A safe general loading error; non-null prevents database fallback.</param>
    public DatabaseSettings(IConfiguration configuration, string? loadError = null, ILocalSettingsStore? localSettings = null)
    {
        ConnectionString = configuration.GetConnectionString("FoodHub") ?? "";
        LoadError = loadError;
        if (localSettings is null) return;
        try
        {
            var group = localSettings.ReadGroup("Database");
            if (group.TryGetValue("ConnectionString", out var connection))
                ConnectionString = connection as string ?? throw new LocalSettingsValueException("The local connection override has an invalid type.");
            if (group.TryGetValue("ResetSessionOnStartup", out var reset))
                ResetSessionOnStartup = reset is bool flag ? flag : throw new LocalSettingsValueException("The local reset marker has an invalid type.");
        }
        catch (LocalSettingsValueException) { LoadError ??= "Saved database settings are invalid. Save or reset the local override in Settings, then restart."; }
        catch (IOException) { LoadError ??= "Local database settings could not be accessed. Check access to application data, then restart."; }
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

/// <summary>Adapts application configuration and native preferences to parameterless runtime contexts.</summary>
/// <remarks>Provider loading belongs to ApplicationConfiguration; this adapter only creates database snapshots.</remarks>
public static class DatabaseConfiguration
{
    private static readonly Lazy<DatabaseSettings> Default = new(() => LoadSettings(
        System.Reflection.Assembly.GetEntryAssembly() ?? typeof(DatabaseConfiguration).Assembly));

    /// <summary>Creates a database snapshot using the selected consumer's build environment.</summary>
    /// <param name="applicationAssembly">The executable creating the runtime context.</param>
    /// <param name="contentRoot">Optional directory containing the runtime host defaults.</param>
    /// <returns>A launch snapshot; no SQL connection is opened.</returns>
    public static DatabaseSettings LoadSettings(System.Reflection.Assembly applicationAssembly, string? contentRoot = null)
    {
        var (configuration, error) = ApplicationConfiguration.Load(contentRoot ?? AppContext.BaseDirectory, ApplicationEnvironment.Resolve(applicationAssembly));
        ApplicationDataContext? context = null;
        try
        {
            // Opening inside the adapter lets the snapshot report native access failures safely.
            using var container = new WindowsLocalSettingsContainer(() =>
                (context ??= new ApplicationDataContext("dashe", "FoodHub", new Dhara.Extensions.WinUI.AppRuntimeEnvironment()))
                .OpenSettings("RuntimeSettings"));
            using var localSettings = new LocalSettingsStore(container);
            return new DatabaseSettings(configuration, error, localSettings);
        }
        finally
        {
            context?.Dispose();
            (configuration as IDisposable)?.Dispose();
        }
    }

    /// <summary>Gets the validated connection for a parameterless context.</summary>
    public static string ConnectionString => Default.Value.RequireConnectionString();
    /// <summary>Creates a short-lived context from the parameterless consumer's launch snapshot.</summary>
    public static FoodHub_DevContext Open() => Default.Value.Open();

}

/// <summary>Persists next-launch settings atomically without changing the active database.</summary>
/// <param name="settings">The native settings store shared with appearance and other runtime features.</param>
/// <param name="active">The immutable launch snapshot used by current workflows.</param>
/// <remarks>
/// A saved connection never changes an existing context or authenticated workflow. The next launch removes
/// remembered authentication before consuming the reset marker, preventing restoration against another database.
/// </remarks>
public sealed class DatabaseSettingsStore(ILocalSettingsStore settings, DatabaseSettings active)
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
    /// <param name="cancellation">Cancels waiting or persistence before the native value is committed.</param>
    /// <returns>A task completing after settings and the durable reset marker are saved together.</returns>
    /// <exception cref="DatabaseConfigurationException">The supplied connection is invalid.</exception>
    /// <remarks>This explicit repair action can replace an invalid database group; host files and unrelated local settings are retained.</remarks>
    public async Task SaveAsync(string? connectionString, CancellationToken cancellation = default)
    {
        if (connectionString is not null)
        {
            var candidate = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["ConnectionStrings:FoodHub"] = connectionString }).Build();
            using (candidate as IDisposable) _ = new DatabaseSettings(candidate).RequireConnectionString();
        }
        await settings.UpdateGroupAsync("Database", group =>
        {
            if (connectionString is null) group.Remove("ConnectionString");
            else group["ConnectionString"] = connectionString;
            // One native group commit stores both the override and the durable token-reset request.
            group["ResetSessionOnStartup"] = true;
        }, repairInvalid: true, cancellation);
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
        await settings.UpdateGroupAsync("Database", group => group["ResetSessionOnStartup"] = false);
    }
}