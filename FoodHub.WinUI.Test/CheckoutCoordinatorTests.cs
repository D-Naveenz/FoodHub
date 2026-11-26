using FoodHub.Core.Services;
using FoodHub.Services;

namespace FoodHub.WinUI.Test;

/// <summary>Exercises write lifetime, duplicate suppression and explicit uncertain-outcome reconciliation.</summary>
[TestClass]
[TestCategory("Unit")]
public sealed class CheckoutCoordinatorTests
{
    /// <summary>Two page submissions share one dispatched request and a confirmed identity is reused until shown.</summary>
    [TestMethod]
    public Task ConcurrentSubmissionsDispatchOnce() => WithSession(async (session, basket, gateway, coordinator) =>
    {
        basket.Set(new(1, 1, "Meal", null, 100, null, true), 2);
        var first = coordinator.PlaceAsync(1, basket.Capture());
        var second = coordinator.PlaceAsync(1, basket.Capture());
        Assert.AreSame(first, second);
        await WinUITestSupport.NextTurn();
        Assert.AreEqual(1, gateway.Calls); Assert.IsTrue(coordinator.IsPlacing);
        gateway.Completion.SetResult(new(42));
        Assert.AreEqual(42, (await first).Value);
        Assert.AreEqual(0, basket.Count); Assert.AreEqual(42, coordinator.ConfirmedOrder);
        Assert.AreEqual(42, (await coordinator.PlaceAsync(1, [])).Value);
        Assert.AreEqual(1, gateway.Calls);
        coordinator.ReceiptShown(42); Assert.IsNull(coordinator.ConfirmedOrder);
    });

    /// <summary>An uncertain write keeps the basket and cannot retry until an explicit acknowledgement follows history.</summary>
    [TestMethod]
    public Task UncertainWriteRequiresHistoryAndAcknowledgement() => WithSession(async (session, basket, gateway, coordinator) =>
    {
        basket.Set(new(1, 1, "Meal", null, 100, null, true), 1);
        var pending = coordinator.PlaceAsync(1, basket.Capture());
        gateway.Completion.SetResult(new(default, OrderingError.Uncertain, "Check history."));
        Assert.AreEqual(OrderingError.Uncertain, (await pending).Error);
        Assert.AreEqual(1, basket.Count); Assert.IsTrue(coordinator.Uncertain);
        coordinator.AcknowledgeUncertain(); Assert.IsTrue(coordinator.Uncertain);
        Assert.AreEqual(OrderingError.Uncertain, (await coordinator.PlaceAsync(1, basket.Capture())).Error);
        Assert.AreEqual(1, gateway.Calls);
        coordinator.MarkHistoryChecked(); coordinator.AcknowledgeUncertain(); Assert.IsFalse(coordinator.Uncertain);
    });

    /// <summary>A late write from an old account cannot clear the new customer's basket or expose a receipt ID.</summary>
    [TestMethod]
    public Task AccountReplacementDiscardsPlacementResult() => WithSession(async (session, basket, gateway, coordinator) =>
    {
        basket.Set(new(1, 1, "Meal", null, 100, null, true), 1);
        var pending = coordinator.PlaceAsync(1, basket.Capture());
        await WinUITestSupport.NextTurn();
        await session.AcceptAsync(Ticket(2), false);
        basket.Set(new(2, 1, "New meal", null, 50, null, true), 3);
        gateway.Completion.SetResult(new(42));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => pending);
        Assert.IsNull(coordinator.ConfirmedOrder); Assert.AreEqual(3, basket.Count);
    });

    /// <summary>Changing accounts before the queued write starts must not submit old items as the new customer.</summary>
    [TestMethod]
    public Task AccountReplacementBeforeDispatchPreventsWrite() => WithSession(async (session, basket, gateway, coordinator) =>
    {
        basket.Set(new(1, 1, "Meal", null, 100, null, true), 1);
        var pending = coordinator.PlaceAsync(1, basket.Capture());
        // AcceptAsync publishes identity synchronously before its storage await, ahead of the queued dispatch.
        var accepted = session.AcceptAsync(Ticket(2), false);
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => pending);
        await accepted;
        Assert.AreEqual(0, gateway.Calls); Assert.AreEqual(0, basket.Count);
    });

    private static Task WithSession(Func<SessionState, BasketState, ControlledOrdering, CheckoutCoordinator, Task> action)
        => WinUITestSupport.OnDispatcher(async () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "FoodHub-checkout-tests-" + Guid.NewGuid().ToString("N"));
            try
            {
                using var session = new SessionState(new AccountService(() => throw new InvalidOperationException()), new SessionStorage(directory));
                await session.AcceptAsync(Ticket(1), false);
                var basket = new BasketState(); basket.ChangeOwner(1);
                var gateway = new ControlledOrdering();
                using var coordinator = new CheckoutCoordinator(session, gateway, basket);
                await action(session, basket, gateway, coordinator);
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory); }
        });
    private static SessionTicket Ticket(int customer) => new(new string('A', 64), DateTime.UtcNow.AddHours(1),
        new(customer, customer, "fictional@example.test", "Example", "Example customer", "0770000000", null, []));

    /// <summary>A controlled dispatch boundary lets tests model an in-flight write without timing sleeps.</summary>
    private sealed class ControlledOrdering : IOrderingService
    {
        internal int Calls { get; private set; }
        internal TaskCompletionSource<OrderingResult<int>> Completion { get; } = new();
        public Task<OrderingResult<int>> PlaceAsync(string token, int addressId, IReadOnlyList<BasketQuantity> items, CancellationToken cancellation = default)
        { Calls++; return Completion.Task; }
        public Task<OrderingResult<CheckoutReview>> ReviewAsync(string token, IReadOnlyList<BasketQuantity> items, CancellationToken cancellation = default) => throw new NotSupportedException();
        public Task<OrderingResult<IReadOnlyList<OrderSummary>>> HistoryAsync(string token, CancellationToken cancellation = default) => throw new NotSupportedException();
        public Task<OrderingResult<OrderReceipt>> ReceiptAsync(string token, int orderId, CancellationToken cancellation = default) => throw new NotSupportedException();
        public Task<OrderingResult<bool>> ChangeStatusAsync(string token, int orderId, byte status, CancellationToken cancellation = default) => throw new NotSupportedException();
    }
}
