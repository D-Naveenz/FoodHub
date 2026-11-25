using FoodHub.Core.Services;
using Microsoft.EntityFrameworkCore;

// Administrative commands are explicit. The customer application never recreates or migrates SQL on startup.
try
{
    if (args.Length != 1 || args[0] is not ("migrate" or "reset"))
    {
        Console.WriteLine("Usage: FoodHub.Database migrate | reset");
        Console.WriteLine("reset deletes localhost/FoodHub_Dev and all its data, then recreates the current schema. No backup or demo data is created.");
        return 2;
    }
    // Console startup has no generated WinUI entry point to register C#/WinRT wrappers.
    WinRT.ComWrappersSupport.InitializeComWrappers();
    var settings = DatabaseConfiguration.LoadSettings(typeof(Program).Assembly);
    var connection = settings.RequireConnectionString();
    if (args[0] == "reset")
    {
        DevelopmentDatabase.ValidateResetTarget(connection); // Validate before even opening a connection.
        Console.WriteLine("Resetting localhost/FoodHub_Dev; all current development data will be removed.");
        await DevelopmentDatabase.ResetAsync(connection);
        Console.WriteLine("Development database recreated. No demonstration data was loaded.");
    }
    else
    {
        await using var db = settings.Open();
        await db.Database.MigrateAsync();
        Console.WriteLine("Migrations applied. No demonstration data was loaded.");
    }
    return 0;
}
catch (Exception ex)
{
    // Expected configuration/target errors are authored safe messages; SQL exception payloads are never printed.
    Console.Error.WriteLine(ex is DatabaseConfigurationException || ex is InvalidOperationException && ex.Message.StartsWith("Reset is allowed", StringComparison.Ordinal)
        ? ex.Message : $"Database operation failed ({ex.GetType().Name}). Check configuration and server availability; use reset only for the disposable local development database.");
    return 1;
}