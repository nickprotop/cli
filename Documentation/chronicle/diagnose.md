# Diagnose

The `diagnose` command runs a comprehensive health check against the connected Chronicle server. It tests connectivity, retrieves the server version, inspects event stores, checks observer states, counts failed partitions, lists active recommendations, and reads the event sequence tail. The results give you a quick picture of whether the system is healthy or requires attention.

The command exits with a non-zero status code if any health check fails, making it suitable for use in CI pipelines and monitoring scripts.

## Usage

```bash
cratis chronicle diagnose
```

### Options

| Flag | Description |
|---|---|
| `--watch` | Continuously refresh the output at the specified interval. Press Ctrl+C to stop. Requires table output format. |
| `--interval <SECONDS>` | Refresh interval in seconds when using `--watch`. Defaults to `5`. |

### Health Checks

The command runs the following checks in sequence:

| Check | What it verifies |
|---|---|
| Connection | The CLI can reach the Chronicle management API. |
| Server version | The server responds with a valid version identifier. The CLI also checks the NuGet feed for a newer Chronicle server release. When a newer version is available, a `▲` warning is shown next to the version in all output modes (table, plain, JSON, and watch). |
| Event stores | At least one event store exists and is accessible. |
| Observers | All observers are active and not stopped or faulted. |
| Failed partitions | No observer partitions are in a failed state. |
| Recommendations | No server-generated recommendations are pending. |
| Event sequence tail | The event log tail is readable and returns a valid sequence number. |

### Exit Codes

| Code | Meaning |
|---|---|
| `0` | All health checks passed. |
| Non-zero | One or more checks failed. |

### Examples

Run a one-time health check:

```bash
cratis chronicle diagnose
```

Run against a specific server:

```bash
cratis chronicle diagnose --server chronicle://prod.example.com:35000
```

Get machine-readable output for CI:

```bash
cratis chronicle diagnose -o json
echo "Exit code: $?"
```

Watch mode — refresh every five seconds:

```bash
cratis chronicle diagnose --watch
```

Watch mode with a custom interval:

```bash
cratis chronicle diagnose --watch --interval 10
```

Use in a CI pipeline and fail the build if the server is unhealthy:

```bash
cratis chronicle diagnose -o json --yes
if [ $? -ne 0 ]; then
  echo "Chronicle health check failed"
  exit 1
fi
```
