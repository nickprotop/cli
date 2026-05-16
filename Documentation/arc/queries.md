# arc queries list

Lists all registered query endpoints in the connected Arc application.

## Usage

```bash
cratis arc queries list [--url <URL>] [-o <FORMAT>]
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
| Name | The simple name of the query method (e.g. `AllAccounts`). |
| Namespace | The dot-separated namespace path of the query. |
| Route | The HTTP GET route registered for this query (e.g. `/api/accounts/all-accounts`). |
| Type | The runtime type name of the query (e.g. `ClientObservable`, `IEnumerable`). |
| Summary | The XML documentation summary of the query type, if available. |

## Examples

```bash
# List queries using the default URL
cratis arc queries list

# List queries from a specific URL
cratis arc queries list --url https://myapp.local:5001

# Output as compact JSON
cratis arc queries list -o json-compact
```

## Notes

The introspection endpoint `GET /.cratis/queries` must be registered in the Arc application. This happens automatically when the application calls `MapIntrospectionEndpoints()` during startup.
