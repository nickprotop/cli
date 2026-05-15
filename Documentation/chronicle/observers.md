# Observers

Observers are the processing units that consume events from an event sequence and produce side effects or projections. Chronicle supports three kinds of observer: reactors (side effects), reducers (compute read model state), and projections (declarative read model builders). Each observer tracks its position in the event sequence and pauses a partition when it encounters a failure.

## list

Lists all observers registered in the specified event store and namespace, including their current state, quarantine status, and sequence position.

```bash
cratis chronicle observers list
```

### Options

| Flag | Description |
|---|---|
| `-e, --event-store <NAME>` | Event store to inspect. Defaults to `default`. |
| `-n, --namespace <NAME>` | Namespace to inspect. Defaults to `Default`. |
| `-t, --type <TYPE>` | Filter by observer type: `all`, `reactor`, `reducer`, `projection`. Defaults to `all`. |

### Examples

List all observers:

```bash
cratis chronicle observers list
```

List only projection observers:

```bash
cratis chronicle observers list --type projection
```

List observers in plain format for scripting:

```bash
cratis chronicle observers list --output plain
```

Get observer IDs for piping:

```bash
cratis chronicle observers list -q
```

## show

Shows detailed information about a single observer, including its event type subscriptions, current state, and sequence position.

```bash
cratis chronicle observers show <OBSERVER_ID>
```

### Arguments

| Argument | Description |
|---|---|
| `OBSERVER_ID` | The observer identifier. Use `observers list -q` to retrieve identifiers. |

### Options

| Flag | Description |
|---|---|
| `-e, --event-store <NAME>` | Event store to inspect. Defaults to `default`. |
| `-n, --namespace <NAME>` | Namespace to inspect. Defaults to `Default`. |
| `--sequence <NAME>` | Event sequence the observer reads from. Defaults to the event log. |

### Examples

Show observer details in a table:

```bash
cratis chronicle observers show my-observer-id
```

Get full observer detail as JSON:

```bash
cratis chronicle observers show my-observer-id -o json
```

## replay

Replays an observer from sequence number zero, re-processing every event it subscribes to. This is a destructive operation: the observer discards all accumulated state and rebuilds it from scratch.

Use replay only as a last resort — for example, when an observer's read model is known to be corrupt and cannot be recovered by a partition retry.

```bash
cratis chronicle observers replay <OBSERVER_ID>
```

The command prompts for confirmation before proceeding. Pass `--yes` to skip the prompt in automated workflows.

### Arguments

| Argument | Description |
|---|---|
| `OBSERVER_ID` | The observer to replay. |

### Options

| Flag | Description |
|---|---|
| `-e, --event-store <NAME>` | Event store. Defaults to `default`. |
| `-n, --namespace <NAME>` | Namespace. Defaults to `Default`. |
| `-y, --yes` | Skip confirmation prompt. |

### Examples

Replay an observer interactively:

```bash
cratis chronicle observers replay my-observer-id
```

Replay without prompting (automation):

```bash
cratis chronicle observers replay my-observer-id --yes
```

Replay all observers (use with caution):

```bash
cratis chronicle observers list -q | xargs -I {} cratis chronicle observers replay {} --yes
```

## replay-partition

Replays a single partition of an observer from sequence number zero for that partition only. This is less disruptive than a full replay because it leaves all other partitions intact.

```bash
cratis chronicle observers replay-partition <OBSERVER_ID> <PARTITION>
```

The command prompts for confirmation before proceeding.

### Arguments

| Argument | Description |
|---|---|
| `OBSERVER_ID` | The observer whose partition to replay. |
| `PARTITION` | The event source ID identifying the partition. |

### Options

| Flag | Description |
|---|---|
| `-e, --event-store <NAME>` | Event store. Defaults to `default`. |
| `-n, --namespace <NAME>` | Namespace. Defaults to `Default`. |
| `-y, --yes` | Skip confirmation prompt. |

### Examples

Replay a specific partition:

```bash
cratis chronicle observers replay-partition my-observer-id user-42
```

## retry-partition

Retries a failed partition without replaying it from the beginning. The observer attempts to process the event that last caused the failure. Use this after fixing the underlying bug — it is the preferred recovery path for transient errors and corrected application bugs.

```bash
cratis chronicle observers retry-partition <OBSERVER_ID> <PARTITION>
```

### Arguments

| Argument | Description |
|---|---|
| `OBSERVER_ID` | The observer whose partition to retry. |
| `PARTITION` | The event source ID identifying the failed partition. |

### Options

| Flag | Description |
|---|---|
| `-e, --event-store <NAME>` | Event store. Defaults to `default`. |
| `-n, --namespace <NAME>` | Namespace. Defaults to `Default`. |
| `-y, --yes` | Skip confirmation prompt. |

### Examples

Retry a failed partition:

```bash
cratis chronicle observers retry-partition my-observer-id user-42
```

## clear-quarantine

Clears quarantine for a quarantined observer so it can resume processing.

```bash
cratis chronicle observers clear-quarantine <OBSERVER_ID>
```

The command prompts for confirmation before proceeding.

### Arguments

| Argument | Description |
|---|---|
| `OBSERVER_ID` | The observer whose quarantine should be cleared. |

### Options

| Flag | Description |
|---|---|
| `-e, --event-store <NAME>` | Event store. Defaults to `default`. |
| `-n, --namespace <NAME>` | Namespace. Defaults to `Default`. |
| `--sequence <NAME>` | Event sequence. Defaults to `event-log`. |
| `-y, --yes` | Skip confirmation prompt. |

### Examples

Clear quarantine for an observer:

```bash
cratis chronicle observers clear-quarantine my-observer-id
```

## Troubleshooting Workflow

Use this sequence when an observer appears stuck, behind, or failing:

1. Check the observer list for state and sequence numbers:

   ```bash
   cratis chronicle observers list --output plain
   ```

2. Inspect the specific observer for detailed state:

   ```bash
   cratis chronicle observers show my-observer-id -o json
   ```

3. Check for failed partitions:

   ```bash
   cratis chronicle failed-partitions list --output plain
   ```

4. Inspect the specific failed partition and its error:

   ```bash
   cratis chronicle failed-partitions show my-observer-id user-42
   ```

5. Fix the underlying application bug, then retry the partition:

   ```bash
   cratis chronicle observers retry-partition my-observer-id user-42
   ```

6. If the read model state is known to be corrupt and cannot be recovered by retry, replay the partition:

   ```bash
   cratis chronicle observers replay-partition my-observer-id user-42
   ```

7. Only as a final resort — if all partitions are corrupt or the observer schema changed incompatibly — replay the entire observer:

   ```bash
   cratis chronicle observers replay my-observer-id
   ```
