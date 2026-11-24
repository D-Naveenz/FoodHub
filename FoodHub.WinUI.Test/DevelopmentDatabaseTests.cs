using FoodHub.Core.Services;

namespace FoodHub.WinUI.Test;

/// <summary>Ensures destructive tooling rejects unrelated databases before connecting.</summary>
[TestClass]
[TestCategory("Unit")]
public sealed class DevelopmentDatabaseTests
{
    /// <summary>The approved local development database can be reset without a backup workflow.</summary>
    [TestMethod]
    public void LocalDevelopmentTargetIsAllowed() => DevelopmentDatabase.ValidateResetTarget("Server=localhost;Database=FoodHub_Dev;Integrated Security=True");
    /// <summary>Remote, system, and unrelated database names are never destructive reset targets.</summary>
    [TestMethod]
    [DataRow("Server=remote;Database=FoodHub_Dev;Integrated Security=True")]
    [DataRow("Server=localhost;Database=master;Integrated Security=True")]
    [DataRow("Server=localhost;Database=FoodHub_Production;Integrated Security=True")]
    [DataRow("Server=localhost;Database=FoodHub_Dev;AttachDbFilename=C:\\other.mdf;Integrated Security=True")]
    public void UnapprovedTargetIsRejected(string connection) => Assert.Throws<InvalidOperationException>(() => DevelopmentDatabase.ValidateResetTarget(connection));
}