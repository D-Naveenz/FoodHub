# Development database

## Current schema and commands

FoodHub_Dev is disposable fictional development data. EF Core migrations own schema evolution. The maintained model and snapshot have one starting migration, `InitialCreate`, which creates tables, keys, indexes, named defaults, checks, computed values, account/session mappings, required order statuses, and the single store-settings row. `Migrations/Sql/OrderProcedures.sql` supplies the BasketItemList TVP and the two order procedures in separate GO-delimited batches.

The tool has two commands:

```powershell
# Apply pending migrations; repeat execution preserves current rows.
dotnet run --project FoodHub.Database -- migrate

# Delete all local development rows and recreate the current schema. No backup or extra prompt.
dotnet run --project FoodHub.Database -- reset
```

The destructive command accepts only `Server=localhost;Database=FoodHub_Dev`; remote servers, other catalogs, attached files, and user-instance connections are rejected before opening SQL. If the default Development connection is insufficient, edit the local appsettings.json in the unpackaged storage root; check this override before running a command. The reset creates no account or optional demonstration data. Optional fictional menu/customer/order data remains in Foodhub_Demo_Seed.sql for explicit use.

The old Baseline/Accounts chain, reference-database comparisons, backup/adoption commands, and manual migration-history insertion were removed. EF still maintains __EFMigrationsHistory automatically. After replacing the initial chain, reset the existing development database once. Do not manually remove history rows while retaining old tables. Future small changes can use ordinary migrations; destructive development rebuilds remain an explicit choice.

The app and Test Connection never migrate or reset a database. Production upgrade/backups are outside this project's current scope. The historical SQL creation document is not a second current schema authority. Existing archived backups/reference databases are not automatically deleted by this change.

## Database configuration

The UI uses Dhara’s reusable WinUI host configuration; Core retains a portable loader for administration/EF tooling. Configuration loading is separate from the database adapter. See [desktop hosting and application settings](Architecture.md#desktop-hosting-and-application-settings) for ownership, build environments, JSON precedence and output rules.

DatabaseSettings captures the effective ConnectionStrings:FoodHub value for one launch. DatabaseConfiguration adapts that shared loader to EF design-time tools and parameterless contexts; it does not define JSON providers. Administration commands explicitly load a snapshot using their executable assembly. EF design-time creation uses the factory assembly. Explicit DbContextOptions continue to take precedence.

The UI uses package LocalFolder when packaged and %LOCALAPPDATA%/FoodHub otherwise. Console/EF tools use the unpackaged root, so packaged GUI overrides remain separate from tool overrides. Database commands never construct a UI host.

## Settings and persistence

Settings → Database accepts Windows-authentication server/database fields and certificate trust. Test Connection checks a candidate with bounded SQL timeouts and cancellation, including required migration history, without saving or changing SQL. Save writes valid settings atomically even if the server is offline; Use default configuration removes only the user connection override. Both require restart and request remembered-token removal before restoration on the next launch. Existing operations use an immutable launch snapshot.

General local appsettings.json stores database overrides, Appearance:Theme and the durable session-reset marker. All feature writers share LocalSettingsStore, preserving unrelated values. session.dat contains only Windows-user-encrypted token bytes. No account switch changes the storage root. Legacy imports and markers have been removed; theme.txt is ignored without migration. Obsolete files are left untouched.

In Development, Settings shows the actual local storage path and an Open local storage folder button. It opens the active service root in File Explorer in either deployment mode; it never changes settings. The whole section and its command are disabled outside IHostEnvironment.IsDevelopment(), independently of Debug/Release and package identity.

Malformed user JSON blocks database fallback and can be explicitly repaired through Save/Reset. Missing/invalid configuration, missing account schema, and unreachable SQL have safe, separate feedback. Diagnostics contain categories/codes, not connection payloads.

## Adding a migration

```powershell
./.tools/dotnet-ef migrations add DescriptiveChange --project FoodHub.Core
./.tools/dotnet-ef migrations script --project FoodHub.Core
```

Review generated changes and keep custom procedure SQL consistent. Do not use EnsureCreated with migrations. Scaffold provenance headers remain, but entities/context are maintained by hand rather than regenerated by EF Power Tools.

## Order write contracts

PlaceOrder(CustomerId, CustomerAddressId, BasketItemList, OrderId OUTPUT) validates ownership/availability and saves authoritative prices, receipt snapshots, order/items and the first event atomically. Read OutputParameter after awaiting the wrapper, then reload the receipt. Wrapper task results are not the new order ID.

UpdateOrderStatus(CustomerId, OrderId, NewStatusId) validates ownership and locks state/history transactionally. Transitions remain 10 → 20 or 90, 20 → 30, 30 → 40; 40/90 are terminal. Expected SQL errors 51001–51006 remain domain outcomes. Do not add cascading deletes or duplicate these transactions in UI code.
