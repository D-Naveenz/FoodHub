using FoodHub.Core.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FoodHub.Core.Services;

/// <summary>A safe configuration failure without connection details.</summary>
public sealed class DatabaseConfigurationException(string message) : InvalidOperationException(message);

/// <summary>Central paths for desktop storage, configuration, and administrative tools.</summary>
public sealed class ApplicationPaths
{
    /// <summary>Overrides allow tests to remain isolated from customer data.</summary>
    public ApplicationPaths(string? installDirectory = null, string? userDirectory = null)
    {
        InstallDirectory = installDirectory ?? AppContext.BaseDirectory;
        UserDirectory = userDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FoodHub");
    }
    /// <summary>Application content, independent of the working directory.</summary>
    public string InstallDirectory { get; }
    /// <summary>Writable data root; the UI supplies package LocalFolder while standalone tools default to LocalAppData/FoodHub.</summary>
    public string UserDirectory { get; }
    /// <summary>Persistent overrides and session-reset marker.</summary>
    public string SettingsFile => Path.Combine(UserDirectory, "appsettings.json");
}

/// <summary>An immutable database configuration snapshot for one launch.</summary>
public sealed class DatabaseSettings
{
    /// <summary>Captures values once so saving never changes active operations.</summary>
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
    public FoodHub_DevContext Open() => new(new DbContextOptionsBuilder<FoodHub_DevContext>()
        .UseSqlServer(RequireConnectionString()).Options);
}

/// <summary>Shared JSON/environment loader and EF design-time factory.</summary>
public sealed class DatabaseConfiguration : IDesignTimeDbContextFactory<FoodHub_DevContext>
{
    private static readonly Lazy<DatabaseSettings> Default = new(() =>
    {
        var (configuration, error) = Load(new ApplicationPaths(), EnvironmentName);
        using (configuration as IDisposable) return new DatabaseSettings(configuration, error);
    });
    /// <summary>Debug defaults to Development and Release to Production; DOTNET_ENVIRONMENT overrides.</summary>
    public static string EnvironmentName => Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") is { Length: > 0 } name ? name :
#if DEBUG
        "Development";
#else
        "Production";
#endif
    /// <summary>Loads providers in priority order without watchers or working-directory assumptions.</summary>
    public static (IConfigurationRoot Configuration, string? Error) Load(ApplicationPaths paths, string environment)
    {
        if (environment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || environment.Contains('/') || environment.Contains('\\'))
            return (new ConfigurationBuilder().Build(), "The host environment name is invalid.");
        var builder = new ConfigurationBuilder().SetBasePath(paths.InstallDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables();
        // Preserve the old alias; per-user settings always have final priority.
        if (Environment.GetEnvironmentVariable("FOODHUB_CONNECTION_STRING") is { } legacy)
            builder.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:FoodHub"] = legacy });
        builder.AddJsonFile(paths.SettingsFile, optional: true, reloadOnChange: false);
        try { return (builder.Build(), null); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or FormatException or JsonException)
        {
            // A broken override must never silently fall through to a different database.
            return (new ConfigurationBuilder().Build(), "Database settings could not be read. Repair or reset them in Settings, then restart FoodHub.");
        }
    }
    /// <summary>Connection for parameterless contexts and administrative tools.</summary>
    public static string ConnectionString => Default.Value.RequireConnectionString();
    /// <summary>Creates a short-lived context from the shared snapshot.</summary>
    public static FoodHub_DevContext Open() => Default.Value.Open();
    /// <summary>Creates an EF context without applying migrations.</summary>
    public FoodHub_DevContext CreateDbContext(string[] args) => Open();
}

/// <summary>Persists next-launch settings atomically without changing the active database.</summary>
public sealed class DatabaseSettingsStore(ILocalStorageService storage, DatabaseSettings active)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    /// <summary>Survives Settings navigation until restart.</summary>
    public bool RestartRequired { get; private set; }
    /// <summary>Saved candidate shown when the user returns to Settings before restarting.</summary>
    public string? PendingConnectionString { get; private set; }
    /// <summary>Builds Windows-authentication settings using SQL's connection-string builder.</summary>
    public static string BuildConnectionString(string server, string database, bool trustCertificate)
    {
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(database))
            throw new DatabaseConfigurationException("Enter a server and database name.");
        return new SqlConnectionStringBuilder { DataSource = server.Trim(), InitialCatalog = database.Trim(),
            IntegratedSecurity = true, TrustServerCertificate = trustCertificate, ConnectTimeout = 5 }.ConnectionString;
    }
    /// <summary>Saves an override, or removes it to use defaults; no SQL availability is required.</summary>
    public async Task SaveAsync(string? connectionString, CancellationToken cancellation = default)
    {
        if (connectionString is not null)
        {
            var candidate = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["ConnectionStrings:FoodHub"] = connectionString }).Build();
            using (candidate as IDisposable) _ = new DatabaseSettings(candidate).RequireConnectionString();
        }
        await _gate.WaitAsync(cancellation);
        try
        {
            var json = await ReadForRepairAsync(cancellation);
            if (json["ConnectionStrings"] is not JsonObject) json["ConnectionStrings"] = new JsonObject();
            var connections = (JsonObject)json["ConnectionStrings"]!;
            if (connectionString is null) connections.Remove("FoodHub");
            else connections["FoodHub"] = connectionString;
            if (json["FoodHub"] is not JsonObject) json["FoodHub"] = new JsonObject();
            // Reset on next launch, including after switching back to defaults.
            json["FoodHub"]!["ResetSessionOnStartup"] = true;
            await WriteAsync(json, cancellation);
            PendingConnectionString = connectionString;
            RestartRequired = true;
        }
        finally { _gate.Release(); }
    }
    /// <summary>Deletes remembered authentication before consuming its durable reset marker.</summary>
    public async Task PrepareSessionAsync(Func<Task> removeToken)
    {
        if (active.LoadError is not null) throw new DatabaseConfigurationException(active.LoadError);
        if (!active.ResetSessionOnStartup) return;
        await removeToken(); // Failure leaves the marker intact and suppresses restoration.
        await _gate.WaitAsync();
        try
        {
            var json = await ReadForRepairAsync(CancellationToken.None);
            if (json["FoodHub"] is not JsonObject) json["FoodHub"] = new JsonObject();
            json["FoodHub"]!["ResetSessionOnStartup"] = false;
            await WriteAsync(json, CancellationToken.None);
        }
        finally { _gate.Release(); }
    }
    /// <summary>Retains unrelated settings; an explicit Save/Reset can repair malformed JSON.</summary>
    private async Task<JsonObject> ReadForRepairAsync(CancellationToken cancellation)
    {
        var bytes = await storage.ReadAsync("appsettings.json", cancellation);
        if (bytes is null) return new JsonObject();
        try { return JsonNode.Parse(bytes) as JsonObject ?? new JsonObject(); }
        catch (JsonException) { return new JsonObject(); }
    }
    /// <summary>Replaces the destination only after a complete file is ready on the same volume.</summary>
    private async Task WriteAsync(JsonObject json, CancellationToken cancellation)
    {
        await storage.WriteAsync("appsettings.json", Encoding.UTF8.GetBytes(
            json.ToJsonString(new JsonSerializerOptions { WriteIndented = true })), cancellation);
    }
}