# Development database

## Current schema and commands

FoodHub_Dev is disposable fictional development data. EF Core migrations own schema evolution. The maintained model and snapshot have one starting migration, `InitialCreate`, which creates tables, keys, indexes, named defaults, checks, computed values, account/session mappings, required order statuses, and the single store-settings row. `Migrations/Sql/OrderProcedures.sql` supplies the BasketItemList TVP and the two order procedures in separate GO-delimited batches.

PowerShell 7.4+ scripts replace the FoodHub.Database executable. Run from the repository root:

```powershell
# Apply pending migrations; repeat execution preserves current rows.
./scripts/Invoke-Database.ps1 Migrate

# Preview without restoring tools, building, or opening SQL.
./scripts/Invoke-Database.ps1 Reset -WhatIf

# Delete localhost/FoodHub_Dev and rebuild the current schema. No backup or extra prompt.
./scripts/Invoke-Database.ps1 Reset

# Inspect context configuration without opening SQL.
./scripts/Invoke-Database.ps1 Info

# Generate reviewable idempotent SQL; do not execute it.
./scripts/Invoke-Database.ps1 Script -OutputPath ./artifacts/database.sql
```

The script restores the repository's pinned dotnet-ef 10.0.15 local tool and builds Core once before executing EF commands. Use -NoRestore and/or -NoBuild when those prerequisites are already current. -Configuration selects the Core build (Debug by default), not a different database. Script paths are resolved independently of the current directory; an explicit relative OutputPath is resolved from the caller's directory. -WhatIf stops before any restore, build, file creation or SQL command.

Server defaults to localhost and Database to FoodHub_Dev, with Windows authentication and development certificate trust. For example, `./scripts/Invoke-Database.ps1 Migrate -Server localhost -Database AnotherDevelopmentDatabase` explicitly targets another development catalog. The script does not read appsettings or the application's native user preferences. Choose the same target explicitly in the application when needed; automation never changes runtime settings.

Reset accepts only the exact localhost/FoodHub_Dev target. Both the script and the Core design-time factory validate before SQL is opened. Reset runs EF database drop --force followed by database update, stops on the first failed command, and creates no optional demonstration data. -Confirm is available when wanted; no extra prompt is required for an explicit Reset. Optional fictional menu/customer/order data remains in Foodhub_Demo_Seed.sql for deliberate use.

The old Baseline/Accounts chain, reference-database comparisons, backup/adoption commands, and manual migration-history insertion were removed. EF still maintains __EFMigrationsHistory automatically. After replacing the initial chain, reset the existing development database once. Do not manually remove history rows while retaining old tables. Future small changes can use ordinary migrations; destructive development rebuilds remain an explicit choice.

The app and Test Connection never migrate or reset a database. Production upgrade/backups are outside this project's current scope. The historical SQL creation document is not a second current schema authority. Existing archived backups/reference databases are not automatically deleted by this change.

## Database configuration

The application uses Dhara's shared host-file loader and native runtime preferences. DatabaseSettings captures the effective connection once per launch; DatabaseConfiguration retains the parameterless runtime-context fallback. Explicit DbContextOptions take precedence.

EF automation uses FoodHub.Core directly as both target and startup project. A small FoodHubDesignTimeContextFactory builds options from --server/--database arguments after EF's -- separator; no arguments mean localhost/FoodHub_Dev. It never initializes native storage or XAML, and the --reset flag enforces the existing approved-target guard. Core generates runtime configuration files and copies its managed dependencies so EF can host the library without a separate executable. The former console program, Windows activation-context P/Invokes and native resource-directory workaround were removed.

