using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace FoodHub.Core.Models;

/// <summary>Keeps the EF snapshot aligned with constraints that the original SQL script owns.</summary>
public partial class FoodHub_DevContext
{
    /// <summary>The baseline has only explicitly declared indexes, not convention-created FK indexes.</summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        => configurationBuilder.Conventions.Remove(typeof(ForeignKeyIndexConvention));

    private static void ConfigureBaseline(ModelBuilder b)
    {
        b.Entity<Customer>().ToTable("Customers", t =>
        {
            t.HasCheckConstraint("CK_Customers_FullName", "LEN(LTRIM(RTRIM(FullName))) > 0");
            t.HasCheckConstraint("CK_Customers_PhoneNumber", "LEN(LTRIM(RTRIM(PhoneNumber))) > 0");
        });
        b.Entity<CustomerAddress>().ToTable("CustomerAddresses", t => t.HasCheckConstraint("CK_CustomerAddresses_RequiredFields",
            "LEN(LTRIM(RTRIM(AddressLabel))) > 0 AND LEN(LTRIM(RTRIM(AddressLine1))) > 0 AND LEN(LTRIM(RTRIM(City))) > 0"));
        b.Entity<MenuCategory>().ToTable("MenuCategories", t => t.HasCheckConstraint("CK_MenuCategories_CategoryName", "LEN(LTRIM(RTRIM(CategoryName))) > 0"));
        b.Entity<MenuCategory>().Property(c => c.DisplayOrder).HasDefaultValue(0, "DF_MenuCategories_DisplayOrder");
        b.Entity<MenuItem>().ToTable("MenuItems", t =>
        {
            t.HasCheckConstraint("CK_MenuItems_ItemName", "LEN(LTRIM(RTRIM(ItemName))) > 0");
            t.HasCheckConstraint("CK_MenuItems_UnitPrice", "UnitPrice >= 0");
        });
        b.Entity<Order>().ToTable("Orders", t =>
        {
            t.HasCheckConstraint("CK_Orders_Amounts", "SubtotalAmount >= 0 AND DeliveryFee >= 0");
            t.HasCheckConstraint("CK_Orders_PaymentMethod", "PaymentMethod = 'CashOnDelivery'");
        });
        b.Entity<OrderItem>().ToTable("OrderItems", t =>
        {
            t.HasCheckConstraint("CK_OrderItems_Quantity", "Quantity > 0");
            t.HasCheckConstraint("CK_OrderItems_UnitPrice", "UnitPrice >= 0");
        });
        b.Entity<StoreSetting>().ToTable("StoreSettings", t =>
        {
            t.HasCheckConstraint("CK_StoreSettings_SingleStore", "StoreSettingsId = 1");
            t.HasCheckConstraint("CK_StoreSettings_DeliveryFee", "DeliveryFee >= 0");
        });
    }
}
