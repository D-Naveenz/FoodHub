# Database setup and evolution

## Schema authority

EF Core migrations now own schema evolution. The model/context originated in EF Core Power Tools; their provenance headers remain, but they are maintained code. Do not regenerate tables/context over authored mappings. The old Power Tools configuration is retained at `legacy/efpt.config.json` for historical reference. Existing procedure wrappers and the basket TVP shape are preserved.

`FoodHub.Core/Migrations` contains an immutable Baseline migration and a separate Accounts migration. Baseline executes its embedded SQL in GO-delimited batches, preserving original procedures, checks, computed values, indexes and required status/store rows. The old `Foodhub_SQL_Server_Schema.sql` is historical documentation, not a parallel current setup path. Optional fictional data is separated into `Foodhub_Demo_Seed.sql`; do not reseed existing data.

## Configuration and new databases

Use a Windows shell with .NET and access to SQL Server. Supply your own connection string; no database password is tracked:

```powershell
$env:FOODHUB_CONNECTION_STRING = 'Server=localhost;Database=FoodHub_Dev;Integrated Security=True;TrustServerCertificate=True'
dotnet run --project FoodHub.Database -- migrate
```

The certificate setting above is for the local development server. Use a trusted certificate for remote servers. EF can create an absent database when the SQL identity has permission. Migrate an empty database; do not run the historical creation script first. The app itself never runs migrations automatically. If using environment overrides, set them before starting Visual Studio or the executable. The persistent per-user configuration has higher priority. Normal Debug launches use JSON.

### Persistent configuration and Settings

The canonical key is `ConnectionStrings:FoodHub`. `appsettings.json` contains an empty production value. Debug builds default to Development and include `appsettings.Development.json` with localhost/FoodHub_Dev, Windows authentication and local certificate trust. Release defaults to Production and excludes that development file from output/package/publish content. `DOTNET_ENVIRONMENT` can explicitly select another environment. Debug publishes omit the development file as well.

Configuration is loaded once per process from the install/output directory (`AppContext.BaseDirectory`), independent of the working directory. Providers, from lowest to highest priority:

1. Bundled appsettings.json.
2. Bundled appsettings.{Environment}.json.
3. Standard environment variables, including ConnectionStrings__FoodHub.
4. Legacy FOODHUB_CONNECTION_STRING compatibility alias.
5. Writable per-user `%LOCALAPPDATA%/FoodHub/appsettings.json`.

The UI, database tool, and EF design-time factory share these rules. A user override wins even over a shell variable: inspect/reset it before using environment overrides for an administrative target. For a separate test database use FOODHUB_TEST_CONNECTION_STRING; SQL integration tests do not consult the application override. No shell variable is required for the normal Debug app launch.

In **Settings → Database**, enter the server and an existing database and choose certificate trust only for a local development server. **Test Connection** checks connectivity and required migration history using a five-second connection/command timeout and ten-second overall cancellation deadline; it never creates/migrates a database or saves the candidate. **Save** validates syntax and writes settings atomically even if the server is offline. **Use default configuration** removes the per-user connection override while preserving unrelated JSON values. Both actions require restart and request clearing remembered authentication on the next launch. Current operations continue using the original snapshot. A persistent notice remains when returning to Settings before restart.

The UI supports Windows authentication only and stores no SQL password. External configuration can still provide a SQL-authentication connection; the form warns before replacing it with Windows-authentication settings. Malformed JSON suppresses all database fallback, with a safe Settings error and explicit Save/Reset recovery. Invalid configuration, missing migration/schema, and SQL connection failures receive distinct feedback. Configuration files never contain session tokens: encrypted sign-in remains in session.dat, independently of the connection string.

Install EF tooling locally when adding/reviewing migrations:

```powershell
dotnet tool install dotnet-ef --version 10.0.15 --tool-path .tools
./.tools/dotnet-ef migrations add DescriptiveChange --project FoodHub.Core
./.tools/dotnet-ef migrations script --project FoodHub.Core
```

Review generated SQL before applying changes. Do not use EnsureCreated with this migration history. The historical baseline intentionally refuses rollback; restoring a reviewed backup is the recovery path. Rolling back Accounts removes account/session data, so review/back up first.

## Adopting an existing SQL-first database

Never apply the creation migration directly to a populated legacy database. Stop other schema-changing tools while adopting the baseline.

1. Create a separate reference database by setting `FOODHUB_CONNECTION_STRING` to its name and running `./.tools/dotnet-ef database update Baseline --project FoodHub.Core`. It must have only Baseline applied.
2. Restore `FOODHUB_CONNECTION_STRING` to the existing target database. Set `FOODHUB_BASELINE_REFERENCE` to the reference connection string.
3. Run `dotnet run --project FoodHub.Database -- verify`. The comparison covers tables, ordered columns, types, nullability, identities, computed/default values, collations, checks, indexes, foreign keys, procedures and table types. Procedure boundary whitespace and CRLF/LF are normalized; internal source differences cause rejection. EF history and application rows are excluded. `describe` prints schema-only JSON for diagnostics.
4. Run `dotnet run --project FoodHub.Database -- baseline 'C:/server/backup/FoodHub-before-accounts.bak'`. The path is on the SQL Server machine. The tool verifies schema, creates a COPY_ONLY/CHECKSUM backup, runs RESTORE VERIFYONLY, rechecks schema, and records baseline history transactionally. No customer rows are changed. A mismatch or backup failure aborts adoption.
5. Run `dotnet run --project FoodHub.Database -- migrate`. Confirm original customer/order counts and saved receipts afterward. The nullable account link leaves legacy customers unclaimed; no credentials are generated.

Use a distinct backup file for each upgrade. Keep the backup until the upgrade is accepted. Reference databases can be removed after verification; do not remove the target database as part of this workflow.

## Data and write contracts

`Users` stores account identity/password hashes/photo bytes. `UserSessions` stores token digests and fixed UTC session dates. `Customers.UserId` is nullable, filtered-unique and noncascading. Existing customer/order keys are unchanged.

`Customers` and `CustomerAddresses` own contact/delivery details. Menu/category/store tables retain current prices and settings. Orders and items retain receipt snapshots, computed line totals/order totals and unique order numbers. OrderStatusHistory records timestamped progression.

`PlaceOrder(CustomerId, CustomerAddressId, BasketItemList, OrderId OUTPUT)` validates the basket, ownership and availability and saves the order/items/initial event atomically. Read the generated OutputParameter after awaiting the wrapper, then reload the saved receipt. Wrapper task results are not the new order ID.

`UpdateOrderStatus(CustomerId, OrderId, NewStatusId)` locks the owned order and transactionally updates state/history. Allowed transitions remain 10 -> 20 or 90, 20 -> 30, 30 -> 40; 40/90 are terminal. Expected SQL errors 51001–51006 remain meaningful domain outcomes. Referenced customer/address/menu deletion is not a supported workflow. Do not add cascading deletes.

See [Verification](Verification.md) for the backup/upgrade record and SQL Server integration commands.
