# Read Models

A read model is projected state built from events by an observer. Chronicle maintains read model instances in storage and updates them as new events arrive. Each instance is identified by a key — typically an event source ID — and contains a document that represents the current state of that entity.

## list

Lists all read model definitions registered in the specified event store and namespace.

```bash
cratis chronicle read-models list
```

### Options

| Flag | Description |
|---|---|
| `-e, --event-store <NAME>` | Event store to inspect. Defaults to `default`. |
| `-n, --namespace <NAME>` | Namespace to inspect. Defaults to `Default`. |

### Output columns

The table includes a **Queryable** column (`Yes`/`No`) indicating whether the read model stores state server-side and can be inspected with `get` or `instances`. Client-owned read models show `No` — their state lives in the client application and cannot be retrieved through the Chronicle server. The JSON output includes a `queryable` boolean field for programmatic use.

### Output tip

Use `--output plain` when enumerating read model names. The plain format is approximately 27 times smaller than JSON because it omits the full type metadata included in the JSON output.

```bash
cratis chronicle read-models list --output plain
```

### Examples

List all read models:

```bash
cratis chronicle read-models list
```

Get read model names for scripting:

```bash
cratis chronicle read-models list -q
```

## instances

Lists the stored instances of a read model, with pagination support.

```bash
cratis chronicle read-models instances <READ_MODEL>
```

### Arguments

| Argument | Description |
|---|---|
| `READ_MODEL` | The read model name. Use `read-models list -q` to retrieve names. |

### Options

| Flag | Description |
|---|---|
| `-e, --event-store <NAME>` | Event store to inspect. Defaults to `default`. |
| `-n, --namespace <NAME>` | Namespace to inspect. Defaults to `Default`. |
| `--page <NUMBER>` | Zero-based page index. Defaults to `0`. |
| `--page-size <COUNT>` | Number of instances per page. Defaults to `20`. |

### Examples

List the first page of instances:

```bash
cratis chronicle read-models instances UserProfile
```

List the second page with a larger page size:

```bash
cratis chronicle read-models instances UserProfile --page 1 --page-size 50
```

## get

Gets a single read model instance by key.

```bash
cratis chronicle read-models get <READ_MODEL> <KEY>
```

### Arguments

| Argument | Description |
|---|---|
| `READ_MODEL` | The read model name. |
| `KEY` | The instance key, typically an event source ID. |

### Options

| Flag | Description |
|---|---|
| `-e, --event-store <NAME>` | Event store to inspect. Defaults to `default`. |
| `-n, --namespace <NAME>` | Namespace to inspect. Defaults to `Default`. |
| `--sequence <NAME>` | Event sequence associated with the read model. Defaults to the event log. |

### Examples

Get a read model instance:

```bash
cratis chronicle read-models get UserProfile user-42
```

Get the full document as JSON:

```bash
cratis chronicle read-models get UserProfile user-42 -o json
```

## occurrences

Gets the number of times a read model type has been stored, optionally for a specific generation.

```bash
cratis chronicle read-models occurrences <READ_MODEL_TYPE>
```

### Arguments

| Argument | Description |
|---|---|
| `READ_MODEL_TYPE` | The read model type name. |

### Options

| Flag | Description |
|---|---|
| `-e, --event-store <NAME>` | Event store to inspect. Defaults to `default`. |
| `-n, --namespace <NAME>` | Namespace to inspect. Defaults to `Default`. |
| `--generation <NUMBER>` | Read model generation to count. Defaults to `1`. |

### Examples

Count all instances of a read model:

```bash
cratis chronicle read-models occurrences UserProfile
```

Count a specific generation:

```bash
cratis chronicle read-models occurrences UserProfile --generation 2
```

## snapshots

Lists or retrieves snapshots for a specific read model instance. Snapshots are point-in-time captures of a read model's state, including the sequence number at which the snapshot was taken.

```bash
cratis chronicle read-models snapshots <READ_MODEL> <KEY>
```

### Arguments

| Argument | Description |
|---|---|
| `READ_MODEL` | The read model name. |
| `KEY` | The instance key, typically an event source ID. |

### Options

| Flag | Description |
|---|---|
| `-e, --event-store <NAME>` | Event store to inspect. Defaults to `default`. |
| `-n, --namespace <NAME>` | Namespace to inspect. Defaults to `Default`. |

### Examples

List snapshots for an instance:

```bash
cratis chronicle read-models snapshots UserProfile user-42
```

Get snapshot details including full documents and event metadata:

```bash
cratis chronicle read-models snapshots UserProfile user-42 -o json
```
