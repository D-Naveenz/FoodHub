# FoodHub

An educational Foodhub-inspired WinUI 3 desktop application backed by Microsoft SQL Server. The goal is a complete, understandable customer journey: select a local profile, browse food, build a basket, place a delivery order, follow simulated progress, and review order history.

This is a learning project, not an official Foodhub application or an integration with its services.

## Start here

| Document | Purpose |
|---|---|
| [Agent instructions](AGENTS.md) | Coding, documentation, and local exploration rules |
| [Project scope](docs/Foodhub_Project_Scope.md) | Agreed features and acceptance criteria |
| [Foodhub research](docs/Foodhub_Research.md) | Official sources and educational simplifications |
| [Architecture](docs/Architecture.md) | Current structure and provisional design defaults |
| [Database workflow](docs/Database.md) | SQL setup, stored procedures, and scaffolding |
| [Development roadmap](docs/Development_Roadmap.md) | Small implementation milestones and verification |
| [Local Graphify](docs/Graphify.md) | Local code graph and MCP setup |

## Current implementation

- `FoodHub`: WinUI Navigation App shell with Home, About, and Settings pages, a custom title bar, and branding assets. Customer workflows are not implemented yet.
- `FoodHub.Core`: EF Core Power Tools-generated entities, SQL Server context, and wrappers for the order stored procedures.
- `docs`: a revisable scope, schema/seed script, and development references.

The UI targets `net10.0-windows10.0.26100.0`; the core library targets `net10.0`. EF Core SQL Server is referenced at `10.0.15`. Windows App SDK and build packages currently use floating versions, so the project files do not establish a fixed resolved WinUI version.

## Development setup

Use Windows with a .NET SDK supporting the target frameworks, the Windows SDK required by the project, and Visual Studio with WinUI development and MSIX tooling. SQL Server and SSMS or `sqlcmd` are needed for database setup. See [database setup](docs/Database.md) before executing SQL.

Open `FoodHub.slnx`, restore packages, select the `FoodHub` startup project and a matching platform such as x64, then launch using the packaged profile. The project also contains an unpackaged launch profile; verify that path separately when needed.

For a command-line build from the repository root:

```powershell
dotnet build FoodHub.slnx -p:Platform=x64
```

This is a development command, not a claim that a build or launch has been verified. Database connectivity and GUI workflows also need separate checks.

The current generated context defaults to `localhost`, database `FoodHub_Dev`, and Windows integrated authentication. Configuration improvements are planned. No internet service is required by the intended customer workflows, but SQL Server must be reachable. A fresh package restore can require internet access.

## Learning and contribution approach

Preserve useful comments and add XML documentation and flow explanations to authored code. Implement one demonstrable feature at a time. Architecture and database choices are provisional and can change with later guidance; update the documents when a decision changes.

Code exploration uses local Graphify only. Its graph is generated locally and excluded from version control; documentation is read directly. No Foodhub API credentials, cloud graph service, or online payment account is needed.
