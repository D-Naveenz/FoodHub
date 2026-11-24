# Database setup and evolution

The current baseline is SQL Server with EF Core Power Tools scaffolding. SQL-first remains the provisional workflow. Do not mix schema changes from EF migrations with direct SQL changes without deliberately selecting a new source of truth.

## Safe initial setup

1. Start a SQL Server instance and create an empty development database, conventionally `FoodHub_Dev`.
2. Select that database in SSMS, then run [the schema script](Foodhub_SQL_Server_Schema.sql). The script creates tables, a table type, procedures, settings, and optional demo data. It neither creates nor drops a database.
3. Inspect the resulting objects and fictional seed data. Do not rerun the creation script on a populated database; it is not idempotent.

Equivalent PowerShell command, after separately creating the empty database:

```powershell
sqlcmd -S localhost -d FoodHub_Dev -E -b -i .\docs\Foodhub_SQL_Server_Schema.sql
```

`-E` uses Windows authentication and `-b` reports SQL errors as command failure. Use the appropriate instance name and your local SQL tooling's trusted certificate configuration. Do not weaken certificate checks for a remote database merely to copy the local development setup.

The script defaults to loading demo data. Change its final batch's `@LoadDemoData` to 0 before execution if a clean dataset is needed. Current samples use LKR, a 250.00 delivery fee, two fictional customers, five menu items, and orders covering active, delivered, and cancelled states. Menu photos must be supplied by the app or replaced with a placeholder.

## Data map

| Objects | Responsibility |
|---|---|
| Customers, CustomerAddresses | Local profiles and their saved delivery addresses |
| MenuCategories, MenuItems | Categorized menu, current prices, availability, and local photo paths |
| StoreSettings | One store's currency and delivery fee |
| Orders, OrderItems | Saved receipt, contact/address snapshots, quantities, and price snapshots |
| OrderStatuses, OrderStatusHistory | Current order state and timestamped progression |
| BasketItemList | Table-valued parameter with one positive quantity per menu item |

Order number, line totals, and order total are computed database values. Scaffolded nullable properties for computed values do not mean the UI should invent replacements; reload the saved result. Keep historical receipts based on snapshot fields rather than the customer's current profile or the current menu.

## Order write contracts

`dbo.PlaceOrder` accepts customer ID, address ID, and `BasketItemList`; it returns the new order ID through an output parameter. It validates a nonempty basket, address ownership, settings, and item availability, then saves the order, items, and initial history event in a transaction. It calculates prices from the menu in SQL Server.

The generated `PlaceOrderAsync` wrapper's task result is not the new order ID. Read the supplied `OutputParameter<int?>` after awaiting the call, then load the saved order and computed receipt fields.

`dbo.UpdateOrderStatus` accepts customer ID, order ID, and new status ID. It locks the matching order and updates both state and history transactionally. Allowed transitions are:

| From | Allowed next state |
|---|---|
| 10 — Order placed | 20 — Preparing, or 90 — Cancelled |
| 20 — Preparing | 30 — Out for delivery |
| 30 — Out for delivery | 40 — Delivered |
| 40 — Delivered / 90 — Cancelled | None |

Use these procedures for order writes. Normal EF operations can handle profile and address edits. Validate input in the application for helpful feedback as well as retaining database checks. Map expected SQL errors 51001–51006 to clear messages; keep unexpected failures diagnosable. A lost response after commit is an uncertain outcome: inspect saved state before retrying placement.

Existing foreign keys preserve references used by orders; deleting referenced customers, addresses, or menu items is not a supported customer workflow. Do not add cascading deletes to make a form operation succeed.

## Schema and scaffolding changes

For a future schema change, inspect existing development data first, prepare a nondestructive SQL change when preservation is needed, and update the clean-create script to match. Explicitly label reset/reseed procedures; do not silently recreate a working database.

Apply and verify the change in the intended development database. Use EF Core Power Tools with the existing `FoodHub.Core/efpt.config.json` to regenerate affected tables and procedures. Review the generated diff, especially computed columns, composite address ownership keys, nullability, table-valued parameters, and output parameters. Update services, documentation, and integration checks together.

Generated files are regeneration-owned. Preserve headers and keep authored behavior or explanations in separate services/partial files. The current scaffolding settings include connection-string generation; changing that setting and the context configuration belongs to the configuration milestone.

## Verification boundary

This guide describes the checked-in script and generated code. It does not certify the live database schema, connectivity, or procedure execution. Integration tests must use SQL Server in a separate disposable test database, since computed columns, table-valued parameters, procedures, and locking behavior cannot be established with EF's in-memory provider.
