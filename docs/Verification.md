# Verification record — 2026-11-24

## Builds and automated checks

- Full solution x64 Debug build: passed with zero warnings/errors, using explicit restore and disabled build servers in this environment.
- Unpackaged x64 build with `WindowsPackageType=None` and `WindowsAppSDKSelfContained=true`: passed with zero warnings/errors and launched successfully.
- Before migration to MSTest, the original runners passed 38 account/model/SQL assertions and seven protected-storage assertions. Their meaningful coverage now lives in `FoodHub.WinUI.Test`; the obsolete console projects and template equality/default-Grid samples were removed.
- The migrated application contains 25 discoverable MSTest cases: 15 account-rule/model/script tests, seven Windows-storage tests, and three end-to-end SQL scenarios (fresh accounts/order rules, legacy upgrade preservation, and schema drift).
- With the local SQL test connection configured: all 25 migrated MSTest cases passed, with zero skipped or failed. The unpackaged self-contained x64 test-app build passed with zero warnings/errors.
- Without a test connection: 22 passed, three SQL tests skipped, zero failures. The unconfigured cases use Assert.Inconclusive, so skips are visible in runner/Test Explorer results.
- SQL cases retain disposable database isolation and cleanup, including the deliberate concurrent-registration race. Protected-storage tests retain isolated directories, exact file cleanup, encryption/restoration/corruption checks, and pending-write/sign-out ordering.

