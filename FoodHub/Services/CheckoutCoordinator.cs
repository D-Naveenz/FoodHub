using CommunityToolkit.Mvvm.ComponentModel;
using FoodHub.Core.Services;
using System.ComponentModel;

namespace FoodHub.Services;

/// <summary>Owns one placement attempt independently of page navigation, preventing duplicate writes.</summary>
public sealed class CheckoutCoordinator : ObservableObject, IDisposable
{
    private readonly SessionState _session;
    private readonly IOrderingService _orders;
    private readonly BasketState _basket;
    private Task<OrderingResult<int>>? _attempt;
    private int? _customer;
    private bool _uncertain;
    private bool _historyChecked;
    private int? _confirmedOrder;
    /// <summary>Creates a launch-lifetime placement coordinator.</summary>
    public CheckoutCoordinator(SessionState session, IOrderingService orders, BasketState basket)
    {
        _session = session; _orders = orders; _basket = basket;
        _customer = session.Profile?.CustomerId;
        basket.ChangeOwner(_customer);
        session.PropertyChanged += SessionChanged;
    }
    /// <summary>True while a write is running, even if its originating page is gone.</summary>
    public bool IsPlacing => _attempt is { IsCompleted: false };
    /// <summary>A transport failure requires explicit reconciliation before another write.</summary>
    public bool Uncertain => _uncertain;
    /// <summary>Whether history has been successfully refreshed after the uncertain attempt.</summary>
    public bool HistoryChecked => _historyChecked;
    /// <summary>Saved order identity, retained until its receipt has been presented.</summary>
    public int? ConfirmedOrder => _confirmedOrder;

    /// <summary>Returns the existing attempt or confirmed ID instead of dispatching a second order.</summary>
    public Task<OrderingResult<int>> PlaceAsync(int addressId, IReadOnlyList<BasketQuantity> items)
    {
        if (_attempt is { IsCompleted: false }) return _attempt;
        if (_confirmedOrder is int id) return Task.FromResult(new OrderingResult<int>(id));
        if (_uncertain) return Task.FromResult(new OrderingResult<int>(default, OrderingError.Uncertain, "Check refreshed order history and explicitly acknowledge the previous attempt before trying again."));
        // RunAsync yields before starting so the task field and busy state are published first.
        _attempt = RunAsync(addressId, items, _session.Generation);
        Notify();
        return _attempt;
    }

    private async Task<OrderingResult<int>> RunAsync(int addressId, IReadOnlyList<BasketQuantity> items, int generation)
    {
        await Task.Yield();
        try
        {
            // Account replacement can occur even before the yielded dispatch starts.
            if (generation != _session.Generation) throw new OperationCanceledException();
            // Page cancellation never cancels a dispatched write. Session versioning still hides late results.
            var result = await _session.OrderAsync(token => _orders.PlaceAsync(token, addressId, items));
            if (generation != _session.Generation) throw new OperationCanceledException();
            if (result.Succeeded) { _confirmedOrder = result.Value; _basket.Clear(); }
            _uncertain = result.Error == OrderingError.Uncertain;
            _historyChecked = false;
            return result;
        }
        finally { _attempt = null; Notify(); }
    }
    /// <summary>Marks a successful owned history read; failure never unlocks uncertain placement.</summary>
    public void MarkHistoryChecked() { if (_uncertain) { _historyChecked = true; Notify(); } }
    /// <summary>Allows a deliberate new attempt only after viewing refreshed history and acknowledging duplicate risk.</summary>
    public void AcknowledgeUncertain() { if (_historyChecked) { _uncertain = false; _historyChecked = false; Notify(); } }
    /// <summary>Releases a confirmed identity only after its receipt was shown.</summary>
    public void ReceiptShown(int id) { if (_confirmedOrder == id) { _confirmedOrder = null; Notify(); } }
    private void SessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SessionState.Profile)) return;
        var customer = _session.Profile?.CustomerId;
        _basket.ChangeOwner(customer);
        if (_customer != customer) { _confirmedOrder = null; _uncertain = false; _historyChecked = false; _customer = customer; Notify(); }
    }
    private void Notify() => OnPropertyChanged(string.Empty);
    /// <summary>Detaches account observation at application shutdown.</summary>
    public void Dispose() => _session.PropertyChanged -= SessionChanged;
}

/// <summary>Public browsing preferences survive temporary navigation to an item page.</summary>
public sealed class MenuBrowseState
{
    /// <summary>Name search text.</summary>
    public string Search { get; set; } = "";
    /// <summary>Selected category, or null for All.</summary>
    public int? Category { get; set; }
    /// <summary>Whether unavailable meals are hidden.</summary>
    public bool AvailableOnly { get; set; }
    /// <summary>Menu order, name, ascending price, or descending price.</summary>
    public int Sort { get; set; }
    /// <summary>Last vertical scroll offset.</summary>
    public double Offset { get; set; }
    /// <summary>Item whose image should receive the return animation.</summary>
    public int? ReturningMeal { get; set; }
}
