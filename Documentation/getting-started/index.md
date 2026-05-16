# Getting Started

## Installation

### macOS (Homebrew)

```bash
brew tap cratis/cratis
brew install cratis
```

To upgrade:

```bash
brew upgrade cratis
```

### Windows (Winget)

```powershell
winget install Cratis.Cli
```

To upgrade:

```powershell
winget upgrade Cratis.Cli
```

### Windows (Chocolatey)

```powershell
choco install cratis
```

To upgrade:

```powershell
choco upgrade cratis
```

### Linux

Download and install the pre-built native binary from the [latest release](https://github.com/Cratis/cli/releases/latest):

```bash
# x64 (Intel/AMD)
curl -Lo cratis.tar.gz https://github.com/Cratis/cli/releases/latest/download/cratis-linux-x64.tar.gz
# arm64
# curl -Lo cratis.tar.gz https://github.com/Cratis/cli/releases/latest/download/cratis-linux-arm64.tar.gz
tar -xzf cratis.tar.gz
sudo mv cratis /usr/local/bin/cratis
```

To upgrade, repeat the steps above with the new release.

Native release artifact names:

| Platform | x64 | arm64 |
| --- | --- | --- |
| macOS | `cratis-<version>-osx-x64.tar.gz` | `cratis-<version>-osx-arm64.tar.gz` |
| Linux | `cratis-<version>-linux-x64.tar.gz` | `cratis-<version>-linux-arm64.tar.gz` |

### .NET Global Tool

Requires [.NET 10 or later](https://dotnet.microsoft.com/download).

```bash
dotnet tool install -g Cratis.Cli
```

To upgrade an existing installation:

```bash
dotnet tool update -g Cratis.Cli
```

## First Run

The first time you run any `cratis` command, the CLI automatically creates a `default` context pointing at `chronicle://localhost:35000/?disableTls=true`. No manual setup is required to get started against a local Chronicle server.

Running `cratis` with no arguments prints the active context name and server URL:

```bash
cratis
```

Run `cratis get-started` to test the server connection and see a summary of useful commands:

```bash
cratis get-started
```

## Shell Completions

```bash
cratis completions install
```

Detects your current shell automatically. Use `--shell bash|zsh|fish|powershell` to target a specific shell. Restart your shell or source your profile to activate.

On Windows, PowerShell is detected automatically. The installer writes the completion hook to your PowerShell profile (`$PROFILE`).

## Changing the Server

The auto-created `default` context points at `localhost:35000`. To point it at a different server:

```bash
cratis context set-value server chronicle://myserver.example.com:35000/
```

To create a separate named context for a different environment:

```bash
cratis context create staging --server chronicle://staging.example.com:35000/
cratis context set staging
```

For full context management see the [Context](../context/index.md) page.

## Using an Environment Variable

As an alternative to contexts, set `CHRONICLE_CONNECTION_STRING` in your shell or CI environment. This is convenient for automation and containers.

The full resolution order is: `--server` flag > `CHRONICLE_CONNECTION_STRING` > active context > default `chronicle://localhost:35000/?disableTls=true`.

## AI Tool Integration

Run `cratis init` in your project directory to configure AI tools (Claude Code, GitHub Copilot, Cursor, Windsurf). It generates a `CHRONICLE.md` file containing the full command catalog as embedded JSON, installs tool-specific instruction files, and adds a `chronicle-diagnose` slash command:

```bash
cratis init
```

When the CLI updates, refresh the embedded snapshot:

```bash
cratis init --refresh
```

For a live JSON descriptor of all CLI capabilities (same data embedded in `CHRONICLE.md`):

```bash
cratis llm-context
cratis llm-context --schema   # JSON Schema for the descriptor format
```
