# Local Graphify workflow

FoodHub uses the installed Graphify skill and local code graph. No hosted Graphify service, cloud memory, or external model is needed for this workflow. Read Markdown documents directly rather than indexing them semantically.

## Installed baseline

On 2026-11-24, Graphify `0.9.118` was installed through `uv`. Its optional MCP and SQL dependencies were added to repair the missing `anyio` import and enable SQL extraction. The skill is installed at `C:/Users/dashe/.codex/skills/graphify/SKILL.md`; it is a machine dependency, not a tracked project file.

To reproduce that installation on a development machine:

```powershell
uv tool install 'graphifyy[mcp,sql]==0.9.118'
graphify-mcp --help
```

If replacing an existing installation without the extras, use `uv tool install --reinstall 'graphifyy[mcp,sql]==0.9.118'`. Package installation may download dependencies; code extraction and queries below do not call a model or graph cloud service.

## Build and query

Run from the repository root:

```powershell
graphify extract . --code-only --no-cluster
graphify query "How does navigation work?" --budget 1500
graphify explain "MainWindow"
```

`--code-only` skips semantic extraction of documents and images. `--no-cluster` avoids community naming/model selection. This deliberately minimal workflow produces `graphify-out/graph.json`; it does not promise an HTML visualization, community report, or document graph. Generated output and caches are ignored by Git.

Reuse the graph until relevant source changes, then rerun the extraction command. Inspect warnings for skipped languages or failed files. An absent relationship can mean parser limitations; it does not establish that no relationship exists. Read current source and use `rg` when results are incomplete. SQL extraction is useful for locating definitions but may omit procedure bodies or dynamic behavior.

## Codex MCP configuration

The machine's existing `mcp_servers.graphify` entry uses a local stdio process. For this checkout, the executable and graph path are explicit:

```toml
[mcp_servers.graphify]
command = 'C:\Users\dashe\.local\bin\graphify-mcp.exe'
args = ['C:\Users\dashe\source\repos\C#\FoodHub\graphify-out\graph.json']
```

Adjust paths on another machine. A single global entry points to one project; change the graph argument deliberately when working in another repository. Preserve unrelated MCP settings. Do not confuse the local server's tools with the hosted connector's tools.

Restart/reconnect the Codex MCP session after changing configuration; an already open chat may not rediscover tools automatically. To test manually, `graphify-mcp` followed by the absolute graph path starts a stdio server and waits for an MCP client. Lack of terminal output alone is not a successful handshake.

Verify with an MCP client by initializing a session, listing tools, calling `graph_stats`, and running a bounded `query_graph` or `get_node` request against a known symbol. If the local server is not exposed in the chat yet, use the CLI rather than the cloud connector.

## Verification record — 2026-11-24

Local code extraction completed with 227 nodes and 362 edges, including SQL after installing its parser. CLI query and explain requests for `MainWindow` succeeded. An MCP stdio client initialized the configured server, listed its tools, called `graph_stats`, and retrieved `MainWindow` through `query_graph`. Community count is zero because clustering was deliberately skipped. No model tokens were used for this extraction.

The sandboxed client attempt stalled; the same local handshake check passed outside the sandbox. This confirms the executable, configuration, and protocol work together; it does not establish that an already open Codex chat has reloaded its MCP tools. Application builds, GUI behavior, and live SQL Server behavior were not tested in this documentation/setup task.
