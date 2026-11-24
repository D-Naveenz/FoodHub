/*
    Foodhub-inspired ordering and tracking | SQL Server

    Run once in an empty project database using SSMS or sqlcmd.
    Select that database before running this script. The script creates objects
    in dbo and does not create or drop a database.

    Demo data is enabled in the final batch. Set @LoadDemoData to 0 to skip it.
    LKR and a 250.00 delivery fee are sample settings. Menu photo paths are
    relative to your app's content directory. Supply the matching files in
    WinUI or display a placeholder for missing photos.

    Scaffold the tables into EF Core. Use dbo.PlaceOrder and
    dbo.UpdateOrderStatus for order writes to preserve the workflow rules.
    Customer and address edits can use normal EF Core operations.
*/

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO

CREATE TABLE dbo.StoreSettings
(
    StoreSettingsId tinyint NOT NULL,
    CurrencyCode char(3) NOT NULL,
    DeliveryFee decimal(12, 2) NOT NULL,

    CONSTRAINT PK_StoreSettings PRIMARY KEY (StoreSettingsId),
    CONSTRAINT CK_StoreSettings_SingleStore CHECK (StoreSettingsId = 1),
    CONSTRAINT CK_StoreSettings_DeliveryFee CHECK (DeliveryFee >= 0)
);

INSERT INTO dbo.StoreSettings (StoreSettingsId, CurrencyCode, DeliveryFee)
VALUES (1, 'LKR', 250.00);

CREATE TABLE dbo.Customers
(
    CustomerId int IDENTITY(1, 1) NOT NULL,
    FullName nvarchar(150) NOT NULL,
    PhoneNumber nvarchar(32) NOT NULL,

    CONSTRAINT PK_Customers PRIMARY KEY (CustomerId),
    CONSTRAINT CK_Customers_FullName CHECK (LEN(LTRIM(RTRIM(FullName))) > 0),
    CONSTRAINT CK_Customers_PhoneNumber CHECK (LEN(LTRIM(RTRIM(PhoneNumber))) > 0)
);

CREATE TABLE dbo.CustomerAddresses
(
    CustomerAddressId int IDENTITY(1, 1) NOT NULL,
    CustomerId int NOT NULL,
    AddressLabel nvarchar(40) NOT NULL,
    AddressLine1 nvarchar(200) NOT NULL,
    AddressLine2 nvarchar(200) NULL,
    City nvarchar(100) NOT NULL,
    PostalCode nvarchar(20) NULL,

    CONSTRAINT PK_CustomerAddresses PRIMARY KEY (CustomerAddressId),
    CONSTRAINT FK_CustomerAddresses_Customers FOREIGN KEY (CustomerId)
        REFERENCES dbo.Customers (CustomerId),
    CONSTRAINT UQ_CustomerAddresses_CustomerId_AddressId
        UNIQUE (CustomerId, CustomerAddressId),
    CONSTRAINT CK_CustomerAddresses_RequiredFields CHECK
    (
        LEN(LTRIM(RTRIM(AddressLabel))) > 0
        AND LEN(LTRIM(RTRIM(AddressLine1))) > 0
        AND LEN(LTRIM(RTRIM(City))) > 0
    )
);

CREATE TABLE dbo.MenuCategories
(
    MenuCategoryId int IDENTITY(1, 1) NOT NULL,
    CategoryName nvarchar(80) NOT NULL,
    DisplayOrder int NOT NULL CONSTRAINT DF_MenuCategories_DisplayOrder DEFAULT (0),

    CONSTRAINT PK_MenuCategories PRIMARY KEY (MenuCategoryId),
    CONSTRAINT UQ_MenuCategories_CategoryName UNIQUE (CategoryName),
    CONSTRAINT CK_MenuCategories_CategoryName CHECK (LEN(LTRIM(RTRIM(CategoryName))) > 0)
);

