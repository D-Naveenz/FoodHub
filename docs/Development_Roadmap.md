# Development roadmap

## Implemented account and shell slice

The app now has the navigation shell and Home, separate modal login/registration, account/customer services, opaque sessions, editable profile/contact details, photo selection, saved addresses, appearance settings, Generic Host dependency injection and persistent database setup in Settings. EF migrations replace SQL-first schema ownership. Menu, Basket, Orders and subordinate customer pages are skeletons.

Build, SQL integration and manual UI results are recorded separately in [Verification](Verification.md). A source file or graph node is never evidence that a GUI journey was exercised.

## Remaining milestones

| Milestone | Work | Observable completion |
|---|---|---|
| Menu and basket | Category/search/photo-led browsing, availability, in-memory quantities and totals | Guest or signed-in user can find food, add/remove items and see accurate totals |
| Checkout and confirmation | Require a valid session, select owned address, review contact/total, call PlaceOrder and reload receipt | SQL saves one complete order and UI shows its unique order number; invalid/empty/unavailable/wrong-owner requests fail without partial orders |
| Tracking and cancellation | Timeline, current status and clearly labeled demo progression/cancellation | Allowed transitions persist; delivered/cancelled are terminal; competition does not duplicate events |
| History and polish | Authenticated customer history and full saved receipts; complete accessibility/error/restart checks | Receipt snapshots survive restart and later profile/menu edits; private state stays scoped to the account |

Keep basket state in memory provisionally and clear/reconcile it at account changes. Do not automatically retry uncertain order placement. Preserve stored procedures, snapshots, UTC timestamps and text status labels. No staff administration, dispatch, GPS, online payments or Foodhub APIs are required.

## Account follow-up verification

Exercise the complete GUI account journey, remembered-token restart, image picker validation, database-outage retry, high text scaling and screen-reader behavior using a disposable account database. Review [Accounts](Accounts.md) for the implemented rules. Password recovery/change and email changes require a later explicit slice.

The implemented infrastructure now keeps generated startup, constructor-built host composition, a hosted main-window lifecycle, package-aware local storage, Development-only Explorer access, and one initial EF migration with explicit disposable-development reset. These changes do not implement deferred ordering pages.

The desktop host now uses a custom Core builder, automatic build-profile environments and a shared JSON settings writer. Appearance applies immediately; database configuration remains restart-only. Legacy settings imports are removed. See Architecture for the public builder API and configuration ownership.
