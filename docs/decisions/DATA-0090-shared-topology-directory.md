# DATA-0090: Shared topology directory — Koan.ZenGarden implementation

Status: Accepted

## Context

Zen Garden is introducing a shared topology directory (see `zen-garden/docs/decisions/TOPO-0002-shared-topology-directory.md`) where Moss and clients coexist:

```
/app/cache/zen-garden/                  (container mount point)
  garden-topology.json                  ← Moss writes (authoritative mesh snapshot)
  garden-stones.json                    ← Clients write (operational roster)
```

Moss auto-injects this directory as a bind mount for every managed container. The host path is `{data_dir}/topology/` (Linux: `/var/lib/zen-garden/topology/`). The container path is `/app/cache/zen-garden/` — which already matches `StoneRosterPathResolver`'s container convention.

Koan.ZenGarden needs two changes:

1. Rename `stones.json` → `garden-stones.json` so the two files pair semantically.
2. On cold start, read `garden-topology.json` as a secondary seed source — pre-warmed topology from Moss that the client has never directly discovered.

## Decision

### 1. Rename roster file

Change the roster filename constant from `stones.json` to `garden-stones.json`.

**File**: `src/Koan.ZenGarden/Constants.cs`

```csharp
public static class Persistence
{
    public const int DefaultPersistedCacheTtlHours = 168; // 7 days
    public const string DefaultCacheSubdirectory = ".Koan/zen-garden";
    public const string RosterFileName = "garden-stones.json";  // was "stones.json"
    public const string MossTopologyFileName = "garden-topology.json";
}
```

### 2. Migration shim

On load, if `garden-stones.json` does not exist but `stones.json` does at the same resolved path, rename it. This is a one-time migration that preserves existing cached topology for containers that have been running with the old filename.

**File**: `src/Koan.ZenGarden/Persistence/StoneRosterStore.cs`

Add migration logic at the beginning of `LoadAsync()`, before the existing file-read logic:

```csharp
public async Task<IReadOnlyList<CachedMossStone>> LoadAsync(CancellationToken ct = default)
{
    try
    {
        MigrateLegacyFilename();

        if (!File.Exists(_filePath))
        // ... rest of existing LoadAsync unchanged
    }
    // ...
}

private void MigrateLegacyFilename()
{
    if (File.Exists(_filePath))
        return; // New name already exists, nothing to do

    var dir = Path.GetDirectoryName(_filePath);
    if (string.IsNullOrEmpty(dir))
        return;

    var legacyPath = Path.Combine(dir, "stones.json");
    if (!File.Exists(legacyPath))
        return;

    try
    {
        File.Move(legacyPath, _filePath);
        _logger.LogInformation(
            "Migrated legacy roster file: {Legacy} → {Current}",
            legacyPath, _filePath);
    }
    catch (Exception ex)
    {
        // Race-safe: on shared volumes, multiple containers may attempt the rename
        // simultaneously. The first succeeds; others get FileNotFoundException.
        // This is expected and non-fatal — the winning container's rename stands.
        _logger.LogDebug(ex,
            "Could not migrate legacy roster file {Legacy}", legacyPath);
    }
}
```

The migration is synchronous and cheap — a single rename operation. It runs once; subsequent loads find `garden-stones.json` and skip. On shared volumes with concurrent container starts, the `catch` block makes the race harmless: first writer wins, others continue gracefully.

### 3. Consolidate topology entry model

`ZenGardenClient.cs` already contains a private `TopologyEntryResponse` record (around line 3080) used for HTTP active hydration. The file-based seed reads the same Rust `TopologyEntry` struct. Rather than creating a duplicate model, **extract and promote** this record.

**Current** (private in `ZenGardenClient.cs`):

```csharp
private sealed record TopologyEntryResponse
{
    [JsonPropertyName("stone_id")]   public string? StoneId { get; init; }
    [JsonPropertyName("stone_name")] public string? StoneName { get; init; }
    [JsonPropertyName("endpoint")]   public string? Endpoint { get; init; }
    [JsonPropertyName("moss_version")] public string? MossVersion { get; init; }
    [JsonPropertyName("last_seen")]  public DateTimeOffset? LastSeen { get; init; }
    [JsonPropertyName("health")]     public string? Health { get; init; }
}
```

