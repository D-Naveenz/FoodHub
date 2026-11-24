using FoodHub.Core.Models;
using FoodHub.Core.Services;
using FoodHub.Core.Migrations;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;

// Dependency-free executable test suite. Any failed assertion exits nonzero, including integration failures.
var passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + name);
    Console.WriteLine("PASS: " + name); passed++;
}
var registration = new Registration("Ada", "Ada Example", " Ada@Example.test ", "0771234567", " twelve chars! ", " twelve chars! ", false);
Check(AccountRules.Validate(registration) is null, "valid registration preserves surrounding password spaces");
Check(AccountRules.NormalizeEmail(registration.Email) == "ADA@EXAMPLE.TEST", "email normalization trims and folds case");
Check(AccountRules.Validate(registration with { Confirmation = registration.Password.Trim() }) is not null, "passwords are compared without trimming");
Check(AccountRules.Validate(registration with { Password = "short", Confirmation = "short" }) is not null, "short passwords rejected");
Check(AccountRules.Validate(registration with { Password = new string('x',129), Confirmation = new string('x',129) }) is not null, "oversized passwords rejected");
Check(AccountRules.Validate(registration with { DisplayName = new string('x',81) }) is not null, "display-name limit");
Check(!AccountRules.ValidEmail("name <a@example.test>") && !AccountRules.ValidEmail("a@") && AccountRules.ValidEmail("a@example.test"), "email syntax without mailbox verification");
Check(AccountRules.Validate(new AddressInput(null,"Home","1 Example Road",null,"Colombo",null)) is null
    && AccountRules.Validate(new AddressInput(null,"Home","",null,"Colombo",null)) is not null, "required address fields");
var now = new DateTime(2026,11,24,0,0,0,DateTimeKind.Utc);
Check(AccountRules.Expiry(now,false) == now.AddHours(8) && AccountRules.Expiry(now,true) == now.AddDays(7), "fixed memory and remembered lifetimes");
Check(!AccountRules.IsActive(now,now,null) && !AccountRules.IsActive(now,now.AddDays(1),now), "expiry boundary and revocation");
var token = AccountRules.NewToken();
Check(token.Length == 64 && token != AccountRules.NewToken() && AccountRules.HashToken(token) != token, "random 256-bit tokens and one-way lookup hash");
var user = new User(); var hasher = new PasswordHasher<User>(); var hash = hasher.HashPassword(user,registration.Password);
Check(hash != registration.Password && hasher.VerifyHashedPassword(user,hash,registration.Password) != PasswordVerificationResult.Failed
    && hasher.VerifyHashedPassword(user,hash,registration.Password.Trim()) == PasswordVerificationResult.Failed, "salted password verification preserves spaces");
var oldHasher = new PasswordHasher<User>(Options.Create(new PasswordHasherOptions { IterationCount = 10000 }));
Check(hasher.VerifyHashedPassword(user,oldHasher.HashPassword(user,registration.Password),registration.Password) == PasswordVerificationResult.SuccessRehashNeeded, "older password hashes request upgrade");
var options = new DbContextOptionsBuilder<FoodHub_DevContext>().UseSqlServer("Server=unused;Database=FoodHub_Test;Integrated Security=True").Options;
using (var db = new FoodHub_DevContext(options))
{
    Check(!db.Database.HasPendingModelChanges(), "migration snapshot matches the current EF model");
    var script = db.GetService<IMigrator>().GenerateScript();
    Check(script.Contains("CREATE PROCEDURE dbo.PlaceOrder") && script.Contains("CREATE TYPE dbo.BasketItemList")
        && script.Contains("CK_Orders_Amounts") && script.Contains("IX_Users_NormalizedEmail"), "fresh migration script retains SQL workflow objects and account uniqueness");
}
var testConnection = Environment.GetEnvironmentVariable("FOODHUB_TEST_CONNECTION_STRING");
if (testConnection is null)
    Console.WriteLine("SKIP: SQL Server integration (set FOODHUB_TEST_CONNECTION_STRING; requires CREATE DATABASE on a test server).");
else await IntegrationAsync(testConnection, Check);
Console.WriteLine($"{passed} assertions passed.");

