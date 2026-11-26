using FoodHub.Core.Services;
using FoodHub.Services;
using FoodHub.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;

namespace FoodHub.WinUI.Test;

/// <summary>Exercises basket totals, account boundaries, filters and delayed session operations.</summary>
[TestClass]
[TestCategory("Unit")]
public sealed class BasketTests
{
    private static MealDetails Meal(int id = 1, decimal price = 100, bool available = true) => new(id, 1, "Test meal", "Description", price, null, available);
    /// <summary>Quantities replace rather than increment; delivery applies only to a nonempty basket.</summary>
    [TestMethod]
    public void QuantitiesAndTotalsStayConsistent()
    {
        var basket = new BasketState();
        basket.Reconcile(new([], new[] { Meal() }, new("LKR", 250)));
        Assert.AreEqual(0m, basket.Total);
        basket.Set(Meal(), 2); Assert.AreEqual(450m, basket.Total);
        basket.Set(Meal(), 3); Assert.AreEqual(3, basket.Count); Assert.AreEqual(550m, basket.Total);
        basket.Set(Meal(2, 50), 2); Assert.AreEqual(650m, basket.Total);
        basket.Set(Meal(), 0); Assert.AreEqual(350m, basket.Total);
        basket.Clear(); Assert.AreEqual(0m, basket.Total); Assert.AreEqual(0, basket.Capture().Count);
    }
    /// <summary>The TVP's range and availability rules apply before a request reaches SQL.</summary>
    [TestMethod]
    public void InvalidBasketInputsAreRejected()
    {
        Assert.IsFalse(OrderingService.ValidBasket([]));
        Assert.IsFalse(OrderingService.ValidBasket(new BasketQuantity[] { new(1, 0) }));
        Assert.IsFalse(OrderingService.ValidBasket(new BasketQuantity[] { new(1, 32768) }));
        Assert.IsFalse(OrderingService.ValidBasket(new BasketQuantity[] { new(1, 1), new(1, 2) }));
        Assert.IsTrue(OrderingService.ValidBasket(new BasketQuantity[] { new(1, 32767) }));
        var basket = new BasketState();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => basket.Set(Meal(), -1));
        Assert.ThrowsExactly<InvalidOperationException>(() => basket.Set(Meal(available: false), 1));
    }
    /// <summary>Guest sign-in carries items, profile updates preserve them, and account departure clears them.</summary>
    [TestMethod]
    public void AccountBoundariesPreserveOnlyGuestSignIn()
    {
        var basket = new BasketState(); basket.Set(Meal(), 2);
        basket.ChangeOwner(10); Assert.AreEqual(2, basket.Count);
        basket.ChangeOwner(10); Assert.AreEqual(2, basket.Count);
        basket.ChangeOwner(20); Assert.AreEqual(0, basket.Count);
        basket.Set(Meal(), 1); basket.ChangeOwner(null); Assert.AreEqual(0, basket.Count);
    }
    /// <summary>A review updates prices without silently dropping unavailable or removed lines.</summary>
    [TestMethod]
    public void ReconciliationRetainsLinesForExplicitRemoval()
    {
        var basket = new BasketState(); basket.Set(Meal(), 2);
        basket.Reconcile(new([], [], new("LKR", 250)));
        Assert.IsFalse(basket.Lines.Single().Meal.Available); Assert.AreEqual(2, basket.Count);
        var profile = new ProfileDetails(1, 1, "a@example.test", "A", "A", "0110000000", null, []);
        var review = new CheckoutReview(profile, new("LKR", 300), new ReviewedLine[] { new(1, "Test meal", 2, 125, true) });
        Assert.IsTrue(basket.ApplyReview(review)); Assert.AreEqual(550m, basket.Total);
        Assert.IsFalse(basket.ApplyReview(review));
    }
    /// <summary>Category, availability, name search and price order combine predictably.</summary>
    [TestMethod]
    public void FiltersAndSortingCompose()
    {
        MealDetails[] meals = [Meal() with { Name = "Rice" }, Meal(2, 50) with { Name = "Lime", CategoryId = 2 }, Meal(3, 150, false) with { Name = "Rice bowl" }];
        var state = new MenuBrowseState { Search = " RICE ", Sort = 3 };
        CollectionAssert.AreEqual(new[] { 3, 1 }, MenuViewModel.Filter(meals, state).Select(m => m.Id).ToArray());
        state.AvailableOnly = true; Assert.AreEqual(1, MenuViewModel.Filter(meals, state).Single().Id);
        state.Category = 2; Assert.AreEqual(0, MenuViewModel.Filter(meals, state).Count);
    }
    /// <summary>Terminal states have no forward demo action.</summary>
    [TestMethod]
    public void DeliveryTransitionsAreReadableAndTerminal()
    {
        Assert.AreEqual((byte)20, DeliveryStages.Next(10)); Assert.AreEqual((byte)40, DeliveryStages.Next(30));
        Assert.IsNull(DeliveryStages.Next(40)); Assert.IsNull(DeliveryStages.Next(90));
        Assert.AreEqual("Cancelled", DeliveryStages.Label(90));
    }
    /// <summary>An obsolete account operation cannot publish its delayed result.</summary>
    [TestMethod]
    public Task AccountChangeDiscardsLateResults() => WinUITestSupport.OnDispatcher(async () =>
    {
        var directory = Path.Combine(Path.GetTempPath(), "FoodHub-order-state-" + Guid.NewGuid().ToString("N"));
        try
        {
            var accounts = new AccountService(() => throw new InvalidOperationException());
            using var session = new SessionState(accounts, new SessionStorage(directory));
            var profile = new ProfileDetails(1, 1, "a@example.test", "A", "A", "0110000000", null, []);
            await session.AcceptAsync(new(new string('A', 64), DateTime.UtcNow.AddHours(1), profile), false);
            var deferred = new TaskCompletionSource<OrderingResult<int>>();
            var pending = session.OrderAsync(_ => deferred.Task);
            await session.AcceptAsync(new(new string('B', 64), DateTime.UtcNow.AddHours(1), profile with { CustomerId = 2 }), false);
            deferred.SetResult(new(99));
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => pending);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory); }
    });
}