**After**: Move to `src/Koan.ZenGarden/Persistence/MossTopologyEntry.cs` as `internal`, rename to `MossTopologyEntry`:

```csharp
using System.Text.Json.Serialization;

namespace Koan.ZenGarden.Persistence;

/// <summary>
/// Deserialization model for Moss's TopologyEntry (Rust schema, snake_case).
/// Used by both HTTP active hydration and file-based topology seeding.
/// Single model, two consumers — same source struct, same schema evolution.
/// </summary>
internal sealed record MossTopologyEntry
{
    [JsonPropertyName("stone_id")]
    public string? StoneId { get; init; }

    [JsonPropertyName("stone_name")]
    public string? StoneName { get; init; }

    [JsonPropertyName("endpoint")]
    public string? Endpoint { get; init; }

    [JsonPropertyName("moss_version")]
    public string? MossVersion { get; init; }

    [JsonPropertyName("last_seen")]
    public DateTimeOffset? LastSeen { get; init; }

    [JsonPropertyName("health")]
    public string? Health { get; init; }
}
```

Update `ZenGardenClient.cs`:
- Remove the private `TopologyEntryResponse` record
- Add `using Koan.ZenGarden.Persistence;`
- Replace all `TopologyEntryResponse` references with `MossTopologyEntry`
- The existing `TopologyApiResponse` (the HTTP envelope wrapper) stays in the client — it references `MossTopologyEntry` instead:

```csharp
private sealed record TopologyApiResponse
{
    [JsonPropertyName("data")]
    public List<MossTopologyEntry>? Data { get; init; }
}
```

This keeps a single deserialization model for the Moss topology schema, whether it arrives via HTTP (wrapped in `{"data": [...]}`) or via file (bare array).

### 4. Topology file path resolution

Add a static method to `StoneRosterPathResolver` — it already encapsulates path logic and knows the directory. The resolver checks multiple locations because Moss writes to a system-wide data directory, while the client's roster lives in a per-project `.Koan/` directory. In containers, both files are co-located via bind mount.

**File**: `src/Koan.ZenGarden/Persistence/StoneRosterPathResolver.cs`

Resolution chain (returns the first path where the file exists, or the roster-adjacent default):

1. **Co-located with roster** — same directory as `garden-stones.json` (container mount or explicit config)
2. **`GARDEN_DATA_DIR` env var** — `{GARDEN_DATA_DIR}/topology/garden-topology.json` (Moss convention, overridable)
3. **System-wide Zen Garden data directory** (platform-specific, stable for services):
   - Linux/macOS: `/var/lib/zen-garden/topology/garden-topology.json`
   - Windows: `C:\ProgramData\zen-garden\topology\garden-topology.json`
4. **Default**: roster-adjacent path (file may appear later via mount injection)

In `ZenGardenClient`, the topology path is resolved once during construction using this method.

### 5. Secondary seed from garden-topology.json

On first resolution, after seeding from the client's own roster, also read `garden-topology.json` if present. This provides pre-warmed topology for containers that have never connected to Moss.

**File**: `src/Koan.ZenGarden/ZenGardenClient.cs`

Modify `SeedFromPersistedRosterAsync()` to add a secondary seed step after the existing roster load:

```csharp
// After existing roster seeding...

// Secondary seed: read Moss-authored topology if present
await SeedFromMossTopologyAsync(ct).ConfigureAwait(false);
```

New private method:

```csharp
private async Task SeedFromMossTopologyAsync(CancellationToken ct)
{
    try
    {
        if (_mossTopologyPath is null || !File.Exists(_mossTopologyPath))
            return;

        var json = await File.ReadAllTextAsync(_mossTopologyPath, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
            return;

        // File is a bare JSON array — NOT the HTTP API envelope.
        // The HTTP path unwraps {"data": [...]}, but the file is just [...].
        var entries = JsonSerializer.Deserialize<List<MossTopologyEntry>>(json, TopologySerializerOptions);
        if (entries is null || entries.Count == 0)
            return;

        var seeded = 0;
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Endpoint))
                continue;

            var cacheKey = !string.IsNullOrWhiteSpace(entry.StoneId)
                ? entry.StoneId
                : entry.StoneName;

            if (string.IsNullOrWhiteSpace(cacheKey))
                continue;

            // Only add if not already present from own roster (own roster wins)
            if (_stoneCache.ContainsKey(cacheKey))
                continue;

            // Refresh LastSeenUtc to now — same policy as own-roster seeding.
            // The file may be hours old if Moss is down, but the data is the best
            // available truth. Let active hydration update to real timestamps later.
            var stone = new CachedMossStone
            {
                Endpoint = entry.Endpoint,
                StoneId = entry.StoneId,
                StoneName = entry.StoneName ?? cacheKey,
                MossVersion = entry.MossVersion,
                LastSeenUtc = now
            };

            if (_stoneCache.TryAdd(cacheKey, stone))
            {
                seeded++;

                // Index by name if StoneId was the primary key
                if (!string.IsNullOrWhiteSpace(entry.StoneId)
                    && !string.IsNullOrWhiteSpace(entry.StoneName))
                {
                    _stoneCache.TryAdd(entry.StoneName, stone);
                }

                // Also cache .local variant for mDNS resolution parity.
                // Active hydration already does this — file-based seed should too,
                // so host-network lookups find file-seeded stones by .local name.
                if (!string.IsNullOrWhiteSpace(entry.StoneName))
                {
                    var localKey = $"{entry.StoneName}.local";
                    var localEndpoint = $"http://{entry.StoneName}.local:{Constants.Moss.DefaultPort}";
                    var localStone = stone with { Endpoint = localEndpoint };
                    _stoneCache.TryAdd(localKey, localStone);
                }
            }
        }

        if (seeded > 0)
        {
            _logger.LogDebug(
                "Seeded {Count} stones from Moss topology file into in-memory cache",
                seeded);
        }
    }
    catch (Exception ex)
    {
        _logger.LogDebug(ex, "Could not seed from Moss topology file (non-fatal)");
    }
}
```

**Key design choices in this method:**

- **`LastSeenUtc = now`**: Refreshes to current time, same as own-roster seeding. Prevents stale file timestamps from causing immediate cache expiry. Active hydration and SSE will update to real values once Moss is reachable.
- **`.local` dual-cache**: Active hydration caches both IP-based and `.local` endpoints. File-based seed does the same for consistency, so selectors like `stone-coral-prairie.local` work against file-seeded entries.
- **Bare array deserialization**: The file is `TopologyEntry[]` directly — no `{"data": [...]}` envelope. The HTTP path uses `TopologyApiResponse` to unwrap the API envelope; the file path skips that layer entirely.

### 6. Serializer options

Add a static instance for reading Moss's format:

```csharp
private static readonly JsonSerializerOptions TopologySerializerOptions = new()
{
    PropertyNameCaseInsensitive = true
};
```

