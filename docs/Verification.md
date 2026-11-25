# Verification record

## Read-only host configuration and native runtime settings — 2026-11-26

This section supersedes the configuration/storage ownership and paths in earlier entries. Dhara now owns the shared ApplicationConfiguration/ApplicationEnvironment, LocalSettingsStore, LocalStorageService and native ApplicationDataContext. FoodHub owns bundled appsettings source files; the database executable links them. Host JSON is read-only and separate from runtime preferences; no legacy files or settings are imported. Core and administration tools now target Windows through Dhara.

Checked:

- Final unpackaged x64 test build: zero warnings/errors. **93 passed, zero failed, four skipped (97 total)**. Skips: actual package LocalPath check and three SQL integration cases without FOODHUB_TEST_CONNECTION_STRING. No database migration/reset or SQL integration run was performed for this change.
- Tests cover host file precedence and immutability, build metadata/environment-variable isolation, local overrides outside IConfiguration, optional real file watching with immutable database snapshots, settings repair/access failures, token-reset retry sequencing, failed/cancelled writes, concurrent grouped updates and appearance state. Existing hosted-window/dispatcher/lifecycle tests continue to pass.
- Six native LocalSettings tests use uniquely named child containers, deleting only those test containers afterward. Values survive reopening; typed values/arrays round-trip, unrelated values survive updates/removal, cancellation preserves previous groups, invalid formats require explicit repair and unsupported group objects cannot overwrite committed data. No FoodHub RuntimeSettings values were modified by tests.
- Windows App SDK containers reject the older Windows.Storage.ApplicationDataCompositeValue. Related values therefore use a typed string envelope committed by a single native assignment; no JSON settings file is involved. Native quotas and per-process-only write coordination are documented in Dhara's guide.
- Debug database executable build and EF `dbcontext info --project FoodHub.Core --startup-project FoodHub.Database --no-build --configuration Debug` passed. EF resolved SQL Server/localhost/FoodHub_Dev without opening SQL or constructing XAML. A temporary direct-executable settings-only probe also passed and was removed. The factory scopes native manifest activation/resource lookup and restores EF's current directory/runtime environment variables afterward.
- App Debug (default packaged build path and unpackaged), Release and custom Staging x64 builds passed with zero warnings/errors. Output inspection found base plus Development JSON in Debug app/tool outputs and only base JSON in Release/Staging. This verifies compilation/output selection, not fresh MSIX deployment.
- Dhara generates XML documentation; builds report no documentation warnings. Existing comments and provenance headers are preserved. The old database-owned configuration/path classes and file-backed settings writer were replaced by the reusable shared implementations; FoodHub retains feature-specific fallback, theme, database and protected-session policy.

The local code-only Graphify graph was refreshed (1324 nodes/3032 edges) without cloud extraction; generated output remains ignored.

Remaining manual verification: fresh MSIX deployment and actual packaged native settings persistence, the complete account/customer journey, and database Save/Reset with remembered sign-in across real application restarts. Unpackaged native storage and generic window lifecycle tests do not claim these full journeys. No NuGet publication was performed.


## Dhara WinUI infrastructure extraction — 2026-11-25

This section supersedes earlier Core-owned host-builder/window-helper implementations. General WinUI hosting now lives in Dhara.Extensions.WinUI.Hosting, independently of FoodHub. Core retains portable configuration/storage and service registration; the full Hosting package is referenced by Dhara while Core references Hosting.Abstractions and Configuration.Json. No schema changes, settings migrations or NuGet publication were performed.

Checks performed:

- Independent Dhara Debug/Staging builds passed with XML documentation generation enabled and zero warnings/errors. The complete x64 solution build, including the default packaged app/test build path, passed with zero warnings/errors. FoodHub's unpackaged Debug, Release and Staging builds and the portable database-tool build also passed.
- Unpackaged x64 test run: **92 passed, zero failed, four skipped (96 total)**. The actual packaged LocalFolder test and three SQL integration tests were skipped because their prerequisites were not present. No SQL integration/reset command was executed for this extraction.
- Native lifecycle tests now use the real Dhara assembly, the existing test Application, and plain windows without a fake FoodHub.App or copied library implementations. They cover ordered/duplicate startup, real BackgroundService cancellation, worker-originated stop, programmatic Window.Close, native WM_CLOSE, secondary-window cleanup, cancelled/failed startup, close during startup, service-stop exceptions/timeouts, host disposal and final registry removal.
- Dispatcher tests cover worker-to-UI asynchronous callbacks, affinity after await, callback errors, cancellation before execution, cancellation while queued, and rejected work after a dedicated test queue shuts down. Generic dependency inspection confirms Dhara references no FoodHub assembly.
- Contract tests compare WinUI and portable tooling profile selection, JSON precedence and malformed/duplicate/root-shape failures. Application environment variables remain ignored. Existing persistence, immutable database snapshot, session reset, account rules and Windows-protected session tests remain in the suite.
- Native helper tests verify window owner HWND, input disable/re-enable, monitor-bounded sizing and WinRT picker owner initialization without displaying a picker. Appearance tests verify successful persistence updates both registered windows and preserves unrelated database JSON.
- EF design-time dbcontext info resolved the SQL Server context, localhost and FoodHub_Dev without opening SQL after Core's package references were reduced. App output inspection confirmed Development JSON in Debug and no Development JSON in Release.
- Manual unpackaged Debug run outside the filesystem sandbox: Guest startup and Settings worked, Development tools showed the normal LocalAppData/FoodHub root, Light applied immediately, and local JSON contained Appearance:Theme=Light. Restored the original System preference and closed the main window; the process exited with code 0.

