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
