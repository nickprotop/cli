# arc commands list

Lists all registered command endpoints in the connected Arc application.

## Usage

```bash
cratis arc commands list [--url <URL>] [-o <FORMAT>]
```

## Options

| Option | Description |
|---|---|
| `--url <URL>` | Base URL of the Arc application (default: `http://localhost:5000`). |
| `-o, --output <FORMAT>` | Output format: `table`, `plain`, `json`, `json-compact`. |

## Output

Each row contains:

| Column | Description |
|---|---|
| Name | The simple type name of the command (e.g. `OpenDebitAccount`). |
| Namespace | The dot-separated namespace path of the command. |
| Route | The HTTP POST route registered for this command (e.g. `/api/accounts/open-debit-account`). |
| Summary | The XML documentation summary of the command type, if available. |

## Examples

```bash
# List commands using the default URL
cratis arc commands list

# List commands from a specific URL
cratis arc commands list --url https://myapp.local:5001

# Output as compact JSON
cratis arc commands list -o json-compact
```

## Notes

The introspection endpoint `GET /.cratis/commands` must be registered in the Arc application. This happens automatically when the application calls `MapIntrospectionEndpoints()` during startup.
