---
audience: [operator, contributor]
doc_type: reference
status: current
last_verified: 2026-01-19
canonical: true
note: "Authoritative configuration reference for Garden-Moss daemon. Documents layered configuration system (CLI > Env > Config File > Defaults), all options (stone_name, port, log_level), file locations, usage examples, and troubleshooting."
related:
  - TECHNICAL-SPEC.md
  - PORT-ALLOCATION.md
---

# Moss Configuration System

The Garden-Moss Daemon supports a layered configuration system with the following priority order:

## Priority Order

1. **CLI Arguments** (highest priority)
2. **Environment Variables**
3. **Configuration File** (moss.toml)
4. **Built-in Defaults** (lowest priority)

## Configuration Options

### `stone_name`
- **Type:** String
- **Default:** `"stone-01"`
- **CLI:** `--stone-name <NAME>`
- **Env:** `STONE_NAME`
- **Config:** `stone_name = "name"`
- **Description:** Unique identifier for this stone instance

### `port`
- **Type:** Integer (u16)
- **Default:** `3001`
- **CLI:** `--port <PORT>`
- **Env:** `PORT`
- **Config:** `port = 3001`
- **Description:** HTTP API server port

### `log_level`
- **Type:** String
- **Default:** `"info"`
- **Valid Values:** `trace`, `debug`, `info`, `warn`, `error`
- **CLI:** `--log-level <LEVEL>`
- **Env:** `RUST_LOG`
- **Config:** `log_level = "info"`
- **Description:** Logging verbosity level

## Configuration File

### File Locations

- **Linux:** `/etc/zen-garden/garden-moss.toml`
- **Windows:** `./moss.toml` (current directory)

### Example moss.toml

```toml
# Production configuration
stone_name = "prod-stone-east-01"
port = 3001
log_level = "warn"
```

### Installation

Copy the example configuration:

**Linux:**
```bash
sudo cp installer/moss.toml.example /etc/zen-garden/garden-moss.toml
sudo nano /etc/zen-garden/garden-moss.toml
```

**Windows:**
```powershell
Copy-Item installer\moss.toml.example moss.toml
notepad moss.toml
```

## Usage Examples

### 1. Use All Defaults
```bash
./garden-moss
# Result: stone_name="stone-01", port=3001, log_level="info"
```

### 2. Override with Environment Variables
```bash
export STONE_NAME="dev-stone"
export PORT=3002
export RUST_LOG="debug"
./garden-moss
# Result: stone_name="dev-stone", port=3002, log_level="debug"
```

### 3. Override with CLI Arguments
```bash
./garden-moss --stone-name prod-01 --port 3003 --log-level warn
# Result: stone_name="prod-01", port=3003, log_level="warn"
```

### 4. Mixed Configuration (demonstrates priority)
```toml
# moss.toml
stone_name = "config-stone"
port = 3001
log_level = "info"
```

```bash
export PORT=3002
./garden-moss --log-level debug
# Result: stone_name="config-stone" (from config file)
#         port=3002 (from env var, overrides config)
#         log_level="debug" (from CLI arg, overrides both)
```

## Implementation Details

### Configuration Loading

The configuration system is implemented in [src/linux/moss/src/main.rs](../../src/linux/moss/src/main.rs):

1. **File Loading:** `MossConfig::load()` attempts to read the platform-specific moss.toml file
2. **CLI Parsing:** `clap` parses arguments and environment variables
3. **Merging:** Values are resolved using `.or_else()` chains following priority order
4. **Logging:** The startup log message shows which configuration was loaded

### Code Snippet

```rust
// Load configuration from file first (lowest priority)
let config = MossConfig::load();

// Parse CLI arguments (CLI and env vars handled by clap with #[arg(env)])
let cli = <Cli as clap::Parser>::parse();

// Merge configuration with priority: CLI > Env > Config File > Defaults
let stone_name = cli.stone_name
    .or_else(|| config.as_ref().and_then(|c| c.stone_name.clone()))
    .unwrap_or_else(|| "stone-01".to_string());
```

## Dependencies

- **serde:** Serialization framework with derive macros
- **toml:** TOML parsing
- **clap:** Command-line argument parsing with environment variable support

See [Cargo.toml](../../src/linux/moss/Cargo.toml) for exact versions.

## Testing

Test configuration priority:

```bash
# 1. Test config file loading
echo 'stone_name = "file-test"
port = 3005
log_level = "debug"' > moss.toml
./garden-moss &
# Check logs for "Loaded configuration from file"

# 2. Test env override
export STONE_NAME="env-test"
./garden-moss &
# Should show stone_name="env-test"

# 3. Test CLI override
./garden-moss --stone-name cli-test &
# Should show stone_name="cli-test"

# Cleanup
killall moss
rm moss.toml
```

## Troubleshooting

### Config file not found
```
DEBUG Config file not found, using defaults path=/etc/zen-garden/garden-moss.toml
```
**Solution:** This is normal. Create the file or use env vars/CLI args.

### Config file parse error
```
WARN Failed to parse config file path=/etc/zen-garden/garden-moss.toml error="TOML parse error..."
```
**Solution:** Check TOML syntax. All fields must be valid types (strings in quotes, numbers without quotes).

### Port already in use
```
ERROR Failed to bind to 0.0.0.0:3001: address already in use
```
**Solution:** Use `--force` flag to kill existing moss instances, or change the port.