Application JSON/build-profile selection and packaged/unpackaged preferences remain unchanged. Automation has explicit development targets rather than inheriting per-user application settings; no script writes host JSON or migrates old preferences. See [desktop hosting and settings](Architecture.md#desktop-hosting-and-application-settings).

## Settings and persistence

Settings → Database accepts Windows-authentication server/database fields and certificate trust. Test Connection checks a candidate with bounded SQL timeouts and cancellation, including required migration history, without saving or changing SQL. Save writes valid settings atomically even if the server is offline; Use default configuration removes only the user connection override. Both require restart and request remembered-token removal before restoration on the next launch. Existing operations use an immutable launch snapshot.

Database feedback uses native InfoBar severity and icons: successful checks and saves use Success, missing migrations/schema use Warning, and failed checks/configuration/saves use Error. Cancellation and authentication guidance use Informational; the separate restart reminder also stays informational. Connection checks return a typed outcome with safe text, so presentation does not depend on matching message wording.

Native LocalSettings holds the Database group (ConnectionString plus ResetSessionOnStartup) and Appearance.Theme. The group is one typed native string value for atomic persistence. All feature writers share Dhara’s LocalSettingsStore, preserving unrelated values. Host IConfiguration only contains read-only base/environment files from FoodHub; runtime settings are resolved separately. No local settings JSON file is read or written. session.dat contains only Windows-user-encrypted token bytes. No account switch changes the storage root. Legacy imports and markers have been removed; theme.txt, legacy local appsettings.json and previous unpackaged paths are ignored without migration. Obsolete files are left untouched.

In Development, Settings shows the actual local storage path as a selectable, wrapping text label and an Open local storage folder button. The path is informational and cannot be changed through the GUI. The button opens the active service root in File Explorer in either deployment mode; it never changes settings. The whole section and its command are disabled outside IHostEnvironment.IsDevelopment(), independently of Debug/Release and package identity.

Invalid native database values block fallback and can be explicitly repaired through Save/Reset. Native access failures cannot be repaired by discarding data. Malformed host files require external correction; saving local settings never edits them. Missing/invalid configuration, missing account schema, and unreachable SQL have safe, separate feedback. Diagnostics contain categories/codes, not connection payloads.

## Adding a migration

```powershell
dotnet tool restore
dotnet ef migrations add DescriptiveChange --project FoodHub.Core --startup-project FoodHub.Core
dotnet ef migrations script --project FoodHub.Core --startup-project FoodHub.Core
```

Review generated changes and keep custom procedure SQL consistent. Do not use EnsureCreated with migrations. Scaffold provenance headers remain, but entities/context are maintained by hand rather than regenerated by EF Power Tools.

## Order write contracts

PlaceOrder(CustomerId, CustomerAddressId, BasketItemList, OrderId OUTPUT) validates ownership/availability and saves authoritative prices, receipt snapshots, order/items and the first event atomically. Read OutputParameter after awaiting the wrapper, then reload the receipt. Wrapper task results are not the new order ID.

UpdateOrderStatus(CustomerId, OrderId, NewStatusId) validates ownership and locks state/history transactionally. Transitions remain 10 → 20 or 90, 20 → 30, 30 → 40; 40/90 are terminal. Expected SQL errors 51001–51006 remain domain outcomes. Do not add cascading deletes or duplicate these transactions in UI code.

## Checking automation without SQL

Run `./scripts/Test-DatabaseAutomation.ps1` to check argument boundaries, Reset ordering, invalid targets, WhatIf and fail-fast behavior using a scoped fake dotnet command. It never opens SQL. `Info` and `Script` above exercise real EF discovery/export without applying SQL. Core's design-time factory and reset policy are also covered by WinUI test-suite logic tests. A real Migrate/Reset remains an explicit database operation, not part of these checks.

References: [EF CLI](https://learn.microsoft.com/ef/core/cli/dotnet) and [design-time factories](https://learn.microsoft.com/ef/core/cli/dbcontext-creation).

## Ordering application services

The customer pages use MenuService and OrderingService with one short-lived context per operation. Every private operation validates the session and derives its customer owner; PlaceOrder and UpdateOrderStatus retain their existing contracts and SQL transactions. No schema change is required. Checkout refreshes prices/contact/address before dispatch, but the receipt always uses authoritative committed SQL snapshots.

Optional demo seed paths now reference bundled Assets/Menu SVG illustrations. Existing databases are not rewritten or seeded automatically. The seed script inserts data deliberately and is not idempotent. Transport failures after placement dispatch may be ambiguous: refresh owned history and explicitly reconcile before another attempt. No idempotency-key migration or automatic retry has been introduced.
