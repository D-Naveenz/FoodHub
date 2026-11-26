# FoodHub

Current version: **v0.2.0**. See [Changelog](CHANGELOG.md) for this milestone.

An educational Foodhub-inspired WinUI 3 customer application backed by SQL Server. The intended journey is account/profile -> menu -> basket -> delivery checkout -> simulated tracking and order history. This is not an official Foodhub app or an integration with its services.

## Current implementation

- WinUI dashboard with Home, Menu, Basket, Orders, About, Settings and a clickable guest/account avatar.
- Separate owned modal login/registration window, hashed passwords and opaque expiring local sessions; optional seven-day Windows-protected persistence.
- Editable account/customer names, phone, saved delivery addresses and profile photos. Email is read-only.
- Photo-led menu with category/search/sort, staged quantity editing, shared in-memory basket, owned delivery checkout, saved receipts, order history and explicitly simulated tracking/cancellation.
- Toolkit Labs TokenView and TransitionHelper are pinned experimental UI dependencies; WinUI ItemsView virtualizes the menu. Bundled fictional food illustrations work offline.
- Reusable Dhara WinUI hosting/helpers, Core services and EF Core migrations; original SQL order procedures and receipt snapshots are retained.

## Start here

| Document | Purpose |
|---|---|
| [Agent instructions](AGENTS.md) | Educational coding and local exploration rules |
| [Project scope](docs/Foodhub_Project_Scope.md) | Agreed features and acceptance criteria |
| [Architecture](docs/Architecture.md) | UI/service boundaries and account lifecycle |
| [Dhara WinUI library](Dhara/Dhara.Extensions.WinUI/README.md) | Reusable hosting, window lifecycle, dispatcher and Windows helpers |
| [Database](docs/Database.md) | Configuration, migrations and explicit development reset |
| [Accounts](docs/Accounts.md) | Validation, ownership and session rules |
| [Roadmap](docs/Development_Roadmap.md) | Implemented workflows and remaining verification |
| [Verification](docs/Verification.md) | Actual checks and remaining manual scenarios |
| [Local Graphify](docs/Graphify.md) | Local graph/MCP workflow |
| [Foodhub research](docs/Foodhub_Research.md) | Domain inspiration and simplifications |

## Build and launch

Use Windows, a compatible .NET SDK, Windows SDK, and Visual Studio WinUI/MSIX tooling. The app, Core and Dhara target net10.0-windows10.0.26100.0; native settings make these Windows-only. EF SQL Server/design tools are 10.0.15. MVVM uses CommunityToolkit.Mvvm 8.4.2; password hashing uses Microsoft.Extensions.Identity.Core 10.0.0. Dhara.Extensions.WinUI supplies reusable hosting in Dhara.Extensions.WinUI.Hosting; Core and the app reference Dhara for shared configuration and storage. Toolkit Labs TokenView/TransitionHelper are pinned to 0.1.251217-build.2433; Windows packages retain the existing floating references; the inspected restore resolves Windows App SDK 2.7.0.

Debug builds load the local Windows-authentication connection from `appsettings.Development.json`; no shell environment variable is required. Release selects Production with an empty connection; custom build configurations select their own environment names. Environment selection is automatic from the consuming assembly’s build metadata; DOTNET_ENVIRONMENT is ignored. Use **Settings → Database** to test and save a Windows-authentication connection, then restart. Use PowerShell 7.4+ `./scripts/Invoke-Database.ps1 Migrate` or explicit `Reset`; see the [database guide](docs/Database.md) for previews, SQL export and parameters. Local FoodHub_Dev data is disposable; reset drops and recreates it without a backup. Guest navigation and Settings remain available when configuration or SQL is unavailable.

```powershell
dotnet restore FoodHub.slnx -p:Platform=x64
dotnet build FoodHub.slnx -p:Platform=x64 --no-restore
```

Open FoodHub.slnx and launch the packaged profile. The app and test projects default `WindowsAppSDKSelfContained` to true, so ordinary builds include the matching native Windows App SDK without requiring a globally installed version. This setting is separate from .NET `SelfContained`. For an unpackaged build:

```powershell
dotnet build FoodHub/FoodHub.csproj -p:Platform=x64 -p:WindowsPackageType=None
& './FoodHub/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/FoodHub.exe'
```