/// <summary>Runs fresh/upgrade checks in two uniquely named disposable databases, never the configured application database.</summary>
static async Task IntegrationAsync(string serverConnection, Action<bool,string> check)
{
    var builder = new SqlConnectionStringBuilder(serverConnection) { InitialCatalog = "master", Pooling = false };
    await using var admin = new SqlConnection(builder.ConnectionString); await admin.OpenAsync();
    var suffix = Guid.NewGuid().ToString("N");
    var freshName = "FoodHub_Test_Fresh_" + suffix; var upgradeName = "FoodHub_Test_Upgrade_" + suffix;
    var referenceName = "FoodHub_Test_Reference_" + suffix;
    var created = new List<string>();
    async Task Execute(string sql) { await using var cmd = new SqlCommand(sql,admin); await cmd.ExecuteNonQueryAsync(); }
    string Connection(string name) { var b = new SqlConnectionStringBuilder(builder.ConnectionString) { InitialCatalog = name }; return b.ConnectionString; }
    FoodHub_DevContext Open(string name) => new(new DbContextOptionsBuilder<FoodHub_DevContext>().UseSqlServer(Connection(name)).Options);
    try
    {
        foreach (var name in new[] { freshName, upgradeName, referenceName }) { await Execute($"CREATE DATABASE [{name}]"); created.Add(name); }
        await using var fresh = Open(freshName); await fresh.Database.MigrateAsync();
        check(await fresh.Users.CountAsync() == 0 && await fresh.OrderStatuses.CountAsync() == 5, "fresh migration creates empty accounts and required order statuses");
        await using var legacy = Open(upgradeName);
        foreach (var batch in BaselineSql.Batches()) await legacy.Database.ExecuteSqlRawAsync(batch);
        await legacy.Database.ExecuteSqlRawAsync("INSERT dbo.Customers(FullName,PhoneNumber) VALUES(N'Legacy customer',N'0770000000');");
        // Compare against a baseline-only schema before writing migration history.
        var baselineId = fresh.Database.GetMigrations().Single(m => m.EndsWith("_Baseline",StringComparison.Ordinal));
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
        await using (var reference = Open(referenceName))
            foreach (var batch in BaselineSql.Batches()) await reference.Database.ExecuteSqlRawAsync(batch);
        await BaselineVerifier.VerifyAsync(Connection(upgradeName),Connection(referenceName));
        check(true,"legacy schema matches the full clean baseline before adoption");
        var history = legacy.GetService<IHistoryRepository>();
        await legacy.Database.ExecuteSqlRawAsync(history.GetCreateIfNotExistsScript());
        await legacy.Database.ExecuteSqlRawAsync(history.GetInsertScript(new HistoryRow(baselineId,"10.0.15")));
        await legacy.Database.MigrateAsync();
        check((await legacy.Customers.SingleAsync()).CustomerId == originalId && (await legacy.Customers.SingleAsync()).UserId is null, "upgrade preserves legacy customer identity without granting credentials");
        var upgradedReceipt = await legacy.Orders.AsNoTracking().SingleAsync();
        check(upgradedReceipt.OrderId == legacyReceipt.OrderId && upgradedReceipt.TotalAmount == legacyReceipt.TotalAmount
            && upgradedReceipt.DeliveryAddressLine1 == legacyReceipt.DeliveryAddressLine1 && await legacy.OrderStatusHistories.CountAsync() == 1,
            "upgrade preserves order identity, receipt snapshots and tracking history");
        var clock = new TestClock(new DateTimeOffset(2026,11,24,0,0,0,TimeSpan.Zero));
        var service = new AccountService(() => Open(freshName),clock);
        var input = new Registration("Ada","Ada Example","ada@example.test","0771234567","correct horse foodhub","correct horse foodhub",true);
        var registered = await service.RegisterAsync(input); check(registered.Succeeded,"registration creates account, customer and session");
        var ticket = registered.Value!;
        check((await service.ValidateAsync(ticket.Token)).Succeeded,"remembered token restores through fresh database validation");
        check(await fresh.Users.CountAsync() == 1 && await fresh.Customers.CountAsync() == 1 && await fresh.UserSessions.CountAsync() == 1,
            "registration persists all records together");
        check((await fresh.UserSessions.SingleAsync()).TokenHash == AccountRules.HashToken(ticket.Token),"database stores token digest only");
        check(!(await service.LoginAsync(input.Email,input.Password.Trim()+"!",false)).Succeeded
            && (await service.LoginAsync(" ADA@EXAMPLE.TEST ",input.Password,false)).Succeeded,"login rejects wrong password and normalizes email");
        var persistedUser = await fresh.Users.SingleAsync();
        var previousHash = new PasswordHasher<User>(Options.Create(new PasswordHasherOptions { IterationCount=10000 })).HashPassword(persistedUser,input.Password);
        persistedUser.PasswordHash = previousHash; await fresh.SaveChangesAsync();
        await service.LoginAsync(input.Email,input.Password,false);
        await fresh.Entry(persistedUser).ReloadAsync();
        check(persistedUser.PasswordHash != previousHash,"successful login persists upgraded password hash");
        var memorySession = (await service.LoginAsync(input.Email,input.Password,false)).Value!;
        check(memorySession.ExpiresAtUtc == clock.GetUtcNow().UtcDateTime.AddHours(8),"nonremembered database session has eight-hour lifetime");
        var concurrent = await Task.WhenAll(service.RegisterAsync(input with { Email="race@example.test" }), service.RegisterAsync(input with { Email="RACE@example.test" }));
        check(concurrent.Count(r => r.Succeeded) == 1 && await fresh.Users.CountAsync() == 2 && await fresh.Customers.CountAsync() == 2,"duplicate-email race leaves no partial customer");
        check(!(await service.RegisterAsync(input with { Password="short",Confirmation="short" })).Succeeded && await fresh.Users.CountAsync() == 2,"invalid registration writes nothing");
        var addressResult = await service.SaveAddressAsync(ticket.Token,new(null,"Home","1 Example Road",null,"Colombo",null));
        check(addressResult.Succeeded,"owned address creation");
        var addressId = addressResult.Value!.Profile.Addresses.Single().Id;
        var other = (await service.LoginAsync("race@example.test",input.Password,false)).Value!;
        check(!(await service.SaveAddressAsync(other.Token,new(addressId,"Other","2 Road",null,"City",null))).Succeeded
            && !(await service.DeleteAddressAsync(other.Token,addressId)).Succeeded,"cross-customer address edits/deletes rejected");
        // Exercise the retained TVP and procedures, then verify profile/address changes cannot rewrite receipt snapshots.
        await fresh.Database.ExecuteSqlRawAsync("INSERT dbo.MenuCategories(CategoryName) VALUES(N'Test'); INSERT dbo.MenuItems(MenuCategoryId,ItemName,UnitPrice) SELECT MenuCategoryId,N'Test meal',100 FROM dbo.MenuCategories;");
        var itemId = await fresh.MenuItems.Select(m => m.MenuItemId).SingleAsync();
        var orderId = new OutputParameter<int?>();
        await fresh.Procedures.PlaceOrderAsync(ticket.Profile.CustomerId,addressId,[new BasketItemList { MenuItemId=itemId,Quantity=2 }],orderId);
        var order = await fresh.Orders.AsNoTracking().SingleAsync(o => o.OrderId == orderId.Value);
        check(order.TotalAmount == 450 && order.CustomerName == "Ada Example","PlaceOrder retains SQL pricing and receipt snapshots");
        check(!(await service.DeleteAddressAsync(ticket.Token,addressId)).Succeeded,"used address cannot be deleted");
        await service.UpdateBasicsAsync(ticket.Token,new("Ada renamed","Changed name","0111234567"));
        await service.SaveAddressAsync(ticket.Token,new(addressId,"Home","Changed road",null,"City",null));
        var savedOrder = await fresh.Orders.AsNoTracking().SingleAsync(o => o.OrderId == orderId.Value);
        check(savedOrder.CustomerName == "Ada Example" && savedOrder.DeliveryAddressLine1 == "1 Example Road","profile/address edits preserve saved receipt");
        await fresh.Procedures.UpdateOrderStatusAsync(ticket.Profile.CustomerId,orderId.Value,20);
        bool rejected = false;
        try { await fresh.Procedures.UpdateOrderStatusAsync(ticket.Profile.CustomerId,orderId.Value,40); }
        catch (SqlException ex) when (ex.Number == 51006) { rejected=true; }
        check(rejected && await fresh.OrderStatusHistories.CountAsync(h => h.OrderId == orderId.Value) == 2,"order procedures reject skipped transitions without duplicate history");
        await service.LogoutAsync(other.Token);
        check((await service.ValidateAsync(other.Token)).Error == AccountError.Expired,"revoked session cannot restore");
        clock.Advance(TimeSpan.FromHours(8));
        check((await service.ValidateAsync(memorySession.Token)).Error == AccountError.Expired
            && (await service.ValidateAsync(ticket.Token)).Succeeded,"eight-hour expiry does not prematurely end seven-day sessions");
        clock.Advance(TimeSpan.FromDays(7)-TimeSpan.FromHours(8));
        check((await service.ValidateAsync(ticket.Token)).Error == AccountError.Expired,"remembered session expires exactly at seven days");
        // Verify the comparison detects intentional drift on otherwise identical account schemas.
        await BaselineVerifier.VerifyAsync(Connection(freshName),Connection(freshName));
        await fresh.Database.ExecuteSqlRawAsync("CREATE TABLE dbo.UnexpectedDrift(Id int NOT NULL);");
        bool drift=false;
        try { await BaselineVerifier.VerifyAsync(Connection(freshName),Connection(upgradeName)); } catch (InvalidOperationException) { drift=true; }
        check(drift,"baseline comparison rejects schema drift");
    }
    finally
    {
        // Only these exact generated names can be removed, on the explicitly selected test server.
        SqlConnection.ClearAllPools();
        foreach (var name in created)
        {
            if (!name.StartsWith("FoodHub_Test_",StringComparison.Ordinal) || !name.EndsWith(suffix,StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe cleanup target.");
            await Execute($"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}];");
        }
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
