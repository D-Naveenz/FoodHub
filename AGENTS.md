# FoodHub agent instructions

## Purpose and reading order

This is an educational, Foodhub-inspired customer application built with WinUI 3 and SQL Server. Assume readers know only programming basics. The agreed scope is one takeaway, local profiles, menu browsing, checkout, simulated delivery tracking, and order history.

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

The following are recommendations pending further user guidance, not claims about existing implementation:

- Use simple MVVM, thin page code-behind for view behavior, and application services for workflows. Keep WinUI types in `FoodHub`; keep database access and non-UI logic in `FoodHub.Core` for now.
- Use asynchronous database operations, cancellation where appropriate, and a short-lived context per operation. Do not block the UI thread or share one context across concurrent operations.
- Retain SQL-first development and EF Core Power Tools provisionally. Use `PlaceOrder` and `UpdateOrderStatus` for order writes; use normal EF operations for profile/address edits. Do not duplicate order transactions in the UI.
- Treat generated files in `FoodHub.Core/Models` as regeneration-owned. Prefer separate authored services and partial extensions; do not add teaching comments directly to generated output only to lose them at regeneration.
- Do not introduce migrations alongside SQL-first changes without explicitly choosing a new schema authority. Keep SQL, scaffolding, and explanations consistent when the schema changes.
- Use local fictional data. Profile selection is not authentication; status controls simulate delivery and do not represent GPS.

## Completion and maintenance

Implement the requested slice, preserve unrelated user work, and update relevant documentation in the same change. Verify behavior proportionately: focused logic tests, SQL Server integration checks for database rules, and manual WinUI checks for the customer journey. Never claim runtime verification from a graph query or source inspection alone.

Record what changed, why, what was checked, and any limitations. Keep reusable decisions in repository documents. No cloud memory writes are needed.