This is separate from `StoneRosterStore.SerializerOptions` (which adds `WriteIndented` and `WhenWritingNull` for the client's own file writes).

## What does NOT change

- `StoneRosterStore.PersistAsync()` — merge-on-write logic is untouched
- `StoneRosterPathResolver.Resolve()` — resolution chain unchanged (same directory, different filename)
- Active topology hydration via `GET /api/v1/garden/topology` — continues as-is
- SSE stream consumption — remains the primary real-time path
- Passive topology enrichment from tool events — untouched
- Container host binding (`host.docker.internal`) — untouched
- UDP discovery fallback — untouched
- All subscription, capability, and wish logic — untouched

## Seeding priority

After implementation, the cold-start seeding order is:

```
1. Own roster (garden-stones.json)     ← Client's operational knowledge, wins on key conflict
2. Moss topology (garden-topology.json) ← Fills gaps from Moss's mesh view, timestamps refreshed to now
3. Active hydration (HTTP)              ← Live fetch on bind, refreshes everything
4. SSE stream (real-time)               ← Continuous updates after connection
```

The first two are file reads (< 1ms). Steps 3-4 require Moss to be reachable.

## Files to modify

| File | Change |
|------|--------|
| `src/Koan.ZenGarden/Constants.cs` | Rename `RosterFileName`, add `MossTopologyFileName` |
| `src/Koan.ZenGarden/Persistence/StoneRosterStore.cs` | Add `MigrateLegacyFilename()`, call in `LoadAsync()` |
| `src/Koan.ZenGarden/Persistence/StoneRosterPathResolver.cs` | Add `ResolveMossTopologyPath()` static method |
| `src/Koan.ZenGarden/Persistence/MossTopologyEntry.cs` | **New file** — extracted from `TopologyEntryResponse`, `internal` visibility |
| `src/Koan.ZenGarden/ZenGardenClient.cs` | Remove private `TopologyEntryResponse`, use `MossTopologyEntry`. Add `SeedFromMossTopologyAsync()`, call from `SeedFromPersistedRosterAsync()`. Store resolved `_mossTopologyPath`. Update `TopologyApiResponse` to reference `MossTopologyEntry`. |
| `src/Koan.ZenGarden/TECHNICAL.md` | Update "Persistent Stone Roster" section: new filename, secondary seed, file format distinction |
| `src/Koan.ZenGarden/README.md` | Update if `stones.json` is mentioned |

## Testing

### Unit tests

1. **Migration**: Verify `stones.json` is renamed to `garden-stones.json` on first `LoadAsync()`.
2. **Migration no-op**: Verify no error when neither file exists.
3. **Migration skip**: Verify no rename when `garden-stones.json` already exists (even if `stones.json` also exists — the new file is canonical, legacy is ignored).
4. **Topology seed**: Given a `garden-topology.json` with 3 entries and an empty roster, verify all 3 are seeded into in-memory cache with `LastSeenUtc` refreshed to approximately now (not the file's stale timestamp).
5. **Roster priority**: Given a `garden-topology.json` and a `garden-stones.json` that both contain the same stone (by `CacheKey`), verify the roster entry wins.
6. **Partial topology**: Verify entries with missing `Endpoint` are skipped. Verify entries with missing `StoneId` use `StoneName` as cache key.
7. **Malformed topology**: Verify a corrupt or empty `garden-topology.json` logs a debug message and doesn't prevent seeding from own roster.
8. **Dual-cache .local**: Verify file-seeded entries produce `.local` secondary cache entries.

### Integration tests

The existing `ZenGardenFixture` connects to a live Moss. After this change:

- Verify `garden-stones.json` is created (not `stones.json`) after a successful bind
- If a `garden-topology.json` exists on the test host, verify it is read during seeding

## Moss-side file contract

**IMPORTANT: The file is a bare JSON array — NOT the HTTP API envelope.**

The HTTP API (`GET /api/v1/garden/topology`) returns:
```json
{ "data": [ ... ], "suggestions": [ ... ] }
```

The file (`garden-topology.json`) contains only the inner array:
```json
[
  {
    "stone_id": "019abc12-...",
    "stone_name": "stone-coral-prairie",
    "endpoint": "http://192.168.1.50:7185",
    "moss_version": "0.9.1",
    "services": [
      { "name": "mongodb", "service_type": "mongodb", "status": "running" }
    ],
    "mac": "aa:bb:cc:dd:ee:ff",
    "health": "thriving",
    "capabilities": null,
    "status": "online",
    "discovered_at": "2026-02-07T12:00:00Z",
    "last_seen": "2026-02-07T12:00:30Z",
    "tags": []
  }
]
```

The HTTP path uses `TopologyApiResponse` to unwrap the envelope, then processes `List<MossTopologyEntry>`. The file path deserializes `List<MossTopologyEntry>` directly. Same model, different entry point.

The client only extracts: `stone_id`, `stone_name`, `endpoint`, `moss_version`, `last_seen`. All other fields (`services`, `mac`, `health`, `capabilities`, `discovered_at`, `tags`) are present in the JSON but ignored by `MossTopologyEntry`. This loose coupling means Moss can evolve its schema without breaking the client's seed path.

## References

- `zen-garden/docs/decisions/TOPO-0002-shared-topology-directory.md` — Architectural decision (cross-repo)
- `src/Koan.ZenGarden/TECHNICAL.md` — Current technical reference
- `src/Koan.ZenGarden/Persistence/StoneRosterStore.cs` — Current roster persistence
- `src/Koan.ZenGarden/Persistence/StoneRosterPathResolver.cs` — Container path resolution
- `src/Koan.ZenGarden/Constants.cs` — Current constants
- `src/Koan.ZenGarden/ZenGardenClient.cs:3074-3099` — Existing TopologyEntryResponse to extract
