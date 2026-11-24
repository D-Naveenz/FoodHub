using FoodHub.Core.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodHub.Core.Services;

/// <summary>Coordinates local account workflows. Every private operation validates its session in SQL Server.</summary>
public sealed class AccountService(Func<FoodHub_DevContext> contextFactory, TimeProvider? clock = null, ILogger<AccountService>? logger = null)
{
    private readonly ILogger<AccountService> _logger = logger ?? NullLogger<AccountService>.Instance;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly PasswordHasher<User> _hasher = new();

    /// <summary>Validates input and atomically saves the account, customer, and initial session.</summary>
    public Task<AccountResult<SessionTicket>> RegisterAsync(Registration input, CancellationToken cancellation = default) => Run(async () =>
    {
        if (AccountRules.Validate(input) is { } error) return Failure<SessionTicket>(AccountError.Validation, error);
        await using var db = contextFactory();
        var normalized = AccountRules.NormalizeEmail(input.Email);
        if (await db.Users.AnyAsync(u => u.NormalizedEmail == normalized, cancellation))
            return Failure<SessionTicket>(AccountError.Conflict, "An account already uses this email address.");
        var user = new User { Email = input.Email.Trim(), NormalizedEmail = normalized,
            DisplayName = input.DisplayName.Trim(), CreatedAtUtc = UtcNow };
        // Password derivation is CPU work; keep it off the WinUI thread.
        user.PasswordHash = await Task.Run(() => _hasher.HashPassword(user, input.Password), cancellation);
        user.Customer = new Customer { FullName = input.FullName.Trim(), PhoneNumber = input.Phone.Trim() };
        db.Users.Add(user);
        var token = AddSession(db, user, input.Remember);
        // One SaveChanges transaction prevents partial registrations, including duplicate-email races.
        await db.SaveChangesAsync(cancellation);
        return new AccountResult<SessionTicket>(new(token.Token, token.ExpiresAtUtc, Project(user)));
    });

    /// <summary>Verifies the versioned hash, upgrades it when needed, and issues a fixed-lifetime session.</summary>
    public Task<AccountResult<SessionTicket>> LoginAsync(string email, string password, bool remember,
        CancellationToken cancellation = default) => Run(async () =>
    {
        if (!AccountRules.ValidEmail(email) || password.Length is < 1 or > 128)
            return Failure<SessionTicket>(AccountError.InvalidCredentials, "Email or password is incorrect.");
        await using var db = contextFactory();
        var normalized = AccountRules.NormalizeEmail(email);
        var user = await db.Users.Include(u => u.Customer).ThenInclude(c => c!.CustomerAddresses)
            .SingleOrDefaultAsync(u => u.NormalizedEmail == normalized, cancellation);
        var verification = user is null ? PasswordVerificationResult.Failed
            : await Task.Run(() => _hasher.VerifyHashedPassword(user, user.PasswordHash, password), cancellation);
        if (user?.Customer is null || verification == PasswordVerificationResult.Failed)
            return Failure<SessionTicket>(AccountError.InvalidCredentials, "Email or password is incorrect.");
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
            user.PasswordHash = await Task.Run(() => _hasher.HashPassword(user, password), cancellation);
        var token = AddSession(db, user, remember);
        await db.SaveChangesAsync(cancellation);
        return new AccountResult<SessionTicket>(new(token.Token, token.ExpiresAtUtc, Project(user)));
    });

    /// <summary>Restores or revalidates a session, returning fresh profile data rather than trusting a local cache.</summary>
    public Task<AccountResult<SessionTicket>> ValidateAsync(string token, CancellationToken cancellation = default) => Run(async () =>
    {
        await using var db = contextFactory();
        var session = await FindSession(db, token, cancellation);
        return session?.User.Customer is null ? Failure<SessionTicket>(AccountError.Expired, "Your session has ended. Please sign in again.")
            : new AccountResult<SessionTicket>(new(token, session.ExpiresAtUtc, Project(session.User)));
    });

    /// <summary>Revokes this token when reachable. Callers clear local state before awaiting this operation.</summary>
    public Task<AccountResult<bool>> LogoutAsync(string token, CancellationToken cancellation = default) => Run(async () =>
    {
        await using var db = contextFactory();
        var hash = AccountRules.HashToken(token);
        var session = await db.UserSessions.SingleOrDefaultAsync(s => s.TokenHash == hash, cancellation);
        if (session is not null) session.RevokedAtUtc = UtcNow;
        await db.SaveChangesAsync(cancellation);
        return new AccountResult<bool>(true);
    });

    /// <summary>Updates the account/customer fields belonging to this session.</summary>
    public Task<AccountResult<SessionTicket>> UpdateBasicsAsync(string token, ProfileBasics input, CancellationToken cancellation = default)
        => Update(token, async (db, user) =>
        {
            if (AccountRules.Validate(input) is { } error) return error;
            user.DisplayName = input.DisplayName.Trim();
            user.Customer!.FullName = input.FullName.Trim();
            user.Customer.PhoneNumber = input.Phone.Trim();
            await Task.CompletedTask;
            return null;
        }, cancellation);

    /// <summary>Stores a normalized photo supplied by the Windows image pipeline, or removes it.</summary>
    public Task<AccountResult<SessionTicket>> UpdatePhotoAsync(string token, byte[]? png, CancellationToken cancellation = default)
        => Update(token, (db, user) =>
        {
            if (png is { Length: > 5 * 1024 * 1024 }) return Task.FromResult<string?>("The photo is too large.");
            user.Photo = png;
            return Task.FromResult<string?>(null);
        }, cancellation);

