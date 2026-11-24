-- Order-write contracts used by the current initial migration.
-- One row per menu item. The client combines duplicates into a single quantity.
CREATE TYPE dbo.BasketItemList AS TABLE
(
    MenuItemId int NOT NULL PRIMARY KEY,
    Quantity smallint NOT NULL CHECK (Quantity > 0)
);
GO

CREATE PROCEDURE dbo.PlaceOrder
    @CustomerId int,
    @CustomerAddressId int,
    @Items dbo.BasketItemList READONLY,
    @OrderId int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @OrderId = NULL;

    IF NOT EXISTS (SELECT 1 FROM @Items)
        THROW 51001, 'Add at least one item before placing the order.', 1;

    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @CustomerName nvarchar(150);
        DECLARE @CustomerPhone nvarchar(32);
        DECLARE @AddressLine1 nvarchar(200);
        DECLARE @AddressLine2 nvarchar(200);
        DECLARE @City nvarchar(100);
        DECLARE @PostalCode nvarchar(20);
        DECLARE @CurrencyCode char(3);
        DECLARE @DeliveryFee decimal(12, 2);
        DECLARE @Subtotal decimal(19, 2);
        DECLARE @PlacedAtUtc datetime2(3);
        DECLARE @NewOrderId int;

        SELECT
            @CustomerName = customer.FullName,
            @CustomerPhone = customer.PhoneNumber,
            @AddressLine1 = address.AddressLine1,
            @AddressLine2 = address.AddressLine2,
            @City = address.City,
            @PostalCode = address.PostalCode
        FROM dbo.Customers AS customer WITH (HOLDLOCK)
        INNER JOIN dbo.CustomerAddresses AS address WITH (HOLDLOCK)
            ON address.CustomerId = customer.CustomerId
        WHERE customer.CustomerId = @CustomerId
            AND address.CustomerAddressId = @CustomerAddressId;

        IF @AddressLine1 IS NULL
            THROW 51002, 'Select a saved delivery address for this customer.', 1;

        SELECT @CurrencyCode = CurrencyCode, @DeliveryFee = DeliveryFee
        FROM dbo.StoreSettings WITH (HOLDLOCK)
        WHERE StoreSettingsId = 1;

        IF @DeliveryFee IS NULL
            THROW 51003, 'Set the delivery fee and currency before accepting orders.', 1;

        DECLARE @PricedItems TABLE
        (
            MenuItemId int NOT NULL PRIMARY KEY,
            ItemName nvarchar(120) NOT NULL,
            Quantity smallint NOT NULL,
            UnitPrice decimal(12, 2) NOT NULL
        );

        INSERT INTO @PricedItems (MenuItemId, ItemName, Quantity, UnitPrice)
        SELECT menu.MenuItemId, menu.ItemName, basket.Quantity, menu.UnitPrice
        FROM @Items AS basket
        INNER JOIN dbo.MenuItems AS menu WITH (HOLDLOCK)
            ON menu.MenuItemId = basket.MenuItemId
        WHERE menu.IsAvailable = 1;

        IF (SELECT COUNT(*) FROM @PricedItems) <> (SELECT COUNT(*) FROM @Items)
            THROW 51004, 'One or more items are unavailable. Update the basket.', 1;

        SELECT @Subtotal = SUM(CONVERT(decimal(19, 2), Quantity * UnitPrice))
        FROM @PricedItems;

        SET @PlacedAtUtc = SYSUTCDATETIME();

        INSERT INTO dbo.Orders
        (
            CustomerId, CustomerAddressId, OrderStatusId, PlacedAtUtc,
            CustomerName, CustomerPhone, DeliveryAddressLine1, DeliveryAddressLine2,
            DeliveryCity, DeliveryPostalCode, CurrencyCode, SubtotalAmount, DeliveryFee
        )
        VALUES
        (
            @CustomerId, @CustomerAddressId, 10, @PlacedAtUtc,
            @CustomerName, @CustomerPhone, @AddressLine1, @AddressLine2,
            @City, @PostalCode, @CurrencyCode, @Subtotal, @DeliveryFee
        );

        SET @NewOrderId = CONVERT(int, SCOPE_IDENTITY());

        INSERT INTO dbo.OrderItems (OrderId, MenuItemId, ItemName, Quantity, UnitPrice)
        SELECT @NewOrderId, MenuItemId, ItemName, Quantity, UnitPrice
        FROM @PricedItems;

        INSERT INTO dbo.OrderStatusHistory (OrderId, OrderStatusId, ChangedAtUtc)
        VALUES (@NewOrderId, 10, @PlacedAtUtc);

        COMMIT TRANSACTION;
        SET @OrderId = @NewOrderId;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO

CREATE PROCEDURE dbo.UpdateOrderStatus
    @CustomerId int,
    @OrderId int,
    @NewStatusId tinyint
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @CurrentStatusId tinyint;
        DECLARE @ChangedAtUtc datetime2(3);

        -- Lock this order so two actions cannot both apply to the same stage.
        SELECT @CurrentStatusId = OrderStatusId
        FROM dbo.Orders WITH (UPDLOCK, HOLDLOCK)
        WHERE OrderId = @OrderId AND CustomerId = @CustomerId;

        IF @CurrentStatusId IS NULL
            THROW 51005, 'No order was found for this customer.', 1;

        IF @NewStatusId IS NULL OR NOT
        (
            (@CurrentStatusId = 10 AND @NewStatusId IN (20, 90))
            OR (@CurrentStatusId = 20 AND @NewStatusId = 30)
            OR (@CurrentStatusId = 30 AND @NewStatusId = 40)
        )
            THROW 51006, 'The order cannot move to that status from its current stage.', 1;

        SET @ChangedAtUtc = SYSUTCDATETIME();

        UPDATE dbo.Orders
        SET OrderStatusId = @NewStatusId
        WHERE OrderId = @OrderId AND CustomerId = @CustomerId;

        INSERT INTO dbo.OrderStatusHistory (OrderId, OrderStatusId, ChangedAtUtc)
        VALUES (@OrderId, @NewStatusId, @ChangedAtUtc);

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO


