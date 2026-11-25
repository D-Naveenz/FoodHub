# FoodHub agent instructions

## Purpose and reading order

This is an educational, Foodhub-inspired customer application built with WinUI 3 and SQL Server. Assume readers know only programming basics. The agreed scope is one takeaway, local email/password accounts with guest browsing, menu browsing, checkout, simulated delivery tracking, and order history.

Before changing a feature, read:

1. [README](README.md) for the repository map and setup.
2. [Project scope](docs/Foodhub_Project_Scope.md) for requirements and acceptance criteria.
3. [Architecture](docs/Architecture.md) and [database workflow](docs/Database.md) for current facts and provisional defaults.
4. [Roadmap](docs/Development_Roadmap.md) for implementation order and verification.
5. [Foodhub research](docs/Foodhub_Research.md) when interpreting the business domain.

Later explicit user guidance takes precedence over provisional recommendations. Keep agreed scope, current implementation, and proposed behavior distinct. Do not expand into staff management or production-scale features without a task requiring them.

## Local Graphify

Use the installed Graphify skill and local graph for structural code questions. Cloud indexing, hosted MCP queries, cloud memory, and external model extraction are not part of this project's workflow.

- The graph is `graphify-out/graph.json`, relative to the repository root. Reuse it when present; use the local MCP server when exposed, otherwise the local CLI.
- Useful CLI queries: `graphify query "How does navigation work?"`, `graphify explain "MainWindow"`, and `graphify path "App" "MainWindow"`. Resolve ambiguous names using the candidates returned by the tool.
- Local MCP tools include `query_graph`, `get_node`, `get_neighbors`, `shortest_path`, and `graph_stats`. Discover their live schemas rather than assuming cloud connector schemas apply.
- Graphs are snapshots. Refresh after relevant source changes before relying on their structure. Read the current source before editing; use `rg` and direct reads when the graph is missing, stale, incomplete, or ambiguous. Missing edges do not prove there are no calls, dependencies, or tests.
- Build or refresh without cloud calls from the repository root: `graphify extract . --code-only --no-cluster`. Read Markdown directly; this graph deliberately excludes semantic document and image extraction.
- Keep generated graph output ignored by Git. Do not install watchers or Git hooks, enable semantic extraction, or publish graph data as an incidental task.

See [local Graphify setup](docs/Graphify.md) for installation, MCP configuration, limitations, and verification.

## Educational coding standards

- Preserve comments, XML documentation, copyright notices, and generated-file headers. Remove or replace comments only when necessary, and explain meaningful removals in the change summary.
- Add `///` documentation to authored types and relevant constructors, properties, methods, interfaces, and enums. Describe purpose and behavior; include parameters, returns, and exceptions where useful to a caller. Internal workflow methods deserve explanations when a beginner would otherwise struggle to follow them.
- Add inline comments explaining intent, data flow, validation, transactions, asynchronous work, and non-obvious decisions. Avoid comments that merely repeat a statement. Update explanations when behavior changes.
- Prefer explicit, readable steps and small cohesive methods. Follow `.editorconfig`; do not introduce abstraction solely to demonstrate a pattern.
- Explain new dependencies and unfamiliar concepts near their use or in the architecture document. Keep teaching commentary out of customer-facing UI text.

## Implementation defaults

The following guidance includes the approved account/migration decisions. Remaining ordering defaults are provisional and do not claim that those workflows are implemented:

- Reusable WinUI hosting belongs to Dhara.Extensions.WinUI.Hosting; Dhara must not reference FoodHub projects. Core keeps portable configuration/storage and database registration, with no Windows SDK dependency. FoodHub maps Dhara paths/status to Core contracts. Preserve the generated WinUI entry point and forward launch through IWinUIApplicationLifetime; keep domain startup work in FoodHubStartupParticipant. Derive host environments from the consuming assembly’s build metadata (Debug → Development, Release → Production, custom names unchanged); do not use DOTNET_ENVIRONMENT. Load base JSON, environment JSON, then local JSON. Share one LocalSettingsStore for all JSON writers; appearance applies immediately and database changes require restart. Do not reintroduce legacy settings migration.
- Use simple MVVM, thin page code-behind for view behavior, and application services for workflows. Keep reusable WinUI helpers in `Dhara.Extensions.WinUI` and application views in `FoodHub`; keep database access and non-UI logic in `FoodHub.Core` for now.
- Use asynchronous database operations, cancellation where appropriate, and a short-lived context per operation. Do not block the UI thread or share one context across concurrent operations.
- EF Core migrations now own schema evolution; do not regenerate the maintained context/entities with EF Core Power Tools. Use the current InitialCreate migration and preserve procedure contracts. FoodHub_Dev is disposable; explicit local reset does not require backups. Use `PlaceOrder` and `UpdateOrderStatus` for order writes; use normal EF operations for profile/address edits. Do not duplicate order transactions in the UI.
- Historical scaffold headers in `FoodHub.Core/Models` record provenance. Entities/context are now maintained code; preserve their headers and add authored behavior through services and partial mappings. The old scaffold configuration is archived under docs/legacy.
- Keep migrations, model snapshots, procedure SQL and explanations consistent. The historical SQL creation script is not a second current schema authority. Use FoodHub.Database reset explicitly for localhost/FoodHub_Dev when a rebuild is needed; it drops all development rows without backups. Never reset on app startup, connection tests, or a failed migration. Production backup/upgrade workflows are out of scope.
- Use local fictional data. Accounts use local opaque sessions; direct database access remains an educational security boundary; status controls simulate delivery and do not represent GPS.

## Completion and maintenance

Implement the requested slice, preserve unrelated user work, and update relevant documentation in the same change. Verify behavior proportionately: focused logic tests, SQL Server integration checks for database rules, and manual WinUI checks for the customer journey. Never claim runtime verification from a graph query or source inspection alone.

Record what changed, why, what was checked, and any limitations. Keep reusable decisions in repository documents. No cloud memory writes are needed.