CREATE TABLE dbo.MenuItems
(
    MenuItemId int IDENTITY(1, 1) NOT NULL,
    MenuCategoryId int NOT NULL,
    ItemName nvarchar(120) NOT NULL,
    ItemDescription nvarchar(500) NULL,
    UnitPrice decimal(12, 2) NOT NULL,
    PhotoPath nvarchar(260) NULL,
    IsAvailable bit NOT NULL CONSTRAINT DF_MenuItems_IsAvailable DEFAULT (1),

    CONSTRAINT PK_MenuItems PRIMARY KEY (MenuItemId),
    CONSTRAINT FK_MenuItems_MenuCategories FOREIGN KEY (MenuCategoryId)
        REFERENCES dbo.MenuCategories (MenuCategoryId),
    CONSTRAINT CK_MenuItems_ItemName CHECK (LEN(LTRIM(RTRIM(ItemName))) > 0),
    CONSTRAINT CK_MenuItems_UnitPrice CHECK (UnitPrice >= 0)
);

CREATE INDEX IX_MenuItems_Category_ItemName
    ON dbo.MenuItems (MenuCategoryId, ItemName);

CREATE TABLE dbo.OrderStatuses
(
    OrderStatusId tinyint NOT NULL,
    StatusName nvarchar(40) NOT NULL,

    CONSTRAINT PK_OrderStatuses PRIMARY KEY (OrderStatusId),
    CONSTRAINT UQ_OrderStatuses_StatusName UNIQUE (StatusName)
);

INSERT INTO dbo.OrderStatuses (OrderStatusId, StatusName)
VALUES
    (10, N'Order placed'),
    (20, N'Preparing'),
    (30, N'Out for delivery'),
    (40, N'Delivered'),
    (90, N'Cancelled');

CREATE TABLE dbo.Orders
(
    OrderId int IDENTITY(1, 1) NOT NULL,
    -- The identity makes this display number unique without a second sequence.
    OrderNumber AS (CONVERT(varchar(13), 'FH-' + CONVERT(varchar(10), OrderId))) PERSISTED,
    CustomerId int NOT NULL,
    CustomerAddressId int NOT NULL,
    OrderStatusId tinyint NOT NULL,
    PlacedAtUtc datetime2(3) NOT NULL,
    PaymentMethod varchar(20) NOT NULL
        CONSTRAINT DF_Orders_PaymentMethod DEFAULT ('CashOnDelivery'),

    -- Keep the details used at checkout for the receipt and order history.
    CustomerName nvarchar(150) NOT NULL,
    CustomerPhone nvarchar(32) NOT NULL,
    DeliveryAddressLine1 nvarchar(200) NOT NULL,
    DeliveryAddressLine2 nvarchar(200) NULL,
    DeliveryCity nvarchar(100) NOT NULL,
    DeliveryPostalCode nvarchar(20) NULL,
    CurrencyCode char(3) NOT NULL,
    SubtotalAmount decimal(19, 2) NOT NULL,
    DeliveryFee decimal(12, 2) NOT NULL,
    TotalAmount AS (CONVERT(decimal(19, 2), SubtotalAmount + DeliveryFee)) PERSISTED,

    CONSTRAINT PK_Orders PRIMARY KEY (OrderId),
    CONSTRAINT FK_Orders_Customers FOREIGN KEY (CustomerId)
        REFERENCES dbo.Customers (CustomerId),
    CONSTRAINT FK_Orders_CustomerAddresses FOREIGN KEY (CustomerId, CustomerAddressId)
        REFERENCES dbo.CustomerAddresses (CustomerId, CustomerAddressId),
    CONSTRAINT FK_Orders_OrderStatuses FOREIGN KEY (OrderStatusId)
        REFERENCES dbo.OrderStatuses (OrderStatusId),
    CONSTRAINT CK_Orders_Amounts CHECK (SubtotalAmount >= 0 AND DeliveryFee >= 0),
    CONSTRAINT CK_Orders_PaymentMethod CHECK (PaymentMethod = 'CashOnDelivery')
);

CREATE INDEX IX_Orders_Customer_PlacedAt
    ON dbo.Orders (CustomerId, PlacedAtUtc DESC, OrderId DESC);

