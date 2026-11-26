using CommunityToolkit.Mvvm.ComponentModel;
using FoodHub.Core.Services;
using FoodHub.Services;

namespace FoodHub.ViewModels;

/// <summary>Page-owned ordering reads and commands; the coordinator owns dispatched placement.</summary>
public sealed class JourneyViewModel(IOrderingService orders, MenuService menu, BasketState basket,
    SessionState session, CheckoutCoordinator checkout) : ObservableObject, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    /// <summary>The shared preview basket.</summary>
    public BasketState Basket => basket;
    /// <summary>Navigation-independent placement state.</summary>
    public CheckoutCoordinator Checkout => checkout;
    /// <summary>Current safe feedback.</summary>
    public string Message { get; private set; } = "";
    /// <summary>Disables duplicate page operations.</summary>
    public bool IsBusy { get; private set; }
    /// <summary>The most recent owned checkout review.</summary>
    public CheckoutReview? Review { get; private set; }
    /// <summary>Current owned history projection.</summary>
    public IReadOnlyList<OrderSummary> History { get; private set; } = [];
    /// <summary>Current saved receipt.</summary>
    public OrderReceipt? Receipt { get; private set; }
    /// <summary>Fresh public item details, or null when the item was removed.</summary>
    public MealDetails? Meal { get; private set; }
    /// <summary>Refreshes an item independently from any stale navigation snapshot.</summary>
    public Task MealAsync(int id) => Run(async () =>
    {
        var result = await menu.GetAsync(id, _lifetime.Token);
        _lifetime.Token.ThrowIfCancellationRequested();
        Message = result.Message; Meal = result.Succeeded ? result.Value : null;
    });
    /// <summary>Refreshes public basket prices before review.</summary>
    public Task RefreshBasketAsync() => Run(async () =>
    {
        var result = await menu.LoadAsync(_lifetime.Token);
        if (_lifetime.IsCancellationRequested) return;
        Message = result.Message;
        if (result.Succeeded) basket.Reconcile(result.Value!);
    });
    /// <summary>Loads current owned addresses/contact and basket pricing.</summary>
    public Task ReviewAsync() => Run(async () => { await ReadReviewAsync(); });
    private async Task<bool> ReadReviewAsync()
    {
        Review = null;
        var revision = basket.Revision;
        var result = await session.OrderAsync(token => orders.ReviewAsync(token, basket.Capture(), _lifetime.Token));
        _lifetime.Token.ThrowIfCancellationRequested();
        Message = result.Message;
        if (revision != basket.Revision) { Message = "Your basket changed. Review it again."; return false; }
        if (!result.Succeeded) return false;
        Review = result.Value;
        return basket.ApplyReview(Review!);
    }
    /// <summary>Revalidates checkout, requires review of changes, then submits exactly one captured basket.</summary>
    public async Task<int?> PlaceAsync(int? addressId)
    {
        int? placed = null;
        await Run(async () =>
        {
            if (checkout.ConfirmedOrder is int saved) { placed = saved; return; }
            var previous = Review;
            var changed = await ReadReviewAsync();
            if (Review is null) return;
            if (changed || previous is null || previous.Profile.FullName != Review.Profile.FullName || previous.Profile.Phone != Review.Profile.Phone
                || !previous.Profile.Addresses.SequenceEqual(Review.Profile.Addresses))
            { Message = "Checkout details changed. Review the updated information, then confirm again."; return; }
            if (addressId is null || !Review.Profile.Addresses.Any(a => a.Id == addressId)) { Message = "Select one of your saved addresses."; return; }
            if (Review.Lines.Any(l => !l.Available)) { Message = "Remove unavailable items from your basket before ordering."; return; }
            var result = await checkout.PlaceAsync(addressId.Value, basket.Capture());
            _lifetime.Token.ThrowIfCancellationRequested();
            Message = result.Message;
            if (result.Succeeded) placed = result.Value;
        });
        return placed;
    }
    /// <summary>Loads only owned history and records reconciliation of an uncertain placement.</summary>
    public Task HistoryAsync() => Run(async () =>
    {
        History = [];
        var result = await session.OrderAsync(token => orders.HistoryAsync(token, _lifetime.Token));
        _lifetime.Token.ThrowIfCancellationRequested();
        Message = result.Message;
        if (result.Succeeded) { History = result.Value!; checkout.MarkHistoryChecked(); }
    });
    /// <summary>Loads a receipt by an untrusted navigation ID; Core enforces ownership.</summary>
    public Task ReceiptAsync(int id) => Run(async () => { await ReadReceiptAsync(id); });
    private async Task ReadReceiptAsync(int id)
    {
        Receipt = null;
        var result = await session.OrderAsync(token => orders.ReceiptAsync(token, id, _lifetime.Token));
        _lifetime.Token.ThrowIfCancellationRequested();
        Message = result.Message;
        if (result.Succeeded) Receipt = result.Value;
    }
    /// <summary>Applies a demo action and reloads current SQL state even after a conflict.</summary>
    public Task ChangeStatusAsync(int id, byte status) => Run(async () =>
    {
        var result = await session.OrderAsync(token => orders.ChangeStatusAsync(token, id, status, _lifetime.Token));
        await ReadReceiptAsync(id);
        if (!result.Succeeded) Message = result.Message;
    });
    private async Task Run(Func<Task> operation)
    {
        if (IsBusy || _lifetime.IsCancellationRequested) return;
        IsBusy = true; Message = ""; OnPropertyChanged(string.Empty);
        try { await operation(); }
        catch (OperationCanceledException) { }
        finally { IsBusy = false; if (!_lifetime.IsCancellationRequested) OnPropertyChanged(string.Empty); }
    }
    /// <summary>Releases page reads; placement continues in its shared coordinator.</summary>
    public void Dispose() { _lifetime.Cancel(); }
}
