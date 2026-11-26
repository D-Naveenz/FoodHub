using Dhara.Extensions.WinUI.Storage;
using Dhara.Extensions.WinUI.Hosting;
using FoodHub.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FoodHub.Core.Hosting;

/// <summary>Registers FoodHub workflows separately from desktop infrastructure and native UI adapters.</summary>
public static class FoodHubServiceCollectionExtensions
{
    /// <summary>Registers launch-scoped database settings and account workflows without accessing SQL.</summary>
    /// <param name="services">The desktop builder's service collection.</param>
    /// <returns>The same collection for additional registrations.</returns>
    /// <remarks>Contexts remain short-lived: the singleton factory creates one context for each workflow operation.</remarks>
    public static IServiceCollection AddFoodHubCore(this IServiceCollection services)
    {
        services.TryAddSingleton(sp => new DatabaseSettings(sp.GetRequiredService<IConfiguration>(),
            sp.GetRequiredService<ApplicationConfigurationStatus>().Error, sp.GetRequiredService<ILocalSettingsStore>()));
        services.TryAddSingleton<DatabaseSettingsStore>();
        services.TryAddSingleton<DatabaseConnectionTester>();
        services.TryAddSingleton<Func<Models.FoodHub_DevContext>>(sp => sp.GetRequiredService<DatabaseSettings>().Open);
        services.TryAddSingleton<AccountService>();
        services.TryAddSingleton<MenuService>();
        services.TryAddSingleton<OrderingService>();
        services.TryAddSingleton<IOrderingService>(sp => sp.GetRequiredService<OrderingService>());
        services.TryAddSingleton<BasketState>();
        return services;
    }
}
