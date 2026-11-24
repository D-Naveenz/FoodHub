using FoodHub.Core.Models;
using FoodHub.Core.Services;
using FoodHub.Core.Migrations;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

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

    /// <summary>A new install supports registration, sessions, ownership, receipts and status rules together.</summary>
    [TestMethod]
    public async Task FreshDatabaseSupportsAccountsAndOrderRules()
    {
        await using var database = await SqlTestDatabase.CreateAsync(_connection);
        await using var fresh = database.Open();
        await fresh.Database.MigrateAsync();
        Assert.AreEqual(0, await fresh.Users.CountAsync());
        Assert.AreEqual(5, await fresh.OrderStatuses.CountAsync());
        var clock = new TestClock(new DateTimeOffset(2026,11,24,0,0,0,TimeSpan.Zero));
        var service = new AccountService(database.Open,clock);
        var input = new Registration("Ada","Ada Example","ada@example.test","0771234567","correct horse foodhub","correct horse foodhub",true);
        var registered = await service.RegisterAsync(input); Assert.IsTrue(registered.Succeeded,"registration creates account, customer and session");
        var ticket = registered.Value!;
        Assert.IsTrue((await service.ValidateAsync(ticket.Token)).Succeeded,"remembered token restores through fresh database validation");
        Assert.IsTrue(await fresh.Users.CountAsync() == 1 && await fresh.Customers.CountAsync() == 1 && await fresh.UserSessions.CountAsync() == 1,
            "registration persists all records together");
        Assert.IsTrue((await fresh.UserSessions.SingleAsync()).TokenHash == AccountRules.HashToken(ticket.Token),"database stores token digest only");
        Assert.IsTrue(!(await service.LoginAsync(input.Email,input.Password.Trim()+"!",false)).Succeeded
            && (await service.LoginAsync(" ADA@EXAMPLE.TEST ",input.Password,false)).Succeeded,"login rejects wrong password and normalizes email");
        var persistedUser = await fresh.Users.SingleAsync();
        var previousHash = new PasswordHasher<User>(Options.Create(new PasswordHasherOptions { IterationCount=10000 })).HashPassword(persistedUser,input.Password);
        persistedUser.PasswordHash = previousHash; await fresh.SaveChangesAsync();
        await service.LoginAsync(input.Email,input.Password,false);
        await fresh.Entry(persistedUser).ReloadAsync();
        Assert.IsTrue(persistedUser.PasswordHash != previousHash,"successful login persists upgraded password hash");
        var memorySession = (await service.LoginAsync(input.Email,input.Password,false)).Value!;
        Assert.IsTrue(memorySession.ExpiresAtUtc == clock.GetUtcNow().UtcDateTime.AddHours(8),"nonremembered database session has eight-hour lifetime");
        var concurrent = await Task.WhenAll(service.RegisterAsync(input with { Email="race@example.test" }), service.RegisterAsync(input with { Email="RACE@example.test" }));
        Assert.IsTrue(concurrent.Count(r => r.Succeeded) == 1 && await fresh.Users.CountAsync() == 2 && await fresh.Customers.CountAsync() == 2,"duplicate-email race leaves no partial customer");
        Assert.IsTrue(!(await service.RegisterAsync(input with { Password="short",Confirmation="short" })).Succeeded && await fresh.Users.CountAsync() == 2,"invalid registration writes nothing");
        var addressResult = await service.SaveAddressAsync(ticket.Token,new(null,"Home","1 Example Road",null,"Colombo",null));
        Assert.IsTrue(addressResult.Succeeded,"owned address creation");
        var addressId = addressResult.Value!.Profile.Addresses.Single().Id;
        var other = (await service.LoginAsync("race@example.test",input.Password,false)).Value!;
        Assert.IsTrue(!(await service.SaveAddressAsync(other.Token,new(addressId,"Other","2 Road",null,"City",null))).Succeeded
            && !(await service.DeleteAddressAsync(other.Token,addressId)).Succeeded,"cross-customer address edits/deletes rejected");
        // Exercise the retained TVP and procedures, then verify profile/address changes cannot rewrite receipt snapshots.
        await fresh.Database.ExecuteSqlRawAsync("INSERT dbo.MenuCategories(CategoryName) VALUES(N'Test'); INSERT dbo.MenuItems(MenuCategoryId,ItemName,UnitPrice) SELECT MenuCategoryId,N'Test meal',100 FROM dbo.MenuCategories;");
        var itemId = await fresh.MenuItems.Select(m => m.MenuItemId).SingleAsync();
        var orderId = new OutputParameter<int?>();
        await fresh.Procedures.PlaceOrderAsync(ticket.Profile.CustomerId,addressId,new BasketItemList[] { new() { MenuItemId=itemId,Quantity=2 } },orderId);
        var order = await fresh.Orders.AsNoTracking().SingleAsync(o => o.OrderId == orderId.Value);
        Assert.IsTrue(order.TotalAmount == 450 && order.CustomerName == "Ada Example","PlaceOrder retains SQL pricing and receipt snapshots");
        Assert.IsTrue(!(await service.DeleteAddressAsync(ticket.Token,addressId)).Succeeded,"used address cannot be deleted");
        await service.UpdateBasicsAsync(ticket.Token,new("Ada renamed","Changed name","0111234567"));
        await service.SaveAddressAsync(ticket.Token,new(addressId,"Home","Changed road",null,"City",null));
        var savedOrder = await fresh.Orders.AsNoTracking().SingleAsync(o => o.OrderId == orderId.Value);
        Assert.IsTrue(savedOrder.CustomerName == "Ada Example" && savedOrder.DeliveryAddressLine1 == "1 Example Road","profile/address edits preserve saved receipt");
        await fresh.Procedures.UpdateOrderStatusAsync(ticket.Profile.CustomerId,orderId.Value,20);
        bool rejected = false;
        try { await fresh.Procedures.UpdateOrderStatusAsync(ticket.Profile.CustomerId,orderId.Value,40); }
        catch (SqlException ex) when (ex.Number == 51006) { rejected=true; }
        Assert.IsTrue(rejected && await fresh.OrderStatusHistories.CountAsync(h => h.OrderId == orderId.Value) == 2,"order procedures reject skipped transitions without duplicate history");
        await service.LogoutAsync(other.Token);
        Assert.IsTrue((await service.ValidateAsync(other.Token)).Error == AccountError.Expired,"revoked session cannot restore");
        clock.Advance(TimeSpan.FromHours(8));
        Assert.IsTrue((await service.ValidateAsync(memorySession.Token)).Error == AccountError.Expired
            && (await service.ValidateAsync(ticket.Token)).Succeeded,"eight-hour expiry does not prematurely end seven-day sessions");
        clock.Advance(TimeSpan.FromDays(7)-TimeSpan.FromHours(8));
        Assert.IsTrue((await service.ValidateAsync(ticket.Token)).Error == AccountError.Expired,"remembered session expires exactly at seven days");
    }

    /// <summary>Adopting the baseline preserves customer/order identities and never grants legacy credentials.</summary>
    [TestMethod]
    public async Task LegacyUpgradePreservesCustomersAndReceipts()
    {
        await using var target = await SqlTestDatabase.CreateAsync(_connection);
        await using var baseline = await SqlTestDatabase.CreateAsync(_connection);
        await using var legacy = target.Open();
        foreach (var batch in BaselineSql.Batches()) await legacy.Database.ExecuteSqlRawAsync(batch);
        await legacy.Database.ExecuteSqlRawAsync("INSERT dbo.Customers(FullName,PhoneNumber) VALUES(N'Legacy customer',N'0770000000');");
        // Compare against a baseline-only schema before writing migration history.
        var baselineId = legacy.Database.GetMigrations().Single(m => m.EndsWith("_Baseline",StringComparison.Ordinal));
        var originalId = await legacy.Customers.Select(c => c.CustomerId).SingleAsync();
        await legacy.Database.ExecuteSqlRawAsync("""
            INSERT dbo.CustomerAddresses(CustomerId,AddressLabel,AddressLine1,City) VALUES(1,N'Legacy',N'Legacy road',N'Colombo');
            INSERT dbo.MenuCategories(CategoryName) VALUES(N'Legacy menu');
            INSERT dbo.MenuItems(MenuCategoryId,ItemName,UnitPrice) VALUES(1,N'Legacy meal',125);
            DECLARE @items dbo.BasketItemList, @order int;
            INSERT @items VALUES(1,2);
            EXEC dbo.PlaceOrder 1,1,@items,@order OUTPUT;
            """);
        var legacyReceipt = await legacy.Orders.AsNoTracking().SingleAsync();
        await using (var reference = baseline.Open())
            foreach (var batch in BaselineSql.Batches()) await reference.Database.ExecuteSqlRawAsync(batch);
        await BaselineVerifier.VerifyAsync(target.ConnectionString,baseline.ConnectionString);
        var history = legacy.GetService<IHistoryRepository>();
        await legacy.Database.ExecuteSqlRawAsync(history.GetCreateIfNotExistsScript());
        await legacy.Database.ExecuteSqlRawAsync(history.GetInsertScript(new HistoryRow(baselineId,"10.0.15")));
        await legacy.Database.MigrateAsync();
        Assert.IsTrue((await legacy.Customers.SingleAsync()).CustomerId == originalId && (await legacy.Customers.SingleAsync()).UserId is null, "upgrade preserves legacy customer identity without granting credentials");
        var upgradedReceipt = await legacy.Orders.AsNoTracking().SingleAsync();
        Assert.IsTrue(upgradedReceipt.OrderId == legacyReceipt.OrderId && upgradedReceipt.TotalAmount == legacyReceipt.TotalAmount
            && upgradedReceipt.DeliveryAddressLine1 == legacyReceipt.DeliveryAddressLine1 && await legacy.OrderStatusHistories.CountAsync() == 1,
            "upgrade preserves order identity, receipt snapshots and tracking history");
    }

    /// <summary>The baseline comparison accepts matching metadata and rejects an unexpected table.</summary>
    [TestMethod]
    public async Task BaselineComparisonRejectsDrift()
    {
        await using var target = await SqlTestDatabase.CreateAsync(_connection);
        await using var reference = await SqlTestDatabase.CreateAsync(_connection);
        await using var targetDb = target.Open();
        await using var referenceDb = reference.Open();
        await targetDb.Database.MigrateAsync();
        await referenceDb.Database.MigrateAsync();
        await BaselineVerifier.VerifyAsync(target.ConnectionString, reference.ConnectionString);
        await targetDb.Database.ExecuteSqlRawAsync("CREATE TABLE dbo.UnexpectedDrift(Id int NOT NULL);");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            BaselineVerifier.VerifyAsync(target.ConnectionString, reference.ConnectionString));
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
