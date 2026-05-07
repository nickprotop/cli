<div align="center">
  <a href="https://cratis.io">
    <img src="https://raw.githubusercontent.com/Cratis/cli/main/cratis.svg" alt="Cratis" width="480" style="background-color: white">
  </a>

  <h3 align="center">Cratis CLI</h3>

  <p align="center">
    The official command-line tool for managing and exploring Chronicle event stores.
    <br />
    <a href="https://cratis.io"><strong>Explore the docs »</strong></a>
    <br />
    <br />
    <a href="https://github.com/cratis/chronicle/issues/new?labels=bug">Report a Bug</a>
    &nbsp;·&nbsp;
    <a href="https://github.com/cratis/chronicle/issues/new?labels=enhancement">Request a Feature</a>
    &nbsp;·&nbsp;
    <a href="https://discord.gg/kt4AMpV8WV">Join the Discord</a>
  </p>

  <p align="center">
    <a href="https://discord.gg/kt4AMpV8WV">
      <img src="https://img.shields.io/discord/1182595891576717413?label=Discord&logo=discord&color=7289da" alt="Discord">
    </a>
    <a href="https://www.nuget.org/packages/Cratis.Cli">
      <img src="https://img.shields.io/nuget/v/Cratis.Cli?logo=nuget" alt="NuGet">
    </a>
  </p>
</div>

---

## Installation

### macOS (Homebrew)

```shell
brew tap cratis/cratis
brew install cratis
```

To upgrade:

```shell
brew upgrade cratis
```

### Linux

Download and install the pre-built native binary from the [latest release](https://github.com/Cratis/cli/releases/latest):

```shell
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

The CLI is also distributed as a [.NET global tool](https://learn.microsoft.com/en-us/dotnet/core/tools/global-tools) and requires [.NET 10+](https://dotnet.microsoft.com/download).

```shell
dotnet tool install -g Cratis.Cli
```

Verify the installation:

```shell
cratis --version
```

### Shell Completions

Install tab completions for your shell:

```shell
cratis completions install        # auto-detects bash / zsh / fish
```

Restart your shell or source your profile to activate completions.

---

## Getting Started

The `get-started` command is your entry point. Run it right after installation — it checks your configuration, tests the server connection, and shows you the most useful commands to explore.

```shell
cratis get-started
```

If no context is configured yet, the command will walk you through the setup:

```
╭─ Getting Started ───────────────────────────────────────────────╮
│                                                                 │
│  No configuration found. Set up a context to get started:       │
│                                                                 │
│  1. Create a context pointing at your server:                   │
│     cratis context create dev \                                 │
│       --server chronicle://localhost:35000/?disableTls=true     │
│                                                                 │
│  2. Verify the connection:                                      │
│     cratis get-started                                          │
│                                                                 │
│  3. Start exploring:                                            │
│     cratis chronicle event-stores list                          │
│                                                                 │
╰─────────────────────────────────────────────────────────────────╯
```

Once a context is configured, `get-started` shows your active context, connection status, and a curated list of commands to explore and debug your event store.

### Setting Up a Context

A context stores a named connection to a Chronicle server:

```shell
# Create a context for local development
cratis context create dev \
  --server chronicle://localhost:35000/?disableTls=true

# Make it the active context
cratis context set dev

# Confirm everything is wired up
cratis get-started
```

---

## Common Commands

```shell
# Explore the event store
cratis chronicle event-stores list
cratis chronicle namespaces list
cratis chronicle events get

# Inspect observers and read models
cratis chronicle observers list
cratis chronicle projections list
cratis chronicle read-models instances <name>

# Diagnose problems
cratis chronicle diagnose
cratis chronicle failed-partitions list
cratis chronicle observers replay <id>

# Manage contexts
cratis context list
cratis context show
```

Use `--help` on any command or subgroup for the full option reference:

```shell
cratis --help
cratis chronicle observers --help
```

### Output Formats

Every command supports `-o` / `--output` to control the format:

| Flag | Output |
|---|---|
| `-o table` | Rich terminal table (default) |
| `-o plain` | Tab-separated — great for scripting |
| `-o json` | Pretty-printed JSON |
| `-o json-compact` | Compact JSON — fewer tokens for AI tools |

```shell
# Pipe quiet output for scripting
cratis chronicle observers list -q | xargs -I {} cratis chronicle observers replay {} -y
```

---

## AI Tool Integration

Run `cratis init` in your project root to generate a `CHRONICLE.md` file that gives Claude, Copilot, and Cursor a machine-readable overview of your Chronicle setup:

```shell
cratis init
```

For a full machine-readable capability descriptor, run:

```shell
cratis llm-context
```

---

## Support

| Channel | Details |
|---|---|
| 💬 **Discord** | Join the community on [Discord](https://discord.gg/kt4AMpV8WV) |
| 🐛 **GitHub Issues** | [Report bugs or request features](https://github.com/cratis/chronicle/issues) |
| 📚 **Documentation** | [https://cratis.io](https://cratis.io) |

---

## License

Distributed under the **MIT License**. See [`LICENSE`](./LICENSE) for full details.
