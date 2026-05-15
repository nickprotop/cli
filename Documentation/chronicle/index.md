# Chronicle

The `cratis chronicle` command group provides the primary interface for interacting with a Chronicle server. Every sub-command connects to a Chronicle management API, authenticates if required, and operates on the event store you specify.

## Connection Flags

Most commands accept the following connection flags:

| Flag | Description |
|---|---|
| `--server <CONNECTION_STRING>` | Chronicle server connection string. Overrides the active context and the `CHRONICLE_CONNECTION_STRING` environment variable. |
| `--management-port <PORT>` | Management API port. Defaults to the port embedded in the connection string. |

For the full connection string format, see the [Connection](../reference/connection.md) reference page.

## Event Store and Namespace Flags

Commands that operate within a specific event store or namespace accept the following flags:

| Flag | Description |
|---|---|
| `-e, --event-store <NAME>` | Event store name. Defaults to `default`. |
| `-n, --namespace <NAME>` | Namespace within the event store. Defaults to `Default`. |

## Sub-Commands

| Sub-command | Description |
|---|---|
| `event-stores` | List event stores registered on the server. |
| `namespaces` | List namespaces within an event store. |
| `event-types` | List and inspect registered event type definitions. |
| `events` | Query events from an event sequence or retrieve the tail sequence number. |
| `observers` | List, inspect, replay, retry, and clear quarantine for observers. |
| `subscriptions` | Manage cross-event-store subscriptions for event forwarding. |
| `failed-partitions` | List and inspect partitions where an observer has failed. |
| `projections` | List and inspect projection definitions. |
| `read-models` | List, query, and inspect read model instances and snapshots. |
| `recommendations` | List, perform, and ignore server-generated recommendations. |
| `identities` | List principals that have interacted with the event store. |
| `jobs` | List, inspect, resume, and stop long-running server-side jobs. |
| `auth` | Check authentication status. |
| `applications` | Manage OAuth client applications authorized to connect to Chronicle. |
| `users` | Manage human users who can log in to Chronicle. |
| `diagnose` | Run a health check against the connected Chronicle server. |

## Global Flags

All `cratis chronicle` commands also inherit the top-level global flags:

| Flag | Description |
|---|---|
| `-o, --output <FORMAT>` | Output format: `table`, `plain`, `json`, `json-compact`. |
| `-q, --quiet` | Output only key identifiers, one per line. Useful for piping. |
| `-y, --yes` | Skip confirmation prompts. |
| `--debug` | Enable verbose debug output. |