CREATE TABLE dbo.OrderItems
(
    OrderItemId int IDENTITY(1, 1) NOT NULL,
    OrderId int NOT NULL,
    MenuItemId int NOT NULL,
    ItemName nvarchar(120) NOT NULL,
    Quantity smallint NOT NULL,
    UnitPrice decimal(12, 2) NOT NULL,
    LineTotal AS (CONVERT(decimal(19, 2), Quantity * UnitPrice)) PERSISTED,

    CONSTRAINT PK_OrderItems PRIMARY KEY (OrderItemId),
    CONSTRAINT FK_OrderItems_Orders FOREIGN KEY (OrderId)
        REFERENCES dbo.Orders (OrderId),
    CONSTRAINT FK_OrderItems_MenuItems FOREIGN KEY (MenuItemId)
        REFERENCES dbo.MenuItems (MenuItemId),
    CONSTRAINT UQ_OrderItems_Order_MenuItem UNIQUE (OrderId, MenuItemId),
    CONSTRAINT CK_OrderItems_Quantity CHECK (Quantity > 0),
    CONSTRAINT CK_OrderItems_UnitPrice CHECK (UnitPrice >= 0)
);

CREATE TABLE dbo.OrderStatusHistory
(
    OrderStatusHistoryId int IDENTITY(1, 1) NOT NULL,
    OrderId int NOT NULL,
    OrderStatusId tinyint NOT NULL,
    ChangedAtUtc datetime2(3) NOT NULL,

    CONSTRAINT PK_OrderStatusHistory PRIMARY KEY (OrderStatusHistoryId),
    CONSTRAINT FK_OrderStatusHistory_Orders FOREIGN KEY (OrderId)
        REFERENCES dbo.Orders (OrderId),
    CONSTRAINT FK_OrderStatusHistory_OrderStatuses FOREIGN KEY (OrderStatusId)
        REFERENCES dbo.OrderStatuses (OrderStatusId),
    CONSTRAINT UQ_OrderStatusHistory_Order_Status UNIQUE (OrderId, OrderStatusId)
);
GO

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

-- Optional presentation data. All customer details below are fictional.
DECLARE @LoadDemoData bit = 1;

