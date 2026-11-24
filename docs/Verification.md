# Verification record — 2026-11-24

## Builds and automated checks

- Full solution x64 Debug build: passed with zero warnings/errors, using explicit restore and disabled build servers in this environment.
- Unpackaged x64 build with `WindowsPackageType=None` and `WindowsAppSDKSelfContained=true`: passed with zero warnings/errors and launched successfully.
- `FoodHub.Tests`: 38 logic/SQL Server assertions passed. These cover input limits, exact password handling, salt/hash verification, rehash-on-login, opaque-token issuance, fixed expiry, migration snapshot consistency, original SQL object retention, fresh setup, baseline comparison, legacy customer/order preservation, duplicate-email races, no partial registrations, address ownership/referenced-address protection, receipt snapshots, order-status rules, restoration and revocation.
- `FoodHub.Windows.Tests`: seven assertions passed using the actual Windows data-protection storage class in an isolated temporary directory. Verified no plaintext token on disk, restoration through a new storage instance, memory-only/sign-out removal, corrupt/oversized-file rejection, and pending-write/sign-out serialization.
- Integration databases use generated FoodHub_Test names and are removed by the runner; application data is never the integration target.

Reproduce the core suite:

```powershell
$env:FOODHUB_TEST_CONNECTION_STRING = 'Server=localhost;Database=master;Integrated Security=True;TrustServerCertificate=True'
dotnet run --project FoodHub.Tests
dotnet run --project FoodHub.Windows.Tests
```

Omitting the test connection runs logic/model/script checks and explicitly skips SQL integration. The test-server identity needs CREATE DATABASE permission. These are executable assertion suites rather than a test-framework package; failures return nonzero.

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

## Remaining manual acceptance scenarios

These have implementation and lower-layer coverage where applicable, but were not claimed as a complete GUI run:

- Register a disposable customer through the GUI, resume a protected destination, edit contact details/addresses/photo, sign out, and confirm protected history is unavailable.
- Restart the actual app with remembered sign-in checked/unchecked; the storage restart check above uses a new storage instance rather than a full GUI restart with credentials.
- Exercise valid/invalid/oversized/rotated images through the native picker, cancellation, missing/corrupt stored-photo fallback, and referenced-address deletion feedback.
- Simulate database outage during restoration/profile editing, retry deliberately, and expire/revoke a token while Profile remains open.
- Check System/Light/Dark persistence, high text scaling, small window sizes, keyboard-only navigation, screen-reader labels, duplicate submission and closing a window during pending work.

## Local graph and maintenance

Local code-only Graphify extraction was refreshed without cloud calls or semantic document/image extraction. The final refreshed graph has 676 nodes/1230 edges. Extraction warned that top-level database-tool and Windows-test programs and the standalone demo seed yielded no symbols; those files were inspected directly. Graph output remains ignored. CodeGraph was unavailable and was not installed.

The existing user change in `.vscode/settings.json` was left untouched. Historical scaffold provenance and template teaching comments were preserved. The old embedded-connection `#warning` was removed because context configuration now comes from external options/environment; EF Power Tools configuration was archived as the user-selected migration workflow replaced regeneration.