The explicit Platform determines runtime architecture even with an ARM64 SDK. Bundled appsettings files belong to the FoodHub executable and are read-only at runtime. Native Windows LocalSettings holds appearance and database overrides; database changes require restart while appearance applies immediately. Legacy local JSON and theme.txt are ignored without migration. See [hosting and settings](docs/Architecture.md#desktop-hosting-and-application-settings). Files live in the native package LocalPath when packaged, or `%LOCALAPPDATA%/dashe/FoodHub` when unpackaged; LocalSettings is a separate Windows-managed store, not a file in that folder; no password or private profile cache is stored there. In Development, Settings shows the active path and opens it in File Explorer.

## Verification

`FoodHub.WinUI.Test` is the WinUI MSTest application and is included in the solution. It includes configuration/storage/developer-tooling tests, native hosted-window lifecycle checks, and SQL integration scenarios for accounts and ordering. See docs/Verification.md for the latest actual counts and deployment-specific skips. The template's sample assertions and the old console runners were removed after their meaningful coverage was migrated.

For the template's packaged path, build x64 and run tests from Visual Studio Test Explorer. Set `FOODHUB_TEST_CONNECTION_STRING` before launching Visual Studio if SQL integration should run. Tests that create/access XAML objects should use `[UITestMethod]`; the app publishes its dispatcher queue for that purpose.

The self-contained unpackaged CLI path runs the same tests with Microsoft.Testing.Platform:

```powershell
dotnet restore FoodHub.WinUI.Test/FoodHub.WinUI.Test.csproj -p:Platform=x64 -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true
dotnet build FoodHub.WinUI.Test/FoodHub.WinUI.Test.csproj -p:Platform=x64 -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true --no-restore
dotnet run --project FoodHub.WinUI.Test/FoodHub.WinUI.Test.csproj --no-build -p:Platform=x64 -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -- --progress off
```

Optionally set a dedicated SQL Server test identity before running:

```powershell
$env:FOODHUB_TEST_CONNECTION_STRING = 'Server=localhost;Database=master;Integrated Security=True;TrustServerCertificate=True'
```

Without this variable, the SQL cases are skipped. Unpackaged runs also skip the check that requires actual package identity. A configured but unreachable server fails those tests. SQL tests create uniquely named FoodHub_Test databases and remove only those databases afterward; the identity needs CREATE DATABASE permission. Windows-storage tests use isolated temporary directories and uniquely named native settings containers and never the customer's saved session. Categories `Unit`, `WindowsStorage`, and `SqlIntegration` allow filtering in Test Explorer. Do not substitute EF InMemory for SQL Server procedure/constraint verification.

The command-line host closes its test window and returns the runner's exit code. The packaged path retains the template's VSTest host; the unpackaged path uses MTP because [VSTest's WinUI provider requires a package manifest](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-mstest-winui#configure-an-unpackaged-app).

Preserve comments/provenance, document authored APIs and update repository guidance with behavior changes. Graphify remains local-only, with ignored generated output and no cloud extraction.

### Windows App SDK activation failures

`REGDB_E_CLASSNOTREG` in `DeploymentInitializeOptions` or a Windows App Runtime module initializer happens before application/host/database startup. Normal builds now include the native Windows App SDK. If upgrading an older build, stop the debug session, clean/rebuild the application, and let Visual Studio redeploy the packaged profile. An explicit `-p:WindowsAppSDKSelfContained=false` opts back into framework-dependent deployment and requires the matching installed Windows App Runtime. Do not disable initialization to hide a missing runtime.

## Trying the ordering journey

Apply migrations explicitly, then deliberately execute `docs/Foodhub_Demo_Seed.sql` against your development database if menu data is wanted. The script is optional, inserts fictional demonstration rows, and is not idempotent. The app never seeds or resets a database automatically. A newly migrated database has an empty menu until data is supplied. Bundled menu paths use `Assets/Menu/*.svg`; missing/invalid images use the shared fallback.

Guests can browse and prepare a basket. Checkout opens the existing sign-in window and resumes after success; save an address through Profile first. Review checkout and confirm Cash on delivery. Orders opens saved receipts and timestamped tracking; labeled demo buttons advance only the next permitted stage, and cancellation is available only at Order placed. Basket contents survive guest sign-in but clear on sign-out, expiry, account replacement or successful placement, and are not saved across app restarts.

A confirmed write retains its order ID until its receipt is shown. If receipt loading fails, retry the receipt rather than place another order. If placement has an uncertain transport outcome, refresh Orders and inspect history; only an explicit acknowledgement allows another attempt, which can create another order. The existing SQL contract does not provide an idempotency key.
