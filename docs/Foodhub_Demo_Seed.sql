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

