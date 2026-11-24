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

The certificate setting above is for the local development server. Use a trusted certificate for remote servers. EF can create an absent database when the SQL identity has permission. Migrate an empty database; do not run the historical creation script first. The app itself never runs migrations automatically. Set the environment before starting Visual Studio or launching the executable so the app inherits it.

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
