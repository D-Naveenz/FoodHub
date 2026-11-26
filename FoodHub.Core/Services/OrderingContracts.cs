namespace FoodHub.Core.Services;

/// <summary>Session-owned ordering operations, independent of view lifetimes and SQL implementation details.</summary>
public interface IOrderingService
{
    /// <summary>Reviews current menu prices and the session owner's delivery details.</summary>
    Task<OrderingResult<CheckoutReview>> ReviewAsync(string token, IReadOnlyList<BasketQuantity> items, CancellationToken cancellation = default);
    /// <summary>Dispatches one order write; an uncertain result must be reconciled explicitly.</summary>
    Task<OrderingResult<int>> PlaceAsync(string token, int addressId, IReadOnlyList<BasketQuantity> items, CancellationToken cancellation = default);
    /// <summary>Returns owned order cards newest first.</summary>
    Task<OrderingResult<IReadOnlyList<OrderSummary>>> HistoryAsync(string token, CancellationToken cancellation = default);
    /// <summary>Returns only an owned receipt and its saved events.</summary>
    Task<OrderingResult<OrderReceipt>> ReceiptAsync(string token, int orderId, CancellationToken cancellation = default);
    /// <summary>Requests a validated demo stage or cancellation.</summary>
    Task<OrderingResult<bool>> ChangeStatusAsync(string token, int orderId, byte status, CancellationToken cancellation = default);
}

/// <summary>Expected ordering failures; Uncertain means a submitted write may have committed.</summary>
public enum OrderingError { None, Validation, Expired, Configuration, Database, Conflict, NotFound, Uncertain }

/// <summary>A safe workflow outcome without database exception payloads.</summary>
public sealed record OrderingResult<T>(T? Value, OrderingError Error = OrderingError.None, string Message = "")
{
    /// <summary>Whether the operation completed successfully.</summary>
    public bool Succeeded => Error == OrderingError.None;
}

/// <summary>A displayable category, in the takeaway's configured order.</summary>
public sealed record CategorySummary(int Id, string Name, int DisplayOrder);
/// <summary>A detached menu item; views never retain an EF entity.</summary>
public sealed record MealDetails(int Id, int CategoryId, string Name, string? Description, decimal Price, string? PhotoPath, bool Available);
/// <summary>The single outlet's current pricing settings.</summary>
public sealed record StorePrices(string Currency, decimal DeliveryFee);
/// <summary>One consistent browsing result.</summary>
public sealed record Catalogue(IReadOnlyList<CategorySummary> Categories, IReadOnlyList<MealDetails> Meals, StorePrices Prices);
/// <summary>A requested quantity. Zero belongs only in the editor and never in a submitted basket.</summary>
public sealed record BasketQuantity(int MealId, int Quantity);
/// <summary>A current checkout line, including missing or unavailable items.</summary>
public sealed record ReviewedLine(int MealId, string Name, int Quantity, decimal Price, bool Available);
/// <summary>A fresh checkout projection owned by the validated customer.</summary>
public sealed record CheckoutReview(ProfileDetails Profile, StorePrices Prices, IReadOnlyList<ReviewedLine> Lines)
{
    /// <summary>The current menu subtotal, before delivery.</summary>
    public decimal Subtotal => Lines.Sum(l => l.Price * l.Quantity);
    /// <summary>The full amount payable on delivery.</summary>
    public decimal Total => Subtotal + Prices.DeliveryFee;
}
/// <summary>An immutable historical receipt line.</summary>
public sealed record ReceiptLine(string Name, int Quantity, decimal Price, decimal Total);
/// <summary>A persisted delivery event with its UTC timestamp.</summary>
public sealed record DeliveryEvent(byte Status, DateTime AtUtc);
/// <summary>An owned order card derived from saved receipt values.</summary>
public sealed record OrderSummary(int Id, string Number, byte Status, DateTime PlacedUtc, string Currency, decimal Total);
/// <summary>The full receipt; no live profile or menu values replace its snapshots.</summary>
public sealed record OrderReceipt(OrderSummary Summary, string CustomerName, string Phone, string Address,
    decimal Subtotal, decimal DeliveryFee, IReadOnlyList<ReceiptLine> Lines, IReadOnlyList<DeliveryEvent> Events);

/// <summary>Shared labels and next-stage rules for the explicitly simulated delivery experience.</summary>
public static class DeliveryStages
{
    /// <summary>Readable text that does not depend on color.</summary>
    public static string Label(byte status) => status switch
    {
        10 => "Order placed", 20 => "Preparing", 30 => "Out for delivery", 40 => "Delivered", 90 => "Cancelled", _ => "Unknown status"
    };
    /// <summary>The one permitted forward demo transition, or null for a terminal state.</summary>
    public static byte? Next(byte status) => status switch { 10 => 20, 20 => 30, 30 => 40, _ => null };
}
