using FoodHub.Core.Models;
using FoodHub.Core.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace FoodHub.WinUI.Test;

/// <summary>Account validation, password hashing and session rules that do not require a UI thread or database.</summary>
[TestClass]
[TestCategory("Unit")]
public sealed class AccountRulesTests
{
    private static Registration Example => new("Ada", "Ada Example", " Ada@Example.test ", "0771234567",
        " twelve chars! ", " twelve chars! ", false);

    /// <summary>A syntactically valid fictional account may register without a real mailbox.</summary>
    [TestMethod]
    public void ValidRegistrationPreservesPasswordSpaces() => Assert.IsNull(AccountRules.Validate(Example));

    /// <summary>Email alone is trimmed and normalized for case-insensitive lookup.</summary>
    [TestMethod]
    public void EmailNormalizationTrimsAndFoldsCase() => Assert.AreEqual("ADA@EXAMPLE.TEST", AccountRules.NormalizeEmail(Example.Email));

    /// <summary>Confirmation compares exact password values rather than silently trimming them.</summary>
    [TestMethod]
    public void PasswordConfirmationDoesNotTrimSpaces() => Assert.IsNotNull(AccountRules.Validate(Example with { Confirmation = Example.Password.Trim() }));

    /// <summary>Registration rejects passwords below the supported minimum.</summary>
    [TestMethod]
    public void ShortPasswordsAreRejected() => Assert.IsNotNull(AccountRules.Validate(Example with { Password = "short", Confirmation = "short" }));

    /// <summary>Oversized passwords are rejected instead of being truncated.</summary>
    [TestMethod]
    public void OversizedPasswordsAreRejected() => Assert.IsNotNull(AccountRules.Validate(Example with { Password = new string('x', 129), Confirmation = new string('x', 129) }));

    /// <summary>The display-name validator matches the SQL column limit.</summary>
    [TestMethod]
    public void DisplayNameLimitIsEnforced() => Assert.IsNotNull(AccountRules.Validate(Example with { DisplayName = new string('x', 81) }));

    /// <summary>A sign-in address is one email, without a display-name wrapper.</summary>
    [TestMethod]
    public void EmailSyntaxDoesNotRequireMailboxVerification()
    {
        Assert.IsFalse(AccountRules.ValidEmail("name <a@example.test>"));
        Assert.IsFalse(AccountRules.ValidEmail("a@"));
        Assert.IsTrue(AccountRules.ValidEmail("a@example.test"));
    }

    /// <summary>Only line 2 and postal code can be omitted from a saved address.</summary>
    [TestMethod]
    public void AddressRequiresItsDeliveryFields()
    {
        Assert.IsNull(AccountRules.Validate(new AddressInput(null, "Home", "1 Example Road", null, "Colombo", null)));
        Assert.IsNotNull(AccountRules.Validate(new AddressInput(null, "Home", "", null, "Colombo", null)));
    }

    /// <summary>Remembered and memory-only sessions have fixed, distinct lifetimes.</summary>
    [TestMethod]
    public void SessionLifetimesAreFixed()
    {
        var now = new DateTime(2026, 11, 24, 0, 0, 0, DateTimeKind.Utc);
        Assert.AreEqual(now.AddHours(8), AccountRules.Expiry(now, false));
        Assert.AreEqual(now.AddDays(7), AccountRules.Expiry(now, true));
    }

    /// <summary>The exact expiry instant and any revocation both invalidate a session.</summary>
    [TestMethod]
    public void ExpiryAndRevocationInvalidateSession()
    {
        var now = new DateTime(2026, 11, 24, 0, 0, 0, DateTimeKind.Utc);
        Assert.IsFalse(AccountRules.IsActive(now, now, null));
        Assert.IsFalse(AccountRules.IsActive(now, now.AddDays(1), now));
    }

    /// <summary>Tokens contain 256 random bits and are represented by a different lookup digest.</summary>
    [TestMethod]
    public void TokensAreRandomAndStoredByDigest()
    {
        var token = AccountRules.NewToken();
        Assert.AreEqual(64, token.Length);
        Assert.AreNotEqual(token, AccountRules.NewToken());
        Assert.AreNotEqual(token, AccountRules.HashToken(token));
    }

    /// <summary>The versioned password hasher verifies the exact password, including spaces.</summary>
    [TestMethod]
    public void PasswordHashVerificationPreservesSpaces()
    {
        var user = new User();
        var hasher = new PasswordHasher<User>();
        var hash = hasher.HashPassword(user, Example.Password);
        Assert.AreNotEqual(Example.Password, hash);
        Assert.AreNotEqual(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(user, hash, Example.Password));
        Assert.AreEqual(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(user, hash, Example.Password.Trim()));
    }

    /// <summary>An older supported work factor requests a hash upgrade after successful verification.</summary>
    [TestMethod]
    public void OlderHashesRequestUpgrade()
    {
        var user = new User();
        var oldHasher = new PasswordHasher<User>(Options.Create(new PasswordHasherOptions { IterationCount = 10000 }));
        var hash = oldHasher.HashPassword(user, Example.Password);
        Assert.AreEqual(PasswordVerificationResult.SuccessRehashNeeded,
            new PasswordHasher<User>().VerifyHashedPassword(user, hash, Example.Password));
    }
}
