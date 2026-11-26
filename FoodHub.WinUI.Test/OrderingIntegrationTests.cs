using FoodHub.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace FoodHub.WinUI.Test;

/// <summary>Verifies the new ordering service against real SQL transactions in an isolated database.</summary>
[TestClass]
[TestCategory("SqlIntegration")]
public sealed class OrderingIntegrationTests
{
    /// <summary>Ownership, validation, snapshots and competing status requests cannot corrupt saved orders.</summary>
    [TestMethod]
    public async Task FullOwnedJourneyAndCompetingTransitions()
    {
        var connection = Environment.GetEnvironmentVariable("FOODHUB_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Inconclusive("Set FOODHUB_TEST_CONNECTION_STRING for isolated SQL integration.");
        await using var target = await SqlTestDatabase.CreateAsync(connection);
        await using var db = target.Open(); await db.Database.MigrateAsync();
        var accounts = new AccountService(target.Open);
        var a = (await accounts.RegisterAsync(new("Ada", "Ada Example", "ada@example.test", "0770000000", "Fictional password 123!", "Fictional password 123!", false))).Value!;
        var b = (await accounts.RegisterAsync(new("Bea", "Bea Example", "bea@example.test", "0770000001", "Fictional password 123!", "Fictional password 123!", false))).Value!;
        var address = (await accounts.SaveAddressAsync(a.Token, new(null, "Home", "1 Example Road", null, "Colombo", null))).Value!.Profile.Addresses.Single().Id;
        await db.Database.ExecuteSqlRawAsync("INSERT dbo.MenuCategories(CategoryName) VALUES(N'Mains'); INSERT dbo.MenuItems(MenuCategoryId,ItemName,UnitPrice) SELECT MenuCategoryId,N'Fictional meal',100 FROM dbo.MenuCategories;");
        var meal = await db.MenuItems.SingleAsync();
        var menu = await new MenuService(target.Open).LoadAsync(); Assert.IsTrue(menu.Succeeded);
        var orders = new OrderingService(target.Open);
        BasketQuantity[] items = [new(meal.MenuItemId, 2)];
        Assert.IsTrue((await orders.ReviewAsync(a.Token, items)).Succeeded);
        Assert.IsFalse((await orders.PlaceAsync(a.Token, address, [])).Succeeded);
        Assert.IsFalse((await orders.PlaceAsync(b.Token, address, items)).Succeeded);
        meal.IsAvailable = false; await db.SaveChangesAsync();
        Assert.IsFalse((await orders.PlaceAsync(a.Token, address, items)).Succeeded);
        Assert.AreEqual(0, await db.Orders.CountAsync());
        meal.IsAvailable = true; await db.SaveChangesAsync();
        var saved = await orders.PlaceAsync(a.Token, address, items); Assert.IsTrue(saved.Succeeded, saved.Message);
        var receipt = (await orders.ReceiptAsync(a.Token, saved.Value)).Value!;
        Assert.AreEqual(450m, receipt.Summary.Total); Assert.AreEqual(1, receipt.Events.Count);
        Assert.IsFalse((await orders.ReceiptAsync(b.Token, saved.Value)).Succeeded);
        Assert.AreEqual(0, (await orders.HistoryAsync(b.Token)).Value!.Count);
        Assert.IsFalse((await orders.ChangeStatusAsync(b.Token, saved.Value, 20)).Succeeded);
        meal.ItemName = "Changed meal"; meal.UnitPrice = 999;
        (await db.StoreSettings.SingleAsync()).DeliveryFee = 999; await db.SaveChangesAsync();
        await accounts.UpdateBasicsAsync(a.Token, new("Changed", "Changed customer", "0770000002"));
        await accounts.SaveAddressAsync(a.Token, new(address, "Home", "Changed road", null, "City", null));
        receipt = (await orders.ReceiptAsync(a.Token, saved.Value)).Value!;
        Assert.AreEqual("Fictional meal", receipt.Lines.Single().Name); Assert.AreEqual(450m, receipt.Summary.Total);
        Assert.AreEqual("Ada Example", receipt.CustomerName); Assert.IsTrue(receipt.Address.Contains("1 Example Road"));
        Assert.IsFalse((await orders.ChangeStatusAsync(a.Token, saved.Value, 40)).Succeeded);
        var race = await Task.WhenAll(orders.ChangeStatusAsync(a.Token, saved.Value, 20), orders.ChangeStatusAsync(a.Token, saved.Value, 20));
        Assert.AreEqual(1, race.Count(r => r.Succeeded));
        Assert.AreEqual(2, (await orders.ReceiptAsync(a.Token, saved.Value)).Value!.Events.Count);
        Assert.IsTrue((await orders.ChangeStatusAsync(a.Token, saved.Value, 30)).Succeeded);
        Assert.IsTrue((await orders.ChangeStatusAsync(a.Token, saved.Value, 40)).Succeeded);
        Assert.IsFalse((await orders.ChangeStatusAsync(a.Token, saved.Value, 90)).Succeeded);
        var cancel = await orders.PlaceAsync(a.Token, address, items); Assert.IsTrue(cancel.Succeeded);
        Assert.IsTrue((await orders.ChangeStatusAsync(a.Token, cancel.Value, 90)).Succeeded);
        Assert.IsFalse((await orders.ChangeStatusAsync(a.Token, cancel.Value, 20)).Succeeded);
        await accounts.LogoutAsync(a.Token);
        Assert.AreEqual(OrderingError.Expired, (await orders.HistoryAsync(a.Token)).Error);
    }
}