Remaining manual checks: fresh packaged deployment/actual package LocalFolder behavior, the complete authentication-window/customer journey and shutdown while that account window is open, and unusual multi-monitor/DPI arrangements. Native generic ownership/secondary-window shutdown tests do not claim those complete application journeys. Existing package identity and storage paths are preserved; the library does not grant identity or own runtime deployment.

The old NativeWindows and window-host comments were relocated/replaced with documented generic responsibilities in Dhara. The placeholder Class1 and duplicate application implementations were removed. Existing scaffold/copyright comments remain. Library APIs have XML summaries, lifecycle/threading remarks, parameters, returns, exceptions and examples; the library README explains usage, ownership and deferred publication. Local code-only graph output is refreshed and remains ignored.

## Desktop hosting and unified settings — 2026-11-25

This section supersedes earlier environment-variable, theme.txt and legacy-import behavior below.

Implemented Core's DesktopApplicationBuilder/IHostApplicationBuilder adapter, build-metadata environment selection, a general JSON configuration loader and one shared atomic local-settings writer. Base JSON → environment JSON → local JSON is the application precedence. DOTNET_ENVIRONMENT and application-setting environment overrides are ignored. Appearance now uses Appearance:Theme in general settings, applies immediately after a successful save and ignores theme.txt. Database connections remain immutable until restart, with remembered-token reset sequencing retained. Legacy imports and migration-only tests were removed; their historical verification below is no longer current behavior. Existing scaffold/copyright headers remain; path/provider explanations were moved out of database code and expanded in XML documentation.

Checks performed:

- Core, database tool and Debug/Release/Staging x64 WinUI app builds passed with zero warnings/errors. Both unpackaged and default packaged Debug build configurations compiled; this is not a claim of a fresh MSIX deployment.
- EF design-time dbcontext info resolved the maintained SQL Server context, localhost and FoodHub_Dev from Debug defaults without opening SQL.
- Core XML documentation generation passed with no documentation/reference warnings (existing unrelated missing-member documentation warnings were suppressed with NoWarn=1591 for this check).
- Unpackaged x64 tests: **74 passed, zero failed, four skipped (78 total)**, including duplicate-key and invalid-root repair cases. Skips: actual packaged LocalFolder selection and three SQL integration tests requiring FOODHUB_TEST_CONNECTION_STRING. No SQL schema/data reset was performed for this refactor.
- Native-window tests verify launch appearance ordering, cancellation, close/host lifetime and immediate appearance application to main/owned windows after persistence. JSON tests cover independent consumer build metadata, missing metadata, three-layer precedence, ignored environment variables, DI validation/disposal, concurrent feature saves, unrelated-key preservation, malformed-file repair, failed writes and session-reset retry.
- Published the database tool through Debug → Release → Staging into the same artifacts/hosting-publish directory. Debug included base/Development JSON; Release and Staging included only base JSON. A reused Core output directory also removed Development JSON when switching Debug → Release. App output inspection matched these rules. No Staging JSON exists in the repository, so it correctly uses base defaults.
- Manual unpackaged Debug UI: observed Guest startup, Settings populated from Development JSON, Development tools and the normal LocalAppData/FoodHub path. Selecting Light changed the window immediately; local JSON contained Appearance:Theme=Light. Restored the original System preference and closed normally with exit code 0.
- The initial sandboxed manual run could not write its redirected app-data root; it showed safe save-failure feedback and closed with exit code 0. Repeating outside the filesystem sandbox verified normal persistence. Isolated native tests also verify failed persistence leaves the active theme unchanged.

Remaining manual checks: fresh packaged deployment/LocalFolder behavior, database Save/Reset with a remembered authenticated session, and complete account/customer journeys. Existing automated snapshot/reset-marker tests cover the refactored logic; SQL integration and these complete journeys were not rerun here. The local code-only graph was refreshed; generated output remains ignored.


## Current host, storage, and development tooling verification

Implemented constructor-built host composition with generated WinUI startup, MainWindowHostedService, package-aware ILocalStorageService, Core-owned JSON content, and Development-only local-folder access. The prior Baseline/Accounts migration chain, BaselineVerifier, backup/reference/adoption commands, and preservation-only tests were removed. The current schema starts with 20261124142837_InitialCreate and retains the maintained snapshot, SQL procedures, TVP, constraints, and required lookup rows.

