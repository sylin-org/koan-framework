# Flow materialization — policies and transformers

Contract (at a glance)
- Inputs: Ordered canonical ranges per reference (path → [values]).
- Outputs: Materialized values (path → value) and applied policies (path → policyName).
- Error modes: Unknown policy falls back to last with a warning. Misconfigured types are ignored with a warning. When a record-level transformer is configured, per-path policies are ignored for that model (warned once).
- Success: One value per path plus policy metadata stored in `flow.views.materialized`.

Precedence and resolution
- Record-level transformer (per model) — overrides everything for that model; single warning emitted.
- Per-path policy: `Sora:Flow:Materialization:PerPath["<Model>:<Path>"]`.
- Per-model default: `Sora:Flow:Materialization:PerModelDefaults["<Model>"]`.
- Global default: `Sora:Flow:Materialization:DefaultPolicy` (default: last).

Built-in property transformers (policy names)
- last: last non-empty value seen in order.
- first: first non-empty value seen in order.
- max: numeric max (non-numeric values are skipped). Invariant culture parsing.
- min: numeric min (non-numeric values are skipped).
- coalesce: first non-empty value across sources (alias to first).

Configuration (startup-only)

appsettings.json

{
  "Sora": {
    "Flow": {
      "Materialization": {
        "DefaultPolicy": "last",
        "PerModelDefaults": {
          "S8.Flow.Shared.Sensor": "coalesce"
        },
        "PerPath": {
          "S8.Flow.Shared.Sensor:reading.value": "max",
          "S8.Flow.Shared.Sensor:inventory.serial": "first"
        },
        "RecordTransformers": {
          // Model-level override (type must implement IRecordMaterializationTransformer)
          // "S8.Flow.Shared.Device": "MyApp.Flow.DeviceRecordTransformer, MyApp"
        },
        "PropertyTransformers": {
          // Register custom policy names → type (implements IPropertyMaterializationTransformer)
          // "latestByTimestamp": "MyApp.Flow.LatestByTimestampTransformer, MyApp"
        }
      }
    }
  }
}

Notes
- Canonical remains policy-free ranges. The engine consumes an ordered view internally, but canonical storage still de-duplicates for readability.
- Whitespace and nulls are treated as empty. Lineage is only recorded for non-empty values.
- Numeric min/max parse using InvariantCulture; non-numeric entries are ignored for these policies.
- The configuration is bound once at startup via options; no runtime reload in v1.

Developer hooks
- IPropertyMaterializationTransformer: invoked per-path with the ordered ranges and full canonical snapshot (read-only) for context.
- IRecordMaterializationTransformer: invoked once per model/reference; returns the full values/policies pair.
- To enable a custom policy name, register it under `PropertyTransformers`. To override a model end-to-end, use `RecordTransformers`.

See also
- Decisions: DATA-0063 (ranges as canonical; materialized as single-valued)
- Reference: Flow — Bindings and canonical IDs

## Custom transformers (concise examples)

Implement a custom property transformer

// MyApp.Flow.LatestByTimestampTransformer.cs
using System.Globalization;
using Sora.Flow.Core.Materialization;

public sealed class LatestByTimestampTransformer : IPropertyMaterializationTransformer
{
    // Expects sibling path '<base>.timestamp' with ISO-8601 or Unix epoch (ms)
    public string Name => "latestByTimestamp";

    public MaterializedDecision Transform(
        string model,
        string path,
        IReadOnlyList<string?> orderedValues,
        IReadOnlyDictionary<string, IReadOnlyList<string?>> canonical)
    {
        // Find a sibling timestamp list if present (same base path, different leaf)
        var lastDot = path.LastIndexOf('.');
        var basePath = lastDot > 0 ? path.Substring(0, lastDot) : path;
        var tsPath = basePath + ".timestamp";

        if (!canonical.TryGetValue(tsPath, out var timestamps) || timestamps.Count != orderedValues.Count)
        {
            // Fallback to last if no timestamps or count mismatch
            var value = orderedValues.LastOrDefault(v => !string.IsNullOrWhiteSpace(v));
            return new(value, Name);
        }

        var maxTs = long.MinValue;
        string? winner = null;
        for (var i = 0; i < orderedValues.Count; i++)
        {
            var v = orderedValues[i];
            if (string.IsNullOrWhiteSpace(v)) continue;
            var tsRaw = timestamps[i];
            if (string.IsNullOrWhiteSpace(tsRaw)) continue;

            // Try Unix epoch ms, then ISO-8601
            long ts;
            if (!long.TryParse(tsRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out ts))
            {
                if (DateTimeOffset.TryParse(tsRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
                    ts = dto.ToUnixTimeMilliseconds();
                else
                    continue;
            }

            if (ts >= maxTs)
            {
                maxTs = ts;
                winner = v;
            }
        }

        return new(winner, Name);
    }
}

Implement a custom record transformer (override whole model)

// MyApp.Flow.DeviceRecordTransformer.cs
using Sora.Flow.Core.Materialization;

public sealed class DeviceRecordTransformer : IRecordMaterializationTransformer
{
    public (IReadOnlyDictionary<string, string?> Values, IReadOnlyDictionary<string, string> Policies) Transform(
        string model,
        string referenceId,
        IReadOnlyDictionary<string, IReadOnlyList<string?>> canonical)
    {
        // Example: enforce coalesce for all properties on this model
        var values = new Dictionary<string, string?>();
        var policies = new Dictionary<string, string>();
        foreach (var (path, ranges) in canonical)
        {
            var value = ranges.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
            values[path] = value;
            policies[path] = "coalesce"; // reflect applied policy per path
        }
        return (values, policies);
    }
}

Register in configuration (type names are assembly-qualified or short if resolvable)

appsettings.json

{
  "Sora": {
    "Flow": {
      "Materialization": {
        "PropertyTransformers": {
          "latestByTimestamp": "MyApp.Flow.LatestByTimestampTransformer, MyApp"
        },
        "RecordTransformers": {
          "S8.Flow.Shared.Device": "MyApp.Flow.DeviceRecordTransformer, MyApp"
        }
      }
    }
  }
}

Notes
- Record transformers suppress per-path policies for that model (a single warning is logged at runtime).
- Keep transformers pure/deterministic; avoid I/O. If you must resolve services, use constructor injection and register in DI.
- For per-path policies, prefer property transformers; reserve record transformers for model-wide overrides.
