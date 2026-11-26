using FoodHub.Core.Migrations;
using FoodHub.Core.Models;
using FoodHub.Core.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FoodHub.WinUI.Test;

/// <summary>Exercises SQL Server rules in disposable databases, with explicit opt-in through test configuration.</summary>
[TestClass]
[TestCategory("SqlIntegration")]
public sealed class SqlIntegrationTests
{
    private string _connection = "";

    /// <summary>Missing test configuration skips only SQL tests; a configured but unreachable server is a failure.</summary>
    [TestInitialize]
    public void RequireTestServer()
    {
        var connection = Environment.GetEnvironmentVariable("FOODHUB_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connection))
            Assert.Inconclusive("Set FOODHUB_TEST_CONNECTION_STRING to a test SQL Server identity with CREATE DATABASE permission.");
        _connection = connection;
    }

    /// <summary>Connection checks distinguish missing migrations from a ready schema without creating objects.</summary>
    [TestMethod]
    public async Task ConnectionCheckIsReadOnlyAndReportsMigrations()
    {
        await using var database = await SqlTestDatabase.CreateAsync(_connection);
        var tester = new DatabaseConnectionTester(Microsoft.Extensions.Logging.Abstractions.NullLogger<DatabaseConnectionTester>.Instance);
        var missing = await tester.TestAsync(database.ConnectionString);
        Assert.AreEqual(ConnectionCheckStatus.Warning, missing.Status);
        StringAssert.Contains(missing.Message, "migrations are missing");
        await using var db = database.Open();
        Assert.IsTrue(await db.Database.CanConnectAsync());
        Assert.AreEqual(0, (await db.Database.GetAppliedMigrationsAsync()).Count());
        var accounts = new AccountService(database.Open);
        Assert.AreEqual(AccountError.Configuration, (await accounts.LoginAsync("fictional@example.test", "password", false)).Error);
        await db.Database.MigrateAsync();
        var ready = await tester.TestAsync(database.ConnectionString);
        Assert.AreEqual(ConnectionCheckStatus.Success, ready.Status);
        StringAssert.Contains(ready.Message, "migrations are present");
        Assert.AreEqual(0, await db.Users.CountAsync());
    }

    /// <summary>A new install supports registration, sessions, ownership, receipts and status rules together.</summary>
    [TestMethod]
    public async Task FreshDatabaseSupportsAccountsAndOrderRules()
    {
        await using var database = await SqlTestDatabase.CreateAsync(_connection);
        await using var fresh = database.Open();
        await fresh.Database.MigrateAsync();
        Assert.AreEqual(0, await fresh.Users.CountAsync());
        Assert.AreEqual(5, await fresh.OrderStatuses.CountAsync());
        var clock = new TestClock(new DateTimeOffset(2026, 11, 24, 0, 0, 0, TimeSpan.Zero));
        var service = new AccountService(database.Open, clock);
        var input = new Registration("Ada", "Ada Example", "ada@example.test", "0771234567", "correct horse foodhub", "correct horse foodhub", true);
        var registered = await service.RegisterAsync(input); Assert.IsTrue(registered.Succeeded, "registration creates account, customer and session");
        var ticket = registered.Value!;
        Assert.IsTrue((await service.ValidateAsync(ticket.Token)).Succeeded, "remembered token restores through fresh database validation");
        Assert.IsTrue(await fresh.Users.CountAsync() == 1 && await fresh.Customers.CountAsync() == 1 && await fresh.UserSessions.CountAsync() == 1,
            "registration persists all records together");
        Assert.IsTrue((await fresh.UserSessions.SingleAsync()).TokenHash == AccountRules.HashToken(ticket.Token), "database stores token digest only");
        Assert.IsTrue(!(await service.LoginAsync(input.Email, input.Password.Trim() + "!", false)).Succeeded
            && (await service.LoginAsync(" ADA@EXAMPLE.TEST ", input.Password, false)).Succeeded, "login rejects wrong password and normalizes email");
        var persistedUser = await fresh.Users.SingleAsync();
        var previousHash = new PasswordHasher<User>(Options.Create(new PasswordHasherOptions { IterationCount = 10000 })).HashPassword(persistedUser, input.Password);
        persistedUser.PasswordHash = previousHash; await fresh.SaveChangesAsync();
        await service.LoginAsync(input.Email, input.Password, false);
        await fresh.Entry(persistedUser).ReloadAsync();
        Assert.IsTrue(persistedUser.PasswordHash != previousHash, "successful login persists upgraded password hash");
        var memorySession = (await service.LoginAsync(input.Email, input.Password, false)).Value!;
        Assert.IsTrue(memorySession.ExpiresAtUtc == clock.GetUtcNow().UtcDateTime.AddHours(8), "nonremembered database session has eight-hour lifetime");
        var concurrent = await Task.WhenAll(service.RegisterAsync(input with { Email = "race@example.test" }), service.RegisterAsync(input with { Email = "RACE@example.test" }));
        Assert.IsTrue(concurrent.Count(r => r.Succeeded) == 1 && await fresh.Users.CountAsync() == 2 && await fresh.Customers.CountAsync() == 2, "duplicate-email race leaves no partial customer");
        Assert.IsTrue(!(await service.RegisterAsync(input with { Password = "short", Confirmation = "short" })).Succeeded && await fresh.Users.CountAsync() == 2, "invalid registration writes nothing");
        var addressResult = await service.SaveAddressAsync(ticket.Token, new(null, "Home", "1 Example Road", null, "Colombo", null));
        Assert.IsTrue(addressResult.Succeeded, "owned address creation");
        var addressId = addressResult.Value!.Profile.Addresses.Single().Id;
        var other = (await service.LoginAsync("race@example.test", input.Password, false)).Value!;
        Assert.IsTrue(!(await service.SaveAddressAsync(other.Token, new(addressId, "Other", "2 Road", null, "City", null))).Succeeded
            && !(await service.DeleteAddressAsync(other.Token, addressId)).Succeeded, "cross-customer address edits/deletes rejected");
        // Exercise the retained TVP and procedures, then verify profile/address changes cannot rewrite receipt snapshots.
        await fresh.Database.ExecuteSqlRawAsync("INSERT dbo.MenuCategories(CategoryName) VALUES(N'Test'); INSERT dbo.MenuItems(MenuCategoryId,ItemName,UnitPrice) SELECT MenuCategoryId,N'Test meal',100 FROM dbo.MenuCategories;");
        var itemId = await fresh.MenuItems.Select(m => m.MenuItemId).SingleAsync();
        var orderId = new OutputParameter<int?>();
        await fresh.Procedures.PlaceOrderAsync(ticket.Profile.CustomerId, addressId, new BasketItemList[] { new() { MenuItemId = itemId, Quantity = 2 } }, orderId);
        var order = await fresh.Orders.AsNoTracking().SingleAsync(o => o.OrderId == orderId.Value);
        Assert.IsTrue(order.TotalAmount == 450 && order.CustomerName == "Ada Example", "PlaceOrder retains SQL pricing and receipt snapshots");
        Assert.IsTrue(!(await service.DeleteAddressAsync(ticket.Token, addressId)).Succeeded, "used address cannot be deleted");
        await service.UpdateBasicsAsync(ticket.Token, new("Ada renamed", "Changed name", "0111234567"));
        await service.SaveAddressAsync(ticket.Token, new(addressId, "Home", "Changed road", null, "City", null));
        var savedOrder = await fresh.Orders.AsNoTracking().SingleAsync(o => o.OrderId == orderId.Value);
        Assert.IsTrue(savedOrder.CustomerName == "Ada Example" && savedOrder.DeliveryAddressLine1 == "1 Example Road", "profile/address edits preserve saved receipt");
        await fresh.Procedures.UpdateOrderStatusAsync(ticket.Profile.CustomerId, orderId.Value, 20);
        bool rejected = false;
        try { await fresh.Procedures.UpdateOrderStatusAsync(ticket.Profile.CustomerId, orderId.Value, 40); }
        catch (SqlException ex) when (ex.Number == 51006) { rejected = true; }
        Assert.IsTrue(rejected && await fresh.OrderStatusHistories.CountAsync(h => h.OrderId == orderId.Value) == 2, "order procedures reject skipped transitions without duplicate history");
        await service.LogoutAsync(other.Token);
        Assert.IsTrue((await service.ValidateAsync(other.Token)).Error == AccountError.Expired, "revoked session cannot restore");
        clock.Advance(TimeSpan.FromHours(8));
        Assert.IsTrue((await service.ValidateAsync(memorySession.Token)).Error == AccountError.Expired
            && (await service.ValidateAsync(ticket.Token)).Succeeded, "eight-hour expiry does not prematurely end seven-day sessions");
        clock.Advance(TimeSpan.FromDays(7) - TimeSpan.FromHours(8));
        Assert.IsTrue((await service.ValidateAsync(ticket.Token)).Error == AccountError.Expired, "remembered session expires exactly at seven days");
    }

    /// <summary>A deliberate recreation drops old rows while repeat migration preserves current rows.</summary>
    [TestMethod]
    public async Task RecreationProducesOneCurrentSchema()
    {
        await using var target = await SqlTestDatabase.CreateAsync(_connection);
        await using (var db = target.Open())
        {
            await db.Database.MigrateAsync();
            await db.Database.ExecuteSqlRawAsync("INSERT dbo.Customers(FullName,PhoneNumber) VALUES(N'Disposable',N'0770000000');");
            await db.Database.MigrateAsync();
            Assert.AreEqual(1, await db.Customers.CountAsync());
            Assert.AreEqual(1, (await db.Database.GetAppliedMigrationsAsync()).Count());
        }
        // Only tests can invoke the recreation primitive with a generated database; the CLI always validates its target first.
        await DevelopmentDatabase.RecreateAsync(target.ConnectionString);
        await using var rebuilt = target.Open();
        Assert.AreEqual(0, await rebuilt.Customers.CountAsync());
        Assert.AreEqual(0, await rebuilt.Users.CountAsync());
        Assert.AreEqual(5, await rebuilt.OrderStatuses.CountAsync());
        Assert.AreEqual(1, await rebuilt.StoreSettings.CountAsync());
        Assert.AreEqual(1, (await rebuilt.Database.GetAppliedMigrationsAsync()).Count());
    }

}

/// <summary>Deterministic UTC clock for expiry tests without sleeps.</summary>
sealed class TestClock(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;
    /// <summary>Current controlled UTC instant.</summary>
    public override DateTimeOffset GetUtcNow() => _now;
    /// <summary>Moves test time forward without renewing sessions.</summary>
    public void Advance(TimeSpan amount) => _now += amount;
}