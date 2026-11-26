using FoodHub.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FoodHub.Core.Services;

/// <summary>Reads the public catalogue with one short-lived context per operation.</summary>
public sealed class MenuService(Func<FoodHub_DevContext> contextFactory)
{
    /// <summary>Loads categories, meals, and store prices without tracking mutable entities.</summary>
    public Task<OrderingResult<Catalogue>> LoadAsync(CancellationToken cancellation = default) => OrderingOperations.Run(async () =>
    {
        await using var db = contextFactory();
        var categories = await db.MenuCategories.AsNoTracking().OrderBy(c => c.DisplayOrder).ThenBy(c => c.CategoryName)
            .Select(c => new CategorySummary(c.MenuCategoryId, c.CategoryName, c.DisplayOrder)).ToListAsync(cancellation);
        var meals = await db.MenuItems.AsNoTracking().OrderBy(m => m.MenuCategory.DisplayOrder).ThenBy(m => m.ItemName)
            .Select(m => new MealDetails(m.MenuItemId, m.MenuCategoryId, m.ItemName, m.ItemDescription, m.UnitPrice, m.PhotoPath, m.IsAvailable)).ToListAsync(cancellation);
        var prices = await OrderingOperations.PricesAsync(db, cancellation);
        return new OrderingResult<Catalogue>(new(categories, meals, prices));
    });

    /// <summary>Refreshes an individual item so a removed item cannot remain orderable through navigation.</summary>
    public Task<OrderingResult<MealDetails>> GetAsync(int id, CancellationToken cancellation = default) => OrderingOperations.Run(async () =>
    {
        await using var db = contextFactory();
        var meal = await db.MenuItems.AsNoTracking().Where(m => m.MenuItemId == id)
            .Select(m => new MealDetails(m.MenuItemId, m.MenuCategoryId, m.ItemName, m.ItemDescription, m.UnitPrice, m.PhotoPath, m.IsAvailable)).SingleOrDefaultAsync(cancellation);
        return meal is null ? new(null, OrderingError.NotFound, "This item is no longer on the menu.") : new OrderingResult<MealDetails>(meal);
    });
}
