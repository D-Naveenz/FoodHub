# Architecture and learning guide

Status: inspected baseline on 2026-11-24. Recommendations below are provisional pending further implementation guidance.

## What exists

`FoodHub` is the WinUI 3 executable. `App` creates and activates `MainWindow`. The window hosts a title bar, `NavigationView`, and `Frame`; its selection handler navigates to Home, About, or Settings. These are template pages rather than working ordering features.

`FoodHub.Core` is a .NET library referenced by the UI. Its `Models` folder contains generated EF entities, `FoodHub_DevContext`, procedure wrappers, the basket table-valued parameter shape, and generated helper types. Despite the name, Core currently combines data access and model responsibilities; there is no separate data project or application service layer.

The SQL script defines the intended persistence rules. It is a schema creation and seed script, not a migration history or proof that a running database matches it. No feature tests are currently present in the inspected repository.

## Proposed flow

Use this simple flow when implementing features:

```text
WinUI page → view model → application service → EF Core / stored procedure → SQL Server
          ← observable state / user-facing result ← saved data / validation outcome
```

Pages own visual behavior and navigation. View models expose screen state and commands. Application services coordinate use cases and translate data-layer failures into useful outcomes. SQL Server remains authoritative for checkout prices, order creation, and valid status transitions.

Start with authored `ViewModels` in the UI project and authored `Services` in Core. Keep generated models separate from those services. Add interfaces only where they establish a useful boundary or support meaningful tests. A third project, generic repository abstraction, messaging framework, or dependency injection package is not required by this baseline.

Choose any MVVM package and dependency composition approach during the first implementation slice, after considering the user's further guidance. This documentation task does not install packages or finalize method signatures.

## Data and UI responsibilities

- Use a short-lived context for each operation and async calls to avoid freezing the UI. Do not run parallel operations on one context or block tasks with `.Result`/`.Wait()`.
- Keep a basket in application memory provisionally. Combine repeated items into one quantity; do not promise basket persistence across restarts. Clear or explicitly reconcile it when switching customer profiles so one customer's checkout is not carried into another's unnoticed.
- Read menu data and preview totals for display, then let `PlaceOrder` determine the persisted price and fee. Reload the saved receipt after successful placement. Handle changed availability or prices visibly; never imply an unsaved basket is a confirmed order.
- Scope order reads and writes to the selected customer. Local profile selection is convenient for teaching, but it provides no security boundary between real users.
- Keep timestamps in UTC in storage and convert them for display. Sort history events by their history ID, since seed events can have equal timestamps.
- Expose loading, empty, validation, and database failure states. Disable duplicate submissions while an operation is pending. Keep retry behavior deliberate: an uncertain checkout response must not automatically create another order.
- Show statuses with text as well as color, support keyboard navigation, and use clearly labeled demo controls. Use WinUI/XAML patterns; this is not a React or shadcn project.

## Known gaps and later decisions

The context embeds a local integrated-authentication connection string and configures SQL Server unconditionally in `OnConfiguring`. A later configuration task must make externally supplied options effective rather than merely adding another connection string elsewhere. Keep credentials outside tracked files.

The UI references Windows packages with `*`; record resolved versions before considering an update or reproducibility change. Do not describe them as a verified latest WinUI release. Menu seed paths reference photos that are not currently supplied; add local photos or an accessible placeholder during menu work.

No GUI, connection, build, or database runtime behavior was validated as part of writing these architecture notes. Follow the [roadmap](Development_Roadmap.md) for feature verification and the [database guide](Database.md) for schema changes.
