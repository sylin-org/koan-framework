# Changelog - garden-rake

All notable changes to garden-rake will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Progressive disclosure in discovery: stones displayed as discovered with timing ([P0-1])
- Timing helpers: `format_elapsed_time()` and `format_wall_clock()` in ui module
- Real network physics visibility: see exact response times (e.g., stone-01 [0.8s], stone-02 [2.1s])
- Cached discovery results now stream progressively for consistent UX
- Streaming progress updates for container installations via `/api/v1/events` polling ([P0-3])
  - Shows percentage, message, and elapsed time when stone supports progress endpoint
  - Falls back to elapsed-time display for older stones (graceful degradation)
  - 500ms poll interval, 5-minute timeout
- Garden vitality language throughout UI ([P1-5])
  - Status indicators use living metaphors: `[thriving]`, `[dormant]`, `[needs attention]`
  - Strengthens garden cultivation mental model vs clinical server management
- Spatial metaphor consistency ([P1-6])
  - Standardized prepositions: 'on' (hosting), 'at' (targeting), 'present on' (topology)
  - Completes language foundation with vitality terminology
  - Consistent mental model: services on stones, commands at endpoints
- Wall-clock timestamps in Watch command ([P2-7])
  - All events show `[HH:MM:SS]` timestamp for timeline correlation
  - Uses event timestamp if present, falls back to current time
  - Enables correlation with external logs and infrastructure events
- Destructive operation confirmations ([P2-8])
  - Remove command requires explicit confirmation (unless `--force` or quiet mode)
  - Shows service name and data deletion warning
  - Prevents accidental data loss

### Deprecated
- Status command marked as deprecated ([P2-9])
  - Displays warning: use `observe` for garden overview or `tend` to set default stone
  - Will be removed in future release after adequate deprecation period
  - Command remains functional to allow gradual migration

### Changed
- **BREAKING**: `discover_all_moss()` now uses callback-based streaming API instead of returning `Vec<String>`
  - Callers must provide callback: `|response, instant| { /* handle discovery */ }`
  - Progressive feedback eliminates batching delay
  - Migration: Refactor code using `discover_all_moss()` to streaming pattern

- **BREAKING**: Status indicators changed from technical to vitality language ([P1-5])
  - `[OK]` → `[thriving]` (green)
  - `[stopped]` → `[dormant]` (yellow)
  - `[ERROR]`/`[WARN]` → `[needs attention]` (red/yellow)
  - Legacy API responses (e.g., "healthy") still map correctly
  - Impact: Pervasive UI change across all status displays

- Help text and error messages use consistent spatial language ([P1-6])
  - "services on stone" (hosting), "stones present on network" (topology)
  - Eliminates cognitive friction from inconsistent prepositions

- Remove command now shows confirmation prompt by default ([P2-8])
  - Use `--force` flag to skip in scripts/automation
  - Skipped automatically in quiet mode

### Removed  
- **BREAKING**: `garden-rake context` command removed (duplicate of `tend`)
  - Migration: Use `garden-rake tend` instead
  - All context subcommands have tend equivalents:
    - `context show` → `tend` (no args)
    - `context set <target>` → `tend <target>`
    - `context clear` → `tend --clear`
  - Rationale: Eliminates code duplication, keeps stronger metaphor

### Fixed
- Discovery now shows stones as they respond (not after timeout)
- Cache hits display with timing information
- Offer command no longer blocks silently during installation
- Watch command always shows timestamps (no blank time fields)

---

## Previous Releases

See git history for changes before formal changelog tracking.
