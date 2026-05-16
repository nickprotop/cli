# Arc

The `cratis arc` command group provides commands for introspecting a running Cratis Arc application. It connects to the Arc application's built-in introspection HTTP endpoints to discover registered commands and queries.

## Connection Flags

All `cratis arc` commands accept the following connection flag:

| Flag | Description |
|---|---|
| `--url <URL>` | Base URL of the Arc application. Overrides the `ARC_URL` environment variable. Defaults to `http://localhost:5000`. |

## Connection Resolution Order

The CLI resolves the Arc application URL in this order:

1. `--url` flag
2. `ARC_URL` environment variable
3. `Properties/launchSettings.json` or `properties/launchSettings.json` `applicationUrl`
4. Default: `http://localhost:5000`

## Sub-Commands

| Sub-command | Description |
|---|---|
| `commands` | List registered command endpoints in the Arc application. |
| `queries` | List registered query endpoints in the Arc application. |

## Global Flags

All `cratis arc` commands also inherit the top-level global flags:

| Flag | Description |
|---|---|
| `-o, --output <FORMAT>` | Output format: `table`, `plain`, `json`, `json-compact`. |
| `-q, --quiet` | Output only key identifiers, one per line. |
| `--debug` | Print debug information to stderr. |
