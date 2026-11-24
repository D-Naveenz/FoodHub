using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;

namespace FoodHub.Core.Services;

/// <summary>Expected outcomes that screens can explain without exposing database internals.</summary>
public enum AccountError { None, Validation, InvalidCredentials, Expired, Database, Conflict, Configuration }

/// <summary>A workflow result with a safe user-facing message.</summary>
public sealed record AccountResult<T>(T? Value, AccountError Error = AccountError.None, string Message = "")
{
    /// <summary>Whether this operation completed successfully.</summary>
    public bool Succeeded => Error == AccountError.None;
}

/// <summary>Required registration fields. Passwords are transient and must not be logged.</summary>
public sealed record Registration(string DisplayName, string FullName, string Email, string Phone,
    string Password, string Confirmation, bool Remember);
/// <summary>Editable names/contact details; email is deliberately read-only.</summary>
public sealed record ProfileBasics(string DisplayName, string FullName, string Phone);
/// <summary>Delivery-address input with an optional existing identity.</summary>
public sealed record AddressInput(int? Id, string Label, string Line1, string? Line2, string City, string? PostalCode);
/// <summary>An owned saved address suitable for display.</summary>
public sealed record AddressDetails(int Id, string Label, string Line1, string? Line2, string City, string? PostalCode);
/// <summary>Account/customer projection that never contains hashes or passwords.</summary>
public sealed record ProfileDetails(int UserId, int CustomerId, string Email, string DisplayName,
    string FullName, string Phone, byte[]? Photo, IReadOnlyList<AddressDetails> Addresses);
/// <summary>A newly issued bearer token and its fixed expiry.</summary>
public sealed record SessionTicket(string Token, DateTime ExpiresAtUtc, ProfileDetails Profile);

/// <summary>Shared input and opaque-token rules, independent of WinUI and SQL Server.</summary>
public static class AccountRules
{
    /// <summary>Normalizes only email; passwords are never trimmed.</summary>
    public static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();

    /// <summary>Checks a single email address without requiring an actual mailbox.</summary>
    public static bool ValidEmail(string email) => email.Trim().Length is > 0 and <= 254
        && MailAddress.TryCreate(email.Trim(), out var address)
        && address.Address.Equals(email.Trim(), StringComparison.OrdinalIgnoreCase)
        && address.Host.Contains('.');

    /// <summary>Returns a useful validation message, or null when registration is valid.</summary>
    public static string? Validate(Registration input)
    {
        if (!ValidEmail(input.Email)) return "Enter a valid email address (up to 254 characters).";
        if (Validate(new ProfileBasics(input.DisplayName, input.FullName, input.Phone)) is { } error) return error;
        if (input.Password.Length is < 12 or > 128) return "Use a password between 12 and 128 characters.";
        return input.Password != input.Confirmation ? "Passwords do not match." : null;
    }

    /// <summary>Checks required profile fields using the database column limits.</summary>
    public static string? Validate(ProfileBasics input) => !Required(input.DisplayName, 80)
        ? "Enter a display name (up to 80 characters)." : !Required(input.FullName, 150)
        ? "Enter your full name (up to 150 characters)." : !Required(input.Phone, 32)
        ? "Enter a phone number (up to 32 characters)." : null;

    /// <summary>Checks address fields before attempting a database write.</summary>
    public static string? Validate(AddressInput input) => !Required(input.Label, 40) || !Required(input.Line1, 200)
        || !Required(input.City, 100) || (input.Line2?.Trim().Length ?? 0) > 200
        || (input.PostalCode?.Trim().Length ?? 0) > 20
        ? "Enter a label, address line and city within the displayed field limits." : null;

    private static bool Required(string value, int limit) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= limit;

    /// <summary>Issues an unpredictable 256-bit token; only its hash belongs in SQL Server.</summary>
    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    /// <summary>Computes the token lookup digest without retaining the bearer token.</summary>
    public static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    /// <summary>Computes the fixed lifetime selected at sign-in.</summary>
    public static DateTime Expiry(DateTime now, bool remember) => remember ? now.AddDays(7) : now.AddHours(8);
    /// <summary>Tests expiry and revocation together; equality at expiry is already expired.</summary>
    public static bool IsActive(DateTime now, DateTime expires, DateTime? revoked) => revoked is null && now < expires;
}