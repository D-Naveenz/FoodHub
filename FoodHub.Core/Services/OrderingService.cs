using FoodHub.Core.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FoodHub.Core.Services;

/// <summary>Validates ownership and delegates all order writes to the existing SQL transactions.</summary>
public sealed class OrderingService(Func<FoodHub_DevContext> contextFactory, TimeProvider? clock = null) : IOrderingService
{
    private DateTime UtcNow => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    /// <summary>Loads current prices, availability, contact and owned addresses for explicit checkout review.</summary>
    public Task<OrderingResult<CheckoutReview>> ReviewAsync(string token, IReadOnlyList<BasketQuantity> items, CancellationToken cancellation = default)
        => OrderingOperations.Run(async () =>
        {
            if (!ValidBasket(items)) return Invalid<CheckoutReview>();
            await using var db = contextFactory();
            var owner = await OrderingOperations.OwnerAsync(db, token, UtcNow, cancellation);
            if (owner is null) return Expired<CheckoutReview>();
            var user = await db.Users.AsNoTracking().SingleAsync(u => u.UserId == owner.UserId, cancellation);
            var addresses = await db.CustomerAddresses.AsNoTracking().Where(a => a.CustomerId == owner.CustomerId)
                .OrderBy(a => a.CustomerAddressId).Select(a => new AddressDetails(a.CustomerAddressId, a.AddressLabel, a.AddressLine1, a.AddressLine2, a.City, a.PostalCode)).ToListAsync(cancellation);
            var ids = items.Select(i => i.MealId).ToArray();
            var meals = await db.MenuItems.AsNoTracking().Where(m => ids.Contains(m.MenuItemId)).ToListAsync(cancellation);
            var lines = items.Select(i =>
            {
                var meal = meals.SingleOrDefault(m => m.MenuItemId == i.MealId);
                return new ReviewedLine(i.MealId, meal?.ItemName ?? "Removed menu item", i.Quantity, meal?.UnitPrice ?? 0, meal?.IsAvailable ?? false);
            }).ToArray();
            var profile = new ProfileDetails(user.UserId, owner.CustomerId, user.Email, user.DisplayName, owner.FullName, owner.PhoneNumber, null, addresses);
            return new OrderingResult<CheckoutReview>(new(profile, await OrderingOperations.PricesAsync(db, cancellation), lines));
        });

    /// <summary>Submits once. A transport/cancellation failure after dispatch is uncertain and must never be automatically retried.</summary>
    public Task<OrderingResult<int>> PlaceAsync(string token, int addressId, IReadOnlyList<BasketQuantity> items, CancellationToken cancellation = default)
        => OrderingOperations.Run(async () =>
        {
            if (!ValidBasket(items)) return Invalid<int>();
            await using var db = contextFactory();
            var owner = await OrderingOperations.OwnerAsync(db, token, UtcNow, cancellation);
            if (owner is null) return Expired<int>();
            var output = new OutputParameter<int?>();
            cancellation.ThrowIfCancellationRequested();
            try
            {
                await db.Procedures.PlaceOrderAsync(owner.CustomerId, addressId,
                    items.Select(i => new BasketItemList { MenuItemId = i.MealId, Quantity = (short)i.Quantity }), output, cancellationToken: cancellation);
                return output.Value is int id ? new OrderingResult<int>(id)
                    : new(default, OrderingError.Uncertain, "The order may have been saved. Check refreshed order history before trying again.");
            }
            catch (SqlException ex) when (ex.Number is >= 51001 and <= 51006 or 208 or 207 or 2812 or 2715) { throw; }
            catch (Exception ex) when (ex is SqlException or TimeoutException or OperationCanceledException)
            { return new(default, OrderingError.Uncertain, "The order may have been saved. Check refreshed order history before trying again."); }
        });

