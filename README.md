# FoodHub

Current version: **v0.1.0**. See [Changelog](CHANGELOG.md) for this milestone.

An educational Foodhub-inspired WinUI 3 customer application backed by SQL Server. The intended journey is account/profile -> menu -> basket -> delivery checkout -> simulated tracking and order history. This is not an official Foodhub app or an integration with its services.

## Current implementation

- WinUI dashboard with Home, Menu, Basket, Orders, About, Settings and a clickable guest/account avatar.
- Separate owned modal login/registration window, hashed passwords and opaque expiring local sessions; optional seven-day Windows-protected persistence.
- Editable account/customer names, phone, saved delivery addresses and profile photos. Email is read-only.
- Menu, basket, checkout, confirmation and order/tracking pages are navigation skeletons. Ordering workflows remain deferred.
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
| [Roadmap](docs/Development_Roadmap.md) | Implemented slice and deferred customer workflows |
| [Verification](docs/Verification.md) | Actual checks and remaining manual scenarios |
| [Local Graphify](docs/Graphify.md) | Local graph/MCP workflow |
| [Foodhub research](docs/Foodhub_Research.md) | Domain inspiration and simplifications |

## Build and launch

Use Windows, a compatible .NET SDK, Windows SDK, and Visual Studio WinUI/MSIX tooling. UI targets net10.0-windows10.0.26100.0; Core targets net10.0. EF SQL Server/design tools are 10.0.15. MVVM uses CommunityToolkit.Mvvm 8.4.2; password hashing uses Microsoft.Extensions.Identity.Core 10.0.0. Dhara.Extensions.WinUI supplies reusable hosting in Dhara.Extensions.WinUI.Hosting; the app references it separately from Core. Windows packages retain the existing floating references; the inspected restore resolves Windows App SDK 2.7.0.

Debug builds load the local Windows-authentication connection from `appsettings.Development.json`; no shell environment variable is required. Release selects Production with an empty connection; custom build configurations select their own environment names. Environment selection is automatic from the consuming assembly’s build metadata; DOTNET_ENVIRONMENT is ignored. Use **Settings → Database** to test and save a Windows-authentication connection, then restart. Follow the [database guide](docs/Database.md) for explicit migrate/reset commands. Local FoodHub_Dev data is disposable; reset drops and recreates it without a backup. Guest navigation and Settings remain available when configuration or SQL is unavailable.

```powershell
dotnet restore FoodHub.slnx -p:Platform=x64
dotnet build FoodHub.slnx -p:Platform=x64 --no-restore
```

Open FoodHub.slnx and launch the packaged profile. Both app and test projects default `WindowsAppSDKSelfContained` to true, so ordinary builds include the matching native Windows App SDK without requiring a globally installed version. This setting is separate from .NET `SelfContained`. For an unpackaged build:

```powershell
dotnet build FoodHub/FoodHub.csproj -p:Platform=x64 -p:WindowsPackageType=None
& './FoodHub/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/FoodHub.exe'
```

The explicit Platform determines runtime architecture even with an ARM64 SDK. General settings, including appearance and database overrides, share local appsettings.json; database changes require restart while appearance applies immediately. Old theme.txt files are ignored without migration. See [hosting and settings](docs/Architecture.md#desktop-hosting-and-application-settings). App data lives in package `ApplicationData.Current.LocalFolder` when packaged, or `%LOCALAPPDATA%/FoodHub` when unpackaged; no password or private profile cache is stored there. In Development, Settings shows the active path and opens it in File Explorer.

## Verification

`FoodHub.WinUI.Test` is the WinUI MSTest application and is included in the solution. It includes configuration/storage/developer-tooling tests, native hosted-window lifecycle checks, and three SQL integration scenarios. See docs/Verification.md for the latest actual counts and deployment-specific skips. The template's sample assertions and the old console runners were removed after their meaningful coverage was migrated.

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

Without this variable, only the three SQL cases are reported as skipped. A configured but unreachable server fails those tests. SQL tests create uniquely named FoodHub_Test databases and remove only those databases afterward; the identity needs CREATE DATABASE permission. Windows-storage tests use isolated temporary directories and never the customer's saved session. Categories `Unit`, `WindowsStorage`, and `SqlIntegration` allow filtering in Test Explorer. Do not substitute EF InMemory for SQL Server procedure/constraint verification.

The command-line host closes its test window and returns the runner's exit code. The packaged path retains the template's VSTest host; the unpackaged path uses MTP because [VSTest's WinUI provider requires a package manifest](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-mstest-winui#configure-an-unpackaged-app).

Preserve comments/provenance, document authored APIs and update repository guidance with behavior changes. Graphify remains local-only, with ignored generated output and no cloud extraction.

### Windows App SDK activation failures

`REGDB_E_CLASSNOTREG` in `DeploymentInitializeOptions` or a Windows App Runtime module initializer happens before application/host/database startup. Normal builds now include the native Windows App SDK. If upgrading an older build, stop the debug session, clean/rebuild the application, and let Visual Studio redeploy the packaged profile. An explicit `-p:WindowsAppSDKSelfContained=false` opts back into framework-dependent deployment and requires the matching installed Windows App Runtime. Do not disable initialization to hide a missing runtime.
