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

The destructive command accepts only `Server=localhost;Database=FoodHub_Dev`; remote servers, other catalogs, attached files, and user-instance connections are rejected before opening SQL. Set FOODHUB_CONNECTION_STRING if the default Development connection is insufficient, and check any higher-priority per-user override. The reset creates no account or optional demonstration data. Optional fictional menu/customer/order data remains in Foodhub_Demo_Seed.sql for explicit use.

The old Baseline/Accounts chain, reference-database comparisons, backup/adoption commands, and manual migration-history insertion were removed. EF still maintains __EFMigrationsHistory automatically. After replacing the initial chain, reset the existing development database once. Do not manually remove history rows while retaining old tables. Future small changes can use ordinary migrations; destructive development rebuilds remain an explicit choice.

The app and Test Connection never migrate or reset a database. Production upgrade/backups are outside this project's current scope. The historical SQL creation document is not a second current schema authority. Existing archived backups/reference databases are not automatically deleted by this change.

## Configuration ownership and precedence

Both appsettings.json and appsettings.Development.json live in FoodHub.Core. Their Content metadata is declared once in Core and copied transitively to executable outputs through project references. Production has an empty connection; Debug defaults to Development with localhost/FoodHub_Dev, Windows authentication and local certificate trust. Development content is excluded from Release output and from published output. No SQL password is tracked.

The host is built in App.App() using AppContext.BaseDirectory, independent of the working directory. DOTNET_ENVIRONMENT can override the Debug=Development / Release=Production default. Providers, lowest to highest priority:

1. Bundled appsettings.json.
2. Bundled appsettings.{Environment}.json.
3. Standard environment variables such as ConnectionStrings__FoodHub.
4. FOODHUB_CONNECTION_STRING compatibility alias.
5. appsettings.json in the active writable storage root.

The UI uses package LocalFolder when packaged and %LOCALAPPDATA%/FoodHub otherwise. Console/EF tools are unpackaged and use the latter root, so packaged GUI overrides are deliberately separate from command-line tool overrides. Windows.Storage APIs stay in the UI project; Core shares the JSON/environment loader and filesystem persistence code.

## Settings and persistence

Settings → Database accepts Windows-authentication server/database fields and certificate trust. Test Connection checks a candidate with bounded SQL timeouts and cancellation, including required migration history, without saving or changing SQL. Save writes valid settings atomically even if the server is offline; Use default configuration removes only the user connection override. Both require restart and request remembered-token removal before restoration on the next launch. Existing operations use an immutable launch snapshot.

Configuration is distinct from authentication: appsettings.json stores configuration, theme.txt stores appearance, and session.dat contains only Windows-user-encrypted token bytes. No FoodHub account switch changes the storage root. Packaged upgrades import legacy database/appearance files once without overwriting newer destination files; encrypted legacy tokens remain untouched and are not imported. A completion marker prevents deleted settings from being re-imported later.

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
