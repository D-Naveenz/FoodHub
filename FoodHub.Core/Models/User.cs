namespace FoodHub.Core.Models;

/// <summary>Account identity, separate from customer delivery/contact details.</summary>
public sealed class User
{
    /// <summary>Database account identity.</summary>
    public int UserId { get; set; }
    /// <summary>Email shown to the account holder.</summary>
    public string Email { get; set; } = "";
    /// <summary>Case-normalized unique sign-in key.</summary>
    public string NormalizedEmail { get; set; } = "";
    /// <summary>Versioned salted password hash, never plaintext.</summary>
    public string PasswordHash { get; set; } = "";
    /// <summary>Name used in navigation and avatar initials.</summary>
    public string DisplayName { get; set; } = "";
    /// <summary>Optional normalized PNG, independent of the source file.</summary>
    public byte[]? Photo { get; set; }
    /// <summary>UTC registration time.</summary>
    public DateTime CreatedAtUtc { get; set; }
    /// <summary>Optional associated customer.</summary>
    public Customer? Customer { get; set; }
}

/// <summary>Stores only the digest of an expiring opaque bearer token.</summary>
public sealed class UserSession
{
    /// <summary>Database session identity.</summary>
    public long UserSessionId { get; set; }
    /// <summary>Owning account ID.</summary>
    public int UserId { get; set; }
    /// <summary>SHA-256 digest as uppercase hex.</summary>
    public string TokenHash { get; set; } = "";
    /// <summary>UTC issue time.</summary>
    public DateTime CreatedAtUtc { get; set; }
    /// <summary>Fixed UTC expiry; reads do not renew it.</summary>
    public DateTime ExpiresAtUtc { get; set; }
    /// <summary>Revocation time, or null while usable.</summary>
    public DateTime? RevokedAtUtc { get; set; }
    /// <summary>Owning account.</summary>
    public User User { get; set; } = null!;
}

/// <summary>Adds account ownership without rewriting historical scaffolded fields.</summary>
public partial class Customer
{
    /// <summary>Optional account; legacy customers remain unclaimed.</summary>
    public int? UserId { get; set; }
    /// <summary>Linked account, if any.</summary>
    public virtual User? User { get; set; }
}
