# Development roadmap

## Implemented account and shell slice

The app now has the navigation shell and Home, separate modal login/registration, account/customer services, opaque sessions, editable profile/contact details, photo selection, saved addresses, appearance settings, Generic Host dependency injection and persistent database setup in Settings. EF migrations replace SQL-first schema ownership. The ordering slice now implements menu/item details, basket, checkout, confirmation, owned receipts/history, and simulated tracking/cancellation.

Build, SQL integration and manual UI results are recorded separately in [Verification](Verification.md). A source file or graph node is never evidence that a GUI journey was exercised.

## Ordering slice and remaining verification

| Milestone | Work | Observable completion |
|---|---|---|
| Menu and basket (implemented) | Category/search/sort, bundled illustrations, availability, staged in-memory quantities and totals | Guest or signed-in user can find food, add/remove items and see accurate totals |
| Checkout and confirmation (implemented) | Require a valid session, select owned address, review contact/total, call PlaceOrder and reload receipt | SQL saves one complete order and UI shows its unique order number; invalid/empty/unavailable/wrong-owner requests fail without partial orders |
| Tracking and cancellation (implemented) | Timeline, current status and clearly labeled demo progression/cancellation | Allowed transitions persist; delivered/cancelled are terminal; competition does not duplicate events |
| History and polish (implemented; accessibility/restart verification remains) | Authenticated customer history and full saved receipts; complete accessibility/error/restart checks | Receipt snapshots survive restart and later profile/menu edits; private state stays scoped to the account |

Basket state is in memory: guest sign-in carries it forward, while sign-out, expiry and account replacement clear it. Do not automatically retry uncertain order placement. Preserve stored procedures, snapshots, UTC timestamps and text status labels. No staff administration, dispatch, GPS, online payments or Foodhub APIs are required.

## Account follow-up verification

Exercise the complete GUI account journey, remembered-token restart, image picker validation, database-outage retry, high text scaling and screen-reader behavior using a disposable account database. Review [Accounts](Accounts.md) for the implemented rules. Password recovery/change and email changes require a later explicit slice.

The implemented infrastructure now keeps generated startup, constructor-built host composition, a hosted main-window lifecycle, package-aware local storage, Development-only Explorer access, and one initial EF migration with explicit disposable-development reset. The ordering implementation builds on this infrastructure without changing its settings or migration ownership.

The desktop host uses Dhara’s WinUI builder, automatic build-profile environments, read-only executable-owned JSON defaults and a shared native LocalSettings writer. Appearance applies immediately; database configuration remains restart-only. Legacy settings imports are removed. See Architecture for the public builder API and configuration ownership.

Reusable Windows hosting and helpers now live in Dhara.Extensions.WinUI. FoodHub remains the application composition/domain layer; Core targets Windows and consumes Dhara without creating XAML UI. Database automation uses PowerShell plus EF CLI and a small Core design-time factory; the separate console project was removed. Dhara publication and background/tray operation are deferred.

Remaining polish: complete the manual account-to-checkout/restart journey, high text scaling/high contrast/screen-reader checks, disabled-animation checks, and fresh MSIX deployment. Track actual execution separately in Verification.md. There is no automatic seeding or uncertain-write retry.
