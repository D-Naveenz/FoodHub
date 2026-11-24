# FoodHub

An educational Foodhub-inspired WinUI 3 customer application backed by SQL Server. The intended journey is account/profile -> menu -> basket -> delivery checkout -> simulated tracking and order history. This is not an official Foodhub app or an integration with its services.

## Current implementation

- WinUI dashboard with Home, Menu, Basket, Orders, About, Settings and a clickable guest/account avatar.
- Separate owned modal login/registration window, hashed passwords and opaque expiring local sessions; optional seven-day Windows-protected persistence.
- Editable account/customer names, phone, saved delivery addresses and profile photos. Email is read-only.
- Menu, basket, checkout, confirmation and order/tracking pages are navigation skeletons. Ordering workflows remain deferred.
- Core services and EF Core migrations; original SQL order procedures and receipt snapshots are retained.

## Start here

| Document | Purpose |
|---|---|
| [Agent instructions](AGENTS.md) | Educational coding and local exploration rules |
| [Project scope](docs/Foodhub_Project_Scope.md) | Agreed features and acceptance criteria |
| [Architecture](docs/Architecture.md) | UI/service boundaries and account lifecycle |
| [Database](docs/Database.md) | Configuration, fresh setup and nondestructive legacy upgrade |
| [Accounts](docs/Accounts.md) | Validation, ownership and session rules |
| [Roadmap](docs/Development_Roadmap.md) | Implemented slice and deferred customer workflows |
| [Verification](docs/Verification.md) | Actual checks and remaining manual scenarios |
| [Local Graphify](docs/Graphify.md) | Local graph/MCP workflow |
| [Foodhub research](docs/Foodhub_Research.md) | Domain inspiration and simplifications |

## Build and launch

Use Windows, a compatible .NET SDK, Windows SDK, and Visual Studio WinUI/MSIX tooling. UI targets net10.0-windows10.0.26100.0; Core targets net10.0. EF SQL Server/design tools are 10.0.15. MVVM uses CommunityToolkit.Mvvm 8.4.0; password hashing uses Microsoft.Extensions.Identity.Core 10.0.0. Windows packages retain the existing floating references; the inspected restore resolves Windows App SDK 2.7.0.

Set `FOODHUB_CONNECTION_STRING` before launching Visual Studio or the application, and follow the database guide to provision/migrate the database. Missing/unreachable configuration produces account feedback; guest navigation still works.

```powershell
$env:FOODHUB_CONNECTION_STRING = 'Server=localhost;Database=FoodHub_Dev;Integrated Security=True;TrustServerCertificate=True'
dotnet restore FoodHub.slnx -p:Platform=x64
dotnet build FoodHub.slnx -p:Platform=x64 --no-restore
```

Open FoodHub.slnx and launch the packaged profile with a matching installed Windows App SDK runtime. For an unpackaged build that includes the Windows App SDK locally:

```powershell
dotnet build FoodHub/FoodHub.csproj -p:Platform=x64 -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true
& './FoodHub/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/FoodHub.exe'
```

The explicit Platform determines runtime architecture even with an ARM64 SDK. App data lives under `%LOCALAPPDATA%/FoodHub`; no password or private profile cache is stored there.

## Verification

```powershell
dotnet run --project FoodHub.Tests
dotnet run --project FoodHub.Windows.Tests
# Optional: a dedicated test SQL Server identity with CREATE DATABASE permission.
$env:FOODHUB_TEST_CONNECTION_STRING = 'Server=localhost;Database=master;Integrated Security=True;TrustServerCertificate=True'
dotnet run --project FoodHub.Tests
```

The dependency-free executable test suite returns nonzero on assertion failure. SQL integration creates uniquely named FoodHub_Test databases and removes only those databases afterward. Do not substitute EF InMemory for SQL Server procedure/constraint verification.

Preserve comments/provenance, document authored APIs and update repository guidance with behavior changes. Graphify remains local-only, with ignored generated output and no cloud extraction.
