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
    /// <summary>EF context discovery needs neither WinUI nor application preferences and does not open SQL.</summary>
    [TestMethod]
    public void DesignTimeDefaultsAreExplicitDevelopmentSettings()
    {
        using var context = new FoodHub.Core.Hosting.FoodHubDesignTimeContextFactory().CreateDbContext([]);
        var connection = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(
            Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.GetConnectionString(context.Database));
        Assert.AreEqual("localhost", connection.DataSource);
        Assert.AreEqual("FoodHub_Dev", connection.InitialCatalog);
        Assert.IsTrue(connection.IntegratedSecurity);
    }
    /// <summary>Provider quoting prevents explicit values from injecting connection-string options.</summary>
    [TestMethod]
    public void DesignTimeValuesAreQuotedAndResetIsRestricted()
    {
        var factory = new FoodHub.Core.Hosting.FoodHubDesignTimeContextFactory();
        using var context = factory.CreateDbContext(["--server", "fictional;server", "--database", "fictional database"]);
        var connection = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(
            Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.GetConnectionString(context.Database));
        Assert.AreEqual("fictional;server", connection.DataSource);
        Assert.AreEqual("fictional database", connection.InitialCatalog);
        Assert.Throws<InvalidOperationException>(() => factory.CreateDbContext(["--server", "remote", "--reset"]));
        Assert.Throws<ArgumentException>(() => factory.CreateDbContext(["--server"]));
        Assert.Throws<ArgumentException>(() => factory.CreateDbContext(["--unknown", "value"]));
        Assert.Throws<ArgumentException>(() => factory.CreateDbContext(["--database", "one", "--database", "two"]));
    }
}