IF @LoadDemoData = 1
BEGIN
    BEGIN TRY
        BEGIN TRANSACTION;

        INSERT INTO dbo.MenuCategories (CategoryName, DisplayOrder)
        VALUES (N'Mains', 1), (N'Sides', 2), (N'Drinks', 3);

        INSERT INTO dbo.MenuItems
            (MenuCategoryId, ItemName, ItemDescription, UnitPrice, PhotoPath, IsAvailable)
        SELECT category.MenuCategoryId, sample.ItemName, sample.ItemDescription,
            sample.UnitPrice, sample.PhotoPath, sample.IsAvailable
        FROM
        (
            VALUES
                (N'Mains', N'Chicken kottu', N'Chopped roti with chicken and vegetables.',
                    1250.00, N'Assets/Menu/chicken-kottu.jpg', 1),
                (N'Mains', N'Vegetable fried rice', N'Fried rice with mixed vegetables.',
                    950.00, N'Assets/Menu/vegetable-fried-rice.jpg', 1),
                (N'Sides', N'Potato wedges', N'Crisp seasoned potato wedges.',
                    550.00, N'Assets/Menu/potato-wedges.jpg', 1),
                (N'Drinks', N'Fresh lime juice', N'Chilled lime juice.',
                    400.00, N'Assets/Menu/lime-juice.jpg', 1),
                (N'Drinks', N'Ginger iced tea', N'Black tea with ginger and lime.',
                    450.00, N'Assets/Menu/ginger-iced-tea.jpg', 0)
        ) AS sample (CategoryName, ItemName, ItemDescription, UnitPrice, PhotoPath, IsAvailable)
        INNER JOIN dbo.MenuCategories AS category
            ON category.CategoryName = sample.CategoryName;

        DECLARE @DemoCustomerId int;
        DECLARE @DemoAddressId int;
        DECLARE @SecondCustomerId int;
        DECLARE @DemoOrderId int;
        DECLARE @DemoBasket dbo.BasketItemList;

        INSERT INTO dbo.Customers (FullName, PhoneNumber)
        VALUES (N'Demo Customer One', N'0000000001');
        SET @DemoCustomerId = CONVERT(int, SCOPE_IDENTITY());

        INSERT INTO dbo.Customers (FullName, PhoneNumber)
        VALUES (N'Demo Customer Two', N'0000000002');
        SET @SecondCustomerId = CONVERT(int, SCOPE_IDENTITY());

        INSERT INTO dbo.CustomerAddresses (CustomerId, AddressLabel, AddressLine1, City)
        VALUES (@DemoCustomerId, N'Home', N'1 Sample Lane', N'Colombo');
        SET @DemoAddressId = CONVERT(int, SCOPE_IDENTITY());

        INSERT INTO dbo.CustomerAddresses (CustomerId, AddressLabel, AddressLine1, City)
        VALUES
            (@DemoCustomerId, N'Work', N'2 Sample Lane', N'Colombo'),
            (@SecondCustomerId, N'Home', N'3 Sample Lane', N'Colombo');

        INSERT INTO @DemoBasket (MenuItemId, Quantity)
        SELECT MenuItemId, 1
        FROM dbo.MenuItems
        WHERE ItemName IN (N'Chicken kottu', N'Fresh lime juice');

        -- An active order at each stage, plus delivered and cancelled examples.
        EXEC dbo.PlaceOrder @DemoCustomerId, @DemoAddressId, @DemoBasket, @DemoOrderId OUTPUT;

        EXEC dbo.PlaceOrder @DemoCustomerId, @DemoAddressId, @DemoBasket, @DemoOrderId OUTPUT;
        EXEC dbo.UpdateOrderStatus @DemoCustomerId, @DemoOrderId, 20;

        EXEC dbo.PlaceOrder @DemoCustomerId, @DemoAddressId, @DemoBasket, @DemoOrderId OUTPUT;
        EXEC dbo.UpdateOrderStatus @DemoCustomerId, @DemoOrderId, 20;
        EXEC dbo.UpdateOrderStatus @DemoCustomerId, @DemoOrderId, 30;

        EXEC dbo.PlaceOrder @DemoCustomerId, @DemoAddressId, @DemoBasket, @DemoOrderId OUTPUT;
        EXEC dbo.UpdateOrderStatus @DemoCustomerId, @DemoOrderId, 20;
        EXEC dbo.UpdateOrderStatus @DemoCustomerId, @DemoOrderId, 30;
        EXEC dbo.UpdateOrderStatus @DemoCustomerId, @DemoOrderId, 40;

        EXEC dbo.PlaceOrder @DemoCustomerId, @DemoAddressId, @DemoBasket, @DemoOrderId OUTPUT;
        EXEC dbo.UpdateOrderStatus @DemoCustomerId, @DemoOrderId, 90;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO

/*
    Read examples. In WinUI use parameters for the selected customer and order.
    Sort timeline events by the history ID to preserve their insertion order,
    including when multiple demo events have the same timestamp.

    SELECT o.OrderId, o.OrderNumber, s.StatusName, o.PlacedAtUtc,
           o.CurrencyCode, o.TotalAmount
    FROM dbo.Orders AS o
    INNER JOIN dbo.OrderStatuses AS s ON s.OrderStatusId = o.OrderStatusId
    WHERE o.CustomerId = 1
    ORDER BY o.PlacedAtUtc DESC, o.OrderId DESC;

    SELECT s.StatusName, h.ChangedAtUtc
    FROM dbo.OrderStatusHistory AS h
    INNER JOIN dbo.OrderStatuses AS s ON s.OrderStatusId = h.OrderStatusId
    INNER JOIN dbo.Orders AS o ON o.OrderId = h.OrderId
    WHERE o.CustomerId = 1 AND h.OrderId = 4
    ORDER BY h.OrderStatusHistoryId;
*/
