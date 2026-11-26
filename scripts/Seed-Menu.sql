-- Optional fictional menu only. Run explicitly against the intended development database:
-- sqlcmd -S localhost -d FoodHub_Dev -E -C -b -i scripts/Seed-Menu.sql
-- Repeat runs add missing examples without overwriting existing menu edits or customer/order data.
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    -- Range locks make concurrent seed runs agree on which example rows are missing.
    INSERT INTO dbo.MenuCategories (CategoryName, DisplayOrder)
    SELECT sample.CategoryName, sample.DisplayOrder
    FROM (VALUES (N'Mains', 1), (N'Sides', 2), (N'Drinks', 3)) AS sample(CategoryName, DisplayOrder)
    WHERE NOT EXISTS
    (
        SELECT 1 FROM dbo.MenuCategories WITH (UPDLOCK, HOLDLOCK)
        WHERE CategoryName = sample.CategoryName
    );

    INSERT INTO dbo.MenuItems
        (MenuCategoryId, ItemName, ItemDescription, UnitPrice, PhotoPath, IsAvailable)
    SELECT category.MenuCategoryId, sample.ItemName, sample.ItemDescription,
        sample.UnitPrice, sample.PhotoPath, sample.IsAvailable
    FROM
    (
        VALUES
            (N'Mains', N'Chicken kottu', N'Chopped roti with chicken and vegetables.',
                1250.00, N'Assets/Menu/chicken-kottu.svg', 1),
            (N'Mains', N'Vegetable fried rice', N'Fried rice with mixed vegetables.',
                950.00, N'Assets/Menu/vegetable-fried-rice.svg', 1),
            (N'Sides', N'Potato wedges', N'Crisp seasoned potato wedges.',
                550.00, N'Assets/Menu/potato-wedges.svg', 1),
            (N'Drinks', N'Fresh lime juice', N'Chilled lime juice.',
                400.00, N'Assets/Menu/lime-juice.svg', 1),
            (N'Drinks', N'Ginger iced tea', N'Black tea with ginger and lime.',
                450.00, N'Assets/Menu/ginger-iced-tea.svg', 0)
    ) AS sample(CategoryName, ItemName, ItemDescription, UnitPrice, PhotoPath, IsAvailable)
    INNER JOIN dbo.MenuCategories AS category ON category.CategoryName = sample.CategoryName
    WHERE NOT EXISTS
    (
        SELECT 1 FROM dbo.MenuItems WITH (UPDLOCK, HOLDLOCK)
        WHERE MenuCategoryId = category.MenuCategoryId AND ItemName = sample.ItemName
    );

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