- Final unpackaged x64 test build: zero warnings/errors. **65 tests passed, zero failed, one skipped (66 total)** with local SQL enabled. The skipped case specifically requires actual package identity; packaged LocalFolder selection was verified in the real app below. Coverage includes native hosted-window creation/appearance ordering, duplicate startup/stop, cancelled startup, worker-thread Stop marshalling, atomic file persistence, missing-file deletion, traversal rejection, cancelled writes, settings import conflicts/retry/no resurrection, fresh packaged authentication, developer-command environment gating/duplicate/failure handling, reset-target rejection, one-migration schema creation/recreation, accounts and order contracts.
- Ordinary Debug app/administration-tool builds passed with zero warnings/errors. No custom Main or DISABLE_XAML_GENERATED_MAIN was introduced. Existing Windows App SDK deployment defaults were retained.
- Authorized localhost/FoodHub_Dev reset completed without backups. SQL verification showed exactly one InitialCreate migration, zero users/customers/orders, five statuses, and one store-settings row. New registration is required after this intentional reset; no optional demonstration data was loaded.
- Unpackaged Development UI: Settings displayed C:/Users/dashe/AppData/Local/FoodHub; Open local storage folder opened precisely that location in Explorer. Closing the app completed host shutdown with exit code 0.
- Packaged Development UI: Settings displayed the package LocalState folder; Explorer opened that exact root. A settings-migrated.v1 marker and copied theme.txt were present, with no legacy token imported. The app started and closed successfully under its registered debug package identity.
- A Debug executable launched with DOTNET_ENVIRONMENT=Production showed empty database fields and omitted the entire Development/path/button section. This verifies runtime-environment gating independently of build configuration; it exited with code 0. Unit tests also guard direct command execution outside Development.
- Release MSIX generation succeeded. Inspecting the x64 package confirmed only appsettings.json, with an empty production connection and no Development JSON. Debug app, administration-tool, and test outputs all received both Core-owned JSON files without consumer declarations.
- Local code-only Graphify extraction refreshed to 1,032 nodes/2,160 edges; generated graph output remains ignored.
- Release packaging retains two SDK/tooling warnings: missing mspdbcmf.exe prevents symbol packaging; PRI263 concerns a neutral Microsoft.Data.SqlClient resource. No source compilation errors occurred.

The first packaged smoke check reached stale AppX binaries, causing the old deployment-manager activation failure. The ordinary build output correctly evaluated the existing self-contained runtime setting; hashes proved the registered layout's assembly was old. Staging the generated .appxrecipe payload into its existing debug layout made hashes match and the actual packaged UI passed. No deployment policy change or unregister-on-exit runner was used.

Historical scaffold/copyright/generated-file headers were preserved. ConstraintMapping explanations now describe the maintained model rather than the removed historical baseline. Tests use a minimal native window boundary to exercise the real hosted service without customer navigation; session encryption retains its existing binary format. SQL reset and storage setting migration are separate workflows.

Remaining manual acceptance: full GUI credential registration/login and remembered-account restart, settings Save/Reset with actual credentials, and high-text-scale/screen-reader scenarios. Automated service/SQL/storage tests do not claim those full GUI journeys.

Earlier sections record prior checks and are superseded where behavior changed above.

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

## Development database reset

The old backup/adoption verification record is superseded by the approved disposable-development workflow below. Existing archived backup/reference artifacts were left alone; no current code depends on them.

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

Local code-only Graphify extraction was refreshed without cloud calls or semantic document/image extraction. After test migration, the refreshed graph has 787 nodes/1448 edges. The assembly-level test configuration yields no symbols, as expected; top-level database-tool code and the standalone demo seed also require direct inspection where graph coverage is incomplete. Graph output remains ignored.

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

Local code-only Graphify extraction was refreshed to 927 nodes/1846 edges without cloud/semantic extraction. Generated graph and build/test/publish artifacts remain ignored by Git.

## Default Windows App SDK deployment fix — 2026-11-24

A normal Visual Studio build could still fail in the Windows App SDK module initializer with REGDB_E_CLASSNOTREG while constructing DeploymentInitializeOptions, before App/Generic Host/database code. Earlier successful launch checks explicitly supplied WindowsAppSDKSelfContained=true; that command-line setting had not become the project default.

Both FoodHub and FoodHub.WinUI.Test now default WindowsAppSDKSelfContained to true, while retaining an explicit false override for intentionally framework-dependent deployment. The SDK includes its matching native runtime and selects registration-free activation; the project does not manually suppress a required initializer. This is independent of .NET SelfContained.

- Ordinary x64 application build, with no WindowsAppSDKSelfContained override: passed, zero warnings/errors.
- Evaluated project properties: WindowsAppSDKSelfContained=true, WindowsAppSdkUndockedRegFreeWinRTInitialize=true, and WindowsAppSdkDeploymentManagerInitialize unset.
- Launched that ordinary-build executable from the temporary directory: observed Home and clean exit code 0, with no activation exception.
- Unpackaged test-app build using only Platform=x64 and WindowsPackageType=None: passed, zero warnings/errors. All 43 tests passed with local SQL enabled and no WindowsAppSDKSelfContained command-line override; zero failures/skips.

README now explains the default, the distinction from the .NET runtime, and clean/rebuild/redeploy steps for older output. No runtime installation, package registration change, database change, or exception-swallowing workaround was used for this fix. Local Graphify output was refreshed and remains ignored.