Use the restore/build/run commands in [README](../README.md#verification). Optionally set `FOODHUB_TEST_CONNECTION_STRING` to a dedicated test-server identity with CREATE DATABASE permission. A configured connection failure fails the SQL tests rather than hiding it as a skip.

The packaged startup retains the template's Visual Studio Test Explorer integration and publishes the WinUI dispatcher for UITestMethod; packaged execution was not independently exercised in this migration. For reproducible CLI verification, the same application can be built unpackaged/self-contained and hosts Microsoft.Testing.Platform in-process; it publishes the runner exit code and closes the window on completion. The current migrated cases do not require the XAML UI thread; setting up the dispatcher preserves support for future actual UI-component tests.

## Development database upgrade

The initial sandboxed SQL attempt failed. A read-only retry outside the sandbox reached localhost/FoodHub_Dev and found two customers and five orders.

A separate `FoodHub_Baseline_20261124_Accounts` reference database was created with Baseline only. Metadata comparison initially rejected source line-ending/trailing-whitespace differences in the procedures; normalizing only those presentation differences produced an exact match. Baseline adoption then completed only after COPY_ONLY/CHECKSUM backup and RESTORE VERIFYONLY succeeded.

Backup retained on the SQL Server machine:

```text
C:/Program Files/Microsoft SQL Server/MSSQL17.MSSQLSERVER/MSSQL/Backup/FoodHub_Dev_before_accounts_20261124.bak
```

Applied migrations: `20261124065707_Baseline`, `20261124065714_Accounts`. Post-upgrade SQL checks confirmed two customers, five orders, zero linked legacy accounts, and zero new account rows. No demo data was reset or reseeded. The separate baseline reference is retained for inspection.

## Manual WinUI observations

Using the self-contained executable, observed the branded Home screen, supplied guest image, expanded and compact navigation, guest Profile page, protected Orders redirect, separate login/registration window, required registration fields, scrollable registration form, native disabled owner while modal, and owner re-enablement after closing the account window. Form layout and navigation selection were refined from these visual checks.

Direct execution of the framework-dependent executable without package deployment failed before application code with Windows App SDK runtime activation error REGDB_E_CLASSNOTREG. The documented self-contained launch passed. Packaged deployment was not independently verified.

## Account-window presentation refinement — 2026-11-24

- Reduced initial height from 680 to 600 effective pixels and retained the existing banner colors, logo sizing and promotional-image margins.
- Replaced the combined show/hide form with separate login/registration pages hosted by a Frame using native horizontal slide transitions. Replaced the secondary account button with a click-handled hyperlink footer and removed the Cancel button.
- The x64 unpackaged self-contained application build passed with zero warnings/errors. Manual execution checked both hyperlink routes, the registration scrollbar and bottom submit/return link, the absence of Cancel, initial focus, and owner re-enablement after title-bar close. Password clearing and delegate detachment were inspected in source; no credential submission or database/schema changes were part of this refinement.

## Navigation profile-name timing — 2026-11-24

The navigation label previously changed visibility in PaneOpened/PaneClosed handlers, so expanding the pane waited for animation completion before showing the name. It now follows changes to NavigationView.IsPaneOpen immediately, initializes visibility during window setup, and unregisters the property callback on close. The x64 self-contained application build passed with zero warnings/errors. Manual collapse/expand checks confirmed the avatar remains visible in compact mode and the Guest label is already visible while expansion is still animating. Session restoration and authenticated-name loading rules were not changed.

## Remaining manual acceptance scenarios

These have implementation and lower-layer coverage where applicable, but were not claimed as a complete GUI run:

- Register a disposable customer through the GUI, resume a protected destination, edit contact details/addresses/photo, sign out, and confirm protected history is unavailable.
- Restart the actual app with remembered sign-in checked/unchecked; the storage restart check above uses a new storage instance rather than a full GUI restart with credentials.
- Exercise valid/invalid/oversized/rotated images through the native picker, cancellation, missing/corrupt stored-photo fallback, and referenced-address deletion feedback.
- Simulate database outage during restoration/profile editing, retry deliberately, and expire/revoke a token while Profile remains open.
- Check System/Light/Dark persistence, high text scaling, small window sizes, keyboard-only navigation, screen-reader labels, duplicate submission and closing a window during pending work.

## Local graph and maintenance

Local code-only Graphify extraction was refreshed without cloud calls or semantic document/image extraction. After test migration, the refreshed graph has 787 nodes/1448 edges. The assembly-level test configuration yields no symbols, as expected; top-level database-tool code and the standalone demo seed also require direct inspection where graph coverage is incomplete. Graph output remains ignored. CodeGraph was unavailable and was not installed.

The existing user change in `.vscode/settings.json` was left untouched. Historical scaffold provenance and template teaching comments were preserved. The old embedded-connection `#warning` was removed because context configuration now comes from external options/environment; EF Power Tools configuration was archived as the user-selected migration workflow replaced regeneration.

## Generic Host and persistent database configuration — 2026-11-24

Implemented Microsoft.Extensions.Hosting 10.0.0, shared JSON/environment configuration, constructor-injected services/view models, next-launch database Settings, and Windows package-identity helpers. Database setup and authentication persistence are independent. The manual ApplicationServices composition facade was replaced; its API explanations now live on the host and factory. The old cancellation-source retention comment was replaced because continuations now capture a token and the source is disposed safely. Historical scaffold and copyright/template headers remain.

- Final x64 unpackaged test build: zero warnings/errors. All **43** tests passed, zero skipped/failed, with local SQL integration enabled: 32 unit/model/configuration cases, seven protected-storage cases, four SQL integration cases. New checks cover provider priority, explicit/build environment, arbitrary working directory, empty/malformed settings, explicit EF options, atomic next-launch saves, unrelated-JSON preservation, persistent token-reset retry, view-model pending fields, cancellation, bounded unreachable-server feedback, failed writes, safe configuration errors, package-identity helpers, and read-only migration checks. SQL cases create and clean up only generated FoodHub_Test databases.
- Unpackaged Debug app: observed Guest startup, Settings populated from Development JSON without FOODHUB_CONNECTION_STRING, disabled edits and available cancellation during a check, and successful SQL/migration feedback. Observed injected Guest Profile and clean host/window shutdown with exit code 0. UI-service resolution remains on the dispatcher; page/window state owns cancellation and subscriptions.
- Clean isolated untrimmed Release publish: launched from the temporary directory, observed Guest and accessible Settings with empty server/database fields, then clean shutdown with exit code 0. Production output contains only appsettings.json with an empty connection. No actual customer database connection or schema was changed.
- Release MSIX build succeeded; inspecting the x64 package confirmed only appsettings.json, with an empty connection, and no Development configuration. Packaging emits existing tooling/resource warnings: missing mspdbcmf.exe prevents symbol packaging, and PRI263 concerns neutral Microsoft.Data.SqlClient.resources.dll. These are recorded separately from compilation. The rebuilt Debug AppX layout was registered and launched with package identity; its Settings loaded Development values and Test Connection confirmed required account migrations.
- Administrative tool build passed; its restricted-network build reported NU1900 vulnerability-feed warnings. It uses the same configuration loader as the app and EF factory. No migration/adoption/reseed command was run against the development database for this change.

Release publishing initially exposed EF trimming warnings, so PublishTrimmed is now false. An old publish directory still contained trimmed runtime assemblies (CoreLib about 9 MB) after republishing; that stale directory crashed in the generated WinRT module initializer. A clean isolated untrimmed publish (CoreLib about 16 MB) passed startup/shutdown. Clean old publish directories when changing trimming/deployment settings. Asset copying and an install-relative icon path were added after the different-working-directory smoke check exposed missing published images. The final standard Release publish was regenerated into a clean output directory and confirmed startup with images and exit code 0.

Remaining manual acceptance: full GUI credential submission and remembered-account restart, Settings Save/Reset/restart with a disposable account, and accessibility/high-scale checks. Session encryption/restore/sign-out and durable database-change reset behavior have automated coverage; those checks are not a claim of a complete GUI login journey. The packaged-run tooling ignored the temporary-name override and unregistered the existing debug identity on exit. The local development registration was restored from the rebuilt AppX layout, then packaged Settings/SQL connectivity was verified directly without that runner. The restored registration remains available. No customer database data was changed.

Local code-only Graphify extraction was refreshed to 927 nodes/1846 edges without cloud/semantic extraction. CodeGraph remains unavailable and was not installed. Generated graph and build/test/publish artifacts remain ignored by Git.