    /// <summary>Reads only the validated customer's orders, newest first.</summary>
    public Task<OrderingResult<IReadOnlyList<OrderSummary>>> HistoryAsync(string token, CancellationToken cancellation = default)
        => OrderingOperations.Run<IReadOnlyList<OrderSummary>>(async () =>
        {
            await using var db = contextFactory();
            var owner = await OrderingOperations.OwnerAsync(db, token, UtcNow, cancellation);
            if (owner is null) return Expired<IReadOnlyList<OrderSummary>>();
            var orders = await db.Orders.AsNoTracking().Where(o => o.CustomerId == owner.CustomerId)
                .OrderByDescending(o => o.PlacedAtUtc).ThenByDescending(o => o.OrderId)
                .Select(o => new OrderSummary(o.OrderId, o.OrderNumber, o.OrderStatusId, o.PlacedAtUtc, o.CurrencyCode, o.TotalAmount ?? 0)).ToListAsync(cancellation);
            return new(orders);
        });

    /// <summary>Reloads an owned saved receipt and its timestamped delivery events.</summary>
    public Task<OrderingResult<OrderReceipt>> ReceiptAsync(string token, int orderId, CancellationToken cancellation = default)
        => OrderingOperations.Run(async () =>
        {
            await using var db = contextFactory();
            var owner = await OrderingOperations.OwnerAsync(db, token, UtcNow, cancellation);
            if (owner is null) return Expired<OrderReceipt>();
            var order = await db.Orders.AsNoTracking().Include(o => o.OrderItems).Include(o => o.OrderStatusHistories)
                .AsSplitQuery().SingleOrDefaultAsync(o => o.OrderId == orderId && o.CustomerId == owner.CustomerId, cancellation);
            if (order is null) return new OrderingResult<OrderReceipt>(null, OrderingError.NotFound, "This order could not be found for your account.");
            return new OrderingResult<OrderReceipt>(new(new(order.OrderId, order.OrderNumber, order.OrderStatusId, order.PlacedAtUtc, order.CurrencyCode, order.TotalAmount ?? 0),
                order.CustomerName, order.CustomerPhone, string.Join(", ", new[] { order.DeliveryAddressLine1, order.DeliveryAddressLine2, order.DeliveryCity, order.DeliveryPostalCode }.Where(s => !string.IsNullOrWhiteSpace(s))),
                order.SubtotalAmount, order.DeliveryFee,
                order.OrderItems.OrderBy(i => i.OrderItemId).Select(i => new ReceiptLine(i.ItemName, i.Quantity, i.UnitPrice, i.LineTotal ?? i.Quantity * i.UnitPrice)).ToArray(),
                order.OrderStatusHistories.OrderBy(h => h.ChangedAtUtc).ThenBy(h => h.OrderStatusHistoryId).Select(h => new DeliveryEvent(h.OrderStatusId, h.ChangedAtUtc)).ToArray()));
        });

    /// <summary>Requests a SQL-validated demo stage or cancellation; callers refresh after success or conflict.</summary>
    public Task<OrderingResult<bool>> ChangeStatusAsync(string token, int orderId, byte status, CancellationToken cancellation = default)
        => OrderingOperations.Run(async () =>
        {
            await using var db = contextFactory();
            var owner = await OrderingOperations.OwnerAsync(db, token, UtcNow, cancellation);
            if (owner is null) return Expired<bool>();
            await db.Procedures.UpdateOrderStatusAsync(owner.CustomerId, orderId, status, cancellationToken: cancellation);
            return new OrderingResult<bool>(true);
        });

    /// <summary>Enforces the existing TVP's positive smallint quantity and unique item contracts.</summary>
    public static bool ValidBasket(IReadOnlyList<BasketQuantity> items) => items.Count > 0
        && items.All(i => i.MealId > 0 && i.Quantity is > 0 and <= short.MaxValue)
        && items.Select(i => i.MealId).Distinct().Count() == items.Count;
    private static OrderingResult<T> Invalid<T>() => new(default, OrderingError.Validation, "Choose unique menu items with quantities from 1 to 32767.");
    private static OrderingResult<T> Expired<T>() => new(default, OrderingError.Expired, "Your session has ended. Please sign in again.");
}
