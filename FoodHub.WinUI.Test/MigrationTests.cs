using FoodHub.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FoodHub.WinUI.Test;

/// <summary>Checks the migration model and generated SQL without connecting to a server.</summary>
[TestClass]
[TestCategory("Unit")]
public sealed class MigrationTests
{
    private static FoodHub_DevContext Open() => new(new DbContextOptionsBuilder<FoodHub_DevContext>()
        .UseSqlServer("Server=unused;Database=FoodHub_Test;Integrated Security=True").Options);

    /// <summary>Authored mappings must stay aligned with the migration snapshot.</summary>
    [TestMethod]
    public void SnapshotMatchesCurrentModel()
    {
        using var db = Open();
        Assert.IsFalse(db.Database.HasPendingModelChanges());
    }

    /// <summary>Fresh-install SQL must retain the stored-procedure and constraint contracts.</summary>
    [TestMethod]
    public void FreshScriptRetainsOrderObjectsAndAccountUniqueness()
    {
        using var db = Open();
        var script = db.GetService<IMigrator>().GenerateScript();
        StringAssert.Contains(script, "CREATE PROCEDURE dbo.PlaceOrder");
        StringAssert.Contains(script, "CREATE TYPE dbo.BasketItemList");
        StringAssert.Contains(script, "CK_Orders_Amounts");
        StringAssert.Contains(script, "IX_Users_NormalizedEmail");
    }
}
