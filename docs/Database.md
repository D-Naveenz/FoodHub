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

The destructive command accepts only `Server=localhost;Database=FoodHub_Dev`; remote servers, other catalogs, attached files, and user-instance connections are rejected before opening SQL. If the default Development connection is insufficient, save a runtime override using an unpackaged FoodHub instance, or edit the executable-owned bundled defaults externally before running a command. Packaged FoodHub preferences belong to a separate identity. The reset creates no account or optional demonstration data. Optional fictional menu/customer/order data remains in Foodhub_Demo_Seed.sql for explicit use.

The old Baseline/Accounts chain, reference-database comparisons, backup/adoption commands, and manual migration-history insertion were removed. EF still maintains __EFMigrationsHistory automatically. After replacing the initial chain, reset the existing development database once. Do not manually remove history rows while retaining old tables. Future small changes can use ordinary migrations; destructive development rebuilds remain an explicit choice.

The app and Test Connection never migrate or reset a database. Production upgrade/backups are outside this project's current scope. The historical SQL creation document is not a second current schema authority. Existing archived backups/reference databases are not automatically deleted by this change.

## Database configuration

The UI, administration executable and EF factory all use Dhara’s shared configuration loader and native runtime settings. Core and tooling target Windows. The database project sets DharaAppSettingsDirectory to FoodHub and imports Dhara’s generic build targets to copy its selected defaults. Configuration loading is separate from the database adapter. See [desktop hosting and application settings](Architecture.md#desktop-hosting-and-application-settings) for ownership, build environments, JSON precedence and output rules.

DatabaseSettings captures the effective ConnectionStrings:FoodHub value for one launch. DatabaseConfiguration adapts that shared loader to EF design-time tools and parameterless contexts; it does not define JSON providers. Administration commands explicitly load a snapshot using their executable assembly. EF design-time creation uses the factory assembly. Explicit DbContextOptions continue to take precedence.

Packaged FoodHub uses its package data identity. Unpackaged FoodHub, console and EF tools use publisher dashe/product FoodHub, sharing native RuntimeSettings and the %LOCALAPPDATA%/dashe/FoodHub file root. Packaged GUI overrides remain separate. Database commands initialize the native runtime without constructing a UI host or window. The EF factory temporarily activates the startup executable’s manifest and resource directory in EF’s process.

## Settings and persistence

Settings → Database accepts Windows-authentication server/database fields and certificate trust. Test Connection checks a candidate with bounded SQL timeouts and cancellation, including required migration history, without saving or changing SQL. Save writes valid settings atomically even if the server is offline; Use default configuration removes only the user connection override. Both require restart and request remembered-token removal before restoration on the next launch. Existing operations use an immutable launch snapshot.

Native LocalSettings holds the Database group (ConnectionString plus ResetSessionOnStartup) and Appearance.Theme. The group is one typed native string value for atomic persistence. All feature writers share Dhara’s LocalSettingsStore, preserving unrelated values. Host IConfiguration only contains read-only base/environment files from FoodHub; runtime settings are resolved separately. No local settings JSON file is read or written. session.dat contains only Windows-user-encrypted token bytes. No account switch changes the storage root. Legacy imports and markers have been removed; theme.txt, legacy local appsettings.json and previous unpackaged paths are ignored without migration. Obsolete files are left untouched.

In Development, Settings shows the actual local storage path and an Open local storage folder button. It opens the active service root in File Explorer in either deployment mode; it never changes settings. The whole section and its command are disabled outside IHostEnvironment.IsDevelopment(), independently of Debug/Release and package identity.

Invalid native database values block fallback and can be explicitly repaired through Save/Reset. Native access failures cannot be repaired by discarding data. Malformed host files require external correction; saving local settings never edits them. Missing/invalid configuration, missing account schema, and unreachable SQL have safe, separate feedback. Diagnostics contain categories/codes, not connection payloads.

## Adding a migration

```powershell
./.tools/dotnet-ef migrations add DescriptiveChange --project FoodHub.Core --startup-project FoodHub.Database
./.tools/dotnet-ef migrations script --project FoodHub.Core --startup-project FoodHub.Database
```

Review generated changes and keep custom procedure SQL consistent. Do not use EnsureCreated with migrations. Scaffold provenance headers remain, but entities/context are maintained by hand rather than regenerated by EF Power Tools.

## Order write contracts

PlaceOrder(CustomerId, CustomerAddressId, BasketItemList, OrderId OUTPUT) validates ownership/availability and saves authoritative prices, receipt snapshots, order/items and the first event atomically. Read OutputParameter after awaiting the wrapper, then reload the receipt. Wrapper task results are not the new order ID.

UpdateOrderStatus(CustomerId, OrderId, NewStatusId) validates ownership and locks state/history transactionally. Transitions remain 10 → 20 or 90, 20 → 30, 30 → 40; 40/90 are terminal. Expected SQL errors 51001–51006 remain domain outcomes. Do not add cascading deletes or duplicate these transactions in UI code.
