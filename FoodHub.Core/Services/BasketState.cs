using System.ComponentModel;

namespace FoodHub.Core.Services;

/// <summary>A basket line stores browsing values only; SQL supplies the final receipt prices.</summary>
public sealed record BasketLine(MealDetails Meal, int Quantity)
{
    /// <summary>The current preview amount.</summary>
    public decimal Total => Meal.Price * Quantity;
}

/// <summary>Shared in-memory basket. All mutations are made on the consumer's UI dispatcher.</summary>
public sealed class BasketState : INotifyPropertyChanged
{
    private readonly Dictionary<int, BasketLine> _lines = [];
    private int? _owner;
    private StorePrices? _prices;
    /// <summary>Signals any basket/pricing change to all visible presentations.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;
    /// <summary>A detached snapshot, safe to enumerate while views rebuild.</summary>
    public IReadOnlyList<BasketLine> Lines => _lines.Values.ToArray();
    /// <summary>Configured prices, or null until the catalogue is loaded.</summary>
    public StorePrices? Prices => _prices;
    /// <summary>Total units, rather than distinct dishes.</summary>
    public int Count => _lines.Values.Sum(l => l.Quantity);
    /// <summary>Preview food amount using the most recently reviewed prices.</summary>
    public decimal Subtotal => _lines.Values.Sum(l => l.Total);
    /// <summary>No delivery fee is charged to an empty basket.</summary>
    public decimal DeliveryFee => Count == 0 ? 0 : _prices?.DeliveryFee ?? 0;
    /// <summary>The preview amount; checkout remains disabled until prices are known.</summary>
    public decimal Total => Subtotal + DeliveryFee;
    /// <summary>Monotonic revision detects basket edits during asynchronous checkout preparation.</summary>
    public int Revision { get; private set; }
    /// <summary>Returns the confirmed quantity for a meal.</summary>
    public int Quantity(int id) => _lines.TryGetValue(id, out var line) ? line.Quantity : 0;
    /// <summary>Replaces a quantity; zero removes it and unavailable food cannot be newly added.</summary>
    public void Set(MealDetails meal, int quantity)
    {
        if (quantity is < 0 or > short.MaxValue) throw new ArgumentOutOfRangeException(nameof(quantity));
        if (quantity > 0 && !meal.Available) throw new InvalidOperationException("This item is unavailable.");
        if (quantity == 0) _lines.Remove(meal.Id); else _lines[meal.Id] = new(meal, quantity);
        Changed();
    }
    /// <summary>Removes all preview items without touching SQL.</summary>
    public void Clear() { _lines.Clear(); Changed(); }
    /// <summary>Tracks account identity independently from profile refresh notifications.</summary>
    public void ChangeOwner(int? customerId)
    {
        if (_owner == customerId) return;
        // A guest basket is carried into first sign-in; leaving/replacing an account clears it.
        if (_owner is not null) Clear();
        _owner = customerId;
    }
    /// <summary>Updates browsing prices while retaining removed/unavailable lines for explicit customer removal.</summary>
    public void Reconcile(Catalogue catalogue)
    {
        _prices = catalogue.Prices;
        foreach (var line in Lines)
        {
            var meal = catalogue.Meals.FirstOrDefault(m => m.Id == line.Meal.Id) ?? line.Meal with { Available = false };
            _lines[meal.Id] = new(meal, line.Quantity);
        }
        Changed();
    }
    /// <summary>Adopts the exact review shown to the customer, returning whether its amounts or availability changed.</summary>
    public bool ApplyReview(CheckoutReview review)
    {
        var changed = _prices != review.Prices;
        _prices = review.Prices;
        foreach (var line in review.Lines)
        {
            if (!_lines.TryGetValue(line.MealId, out var old)) continue;
            changed |= old.Meal.Price != line.Price || old.Meal.Available != line.Available || old.Meal.Name != line.Name;
            _lines[line.MealId] = new(old.Meal with { Price = line.Price, Available = line.Available, Name = line.Name }, old.Quantity);
        }
        Changed();
        return changed;
    }
    /// <summary>Captures unique TVP inputs for one checkout attempt.</summary>
    public IReadOnlyList<BasketQuantity> Capture() => Lines.Select(l => new BasketQuantity(l.Meal.Id, l.Quantity)).ToArray();
    private void Changed() { Revision++; PropertyChanged?.Invoke(this, new(null)); }
}