    /// <summary>Creates or updates an address, ignoring any identity that is not owned by this customer.</summary>
    public Task<AccountResult<SessionTicket>> SaveAddressAsync(string token, AddressInput input, CancellationToken cancellation = default)
        => Update(token, async (db, user) =>
        {
            if (AccountRules.Validate(input) is { } error) return error;
            var customer = user.Customer!;
            var address = input.Id is null ? new CustomerAddress() : customer.CustomerAddresses.SingleOrDefault(a => a.CustomerAddressId == input.Id);
            if (address is null) return "This address is not available for your account.";
            if (input.Id is null) customer.CustomerAddresses.Add(address);
            address.AddressLabel = input.Label.Trim(); address.AddressLine1 = input.Line1.Trim();
            address.AddressLine2 = Optional(input.Line2); address.City = input.City.Trim(); address.PostalCode = Optional(input.PostalCode);
            await Task.CompletedTask;
            return null;
        }, cancellation);

    /// <summary>Deletes only an owned address with no historical order references.</summary>
    public Task<AccountResult<SessionTicket>> DeleteAddressAsync(string token, int addressId, CancellationToken cancellation = default)
        => Update(token, async (db, user) =>
        {
            var address = user.Customer!.CustomerAddresses.SingleOrDefault(a => a.CustomerAddressId == addressId);
            if (address is null) return "This address is not available for your account.";
            if (await db.Orders.AnyAsync(o => o.CustomerAddressId == addressId, cancellation))
                return "This address is used by an order and cannot be deleted.";
            db.CustomerAddresses.Remove(address);
            user.Customer.CustomerAddresses.Remove(address);
            return null;
        }, cancellation);

    /// <summary>Validates ownership once, applies a profile change, then returns a fresh safe projection.</summary>
    private Task<AccountResult<SessionTicket>> Update(string token, Func<FoodHub_DevContext, User, Task<string?>> change,
        CancellationToken cancellation) => Run(async () =>
    {
        await using var db = contextFactory();
        // The session and owner are loaded together; a caller never supplies a customer ID.
        var session = await FindSession(db, token, cancellation);
        if (session?.User.Customer is null) return Failure<SessionTicket>(AccountError.Expired, "Your session has ended. Please sign in again.");
        if (await change(db, session.User) is { } error) return Failure<SessionTicket>(AccountError.Validation, error);
        await db.SaveChangesAsync(cancellation);
        return new AccountResult<SessionTicket>(new(token, session.ExpiresAtUtc, Project(session.User)));
    });

    /// <summary>Loads only an active token and its owning account/customer in one context.</summary>
    private async Task<UserSession?> FindSession(FoodHub_DevContext db, string token, CancellationToken cancellation)
    {
        if (token.Length != 64 || !token.All(Uri.IsHexDigit)) return null;
        var hash = AccountRules.HashToken(token);
        var now = UtcNow;
        return await db.UserSessions.Include(s => s.User).ThenInclude(u => u.Customer).ThenInclude(c => c!.CustomerAddresses)
            .SingleOrDefaultAsync(s => s.TokenHash == hash && s.RevokedAtUtc == null && s.ExpiresAtUtc > now, cancellation);
    }

    /// <summary>Creates a random token for the caller and adds only its digest to EF tracking.</summary>
    private (string Token, DateTime ExpiresAtUtc) AddSession(FoodHub_DevContext db, User user, bool remember)
    {
        var token = AccountRules.NewToken(); var now = UtcNow; var expiry = AccountRules.Expiry(now, remember);
        db.UserSessions.Add(new UserSession { User = user, TokenHash = AccountRules.HashToken(token), CreatedAtUtc = now, ExpiresAtUtc = expiry });
        return (token, expiry);
    }

    private DateTime UtcNow => _clock.GetUtcNow().UtcDateTime;
    private static string? Optional(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    private static ProfileDetails Project(User user) => new(user.UserId, user.Customer!.CustomerId, user.Email, user.DisplayName,
        user.Customer.FullName, user.Customer.PhoneNumber, user.Photo,
        user.Customer.CustomerAddresses.OrderBy(a => a.CustomerAddressId).Select(a => new AddressDetails(a.CustomerAddressId,
            a.AddressLabel, a.AddressLine1, a.AddressLine2, a.City, a.PostalCode)).ToList());
    private static AccountResult<T> Failure<T>(AccountError error, string message) => new(default, error, message);

    /// <summary>Maps expected data failures to safe UI results while allowing cancellation to propagate.</summary>
    private async Task<AccountResult<T>> Run<T>(Func<Task<AccountResult<T>>> action)
    {
        try { return await action(); }
        catch (DatabaseConfigurationException ex)
        { return Failure<T>(AccountError.Configuration, ex.Message); }
        catch (SqlException ex) when (ex.Number is 208 or 207)
        {
            _logger.LogWarning("Account schema unavailable ({Code})", ex.Number);
            return Failure<T>(AccountError.Configuration, "The account database schema is unavailable. Apply the required migrations before signing in.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        { return Failure<T>(AccountError.Conflict, "This record already exists. Check the email address and try again."); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 547 })
        { return Failure<T>(AccountError.Conflict, "This record is in use and cannot be removed."); }
        catch (Exception ex) when (ex is SqlException or DbUpdateException or InvalidOperationException or TimeoutException)
        {
            // Log only an error category/code, never connection strings, parameters, tokens, or passwords.
            _logger.LogError("Account data operation failed: {Category} ({Code})", ex.GetType().Name,
                ex is SqlException sql ? sql.Number : 0);
            return Failure<T>(AccountError.Database, "Cannot connect to the account database. Check database Settings and SQL Server availability.");
        }
    }
}