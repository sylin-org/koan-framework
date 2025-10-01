using S8.Canon.Shared;
using Koan.Data.Core;
using Koan.Canon.Infrastructure;
using Koan.Canon.Model;

namespace S8.Canon.Api.Adapters;

internal sealed class KeyedAdapterHealthRegistry : IAdapterHealthRegistry
{
    // Computes a best-effort snapshot from recent Keyed stage records
    public IReadOnlyDictionary<string, AdapterHealth> Snapshot()
    {
        try
        {
            IReadOnlyList<StageRecord<Reading>> list;
            // Inspect up to 500 recent Reading records using a synchronous async-enumerator loop
            var buf = new List<StageRecord<Reading>>(capacity: 500);
            using (EntityContext.With(CanonSets.StageShort(CanonSets.Keyed)))
            {
                var e = StageRecord<Reading>.AllStream(batchSize: 200).GetAsyncEnumerator();
                try
                {
                    while (buf.Count < 500 && e.MoveNextAsync().AsTask().GetAwaiter().GetResult())
                    {
                        buf.Add(e.Current);
                    }
                }
                finally { try { e.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { } }
            }
            if (buf.Count == 0)
            {
                using (EntityContext.With(CanonSets.StageShort(CanonSets.Intake)))
                {
                    var e = StageRecord<Reading>.AllStream(batchSize: 200).GetAsyncEnumerator();
                    try
                    {
                        while (buf.Count < 500 && e.MoveNextAsync().AsTask().GetAwaiter().GetResult())
                        {
                            buf.Add(e.Current);
                        }
                    }
                    finally { try { e.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { } }
                }
            }
            list = buf;

            var now = DateTimeOffset.UtcNow;
            var map = list
                .GroupBy(r => string.IsNullOrWhiteSpace(r.SourceId) ? "unknown" : r.SourceId!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g =>
                    {
                        var last = g.MaxBy(x => x.OccurredAt);
                        var emitted = g.Count();
                        var lastAt = last?.OccurredAt ?? default;
                        var status = lastAt >= now.AddMinutes(-2) ? "active" : "idle";
                        return new AdapterHealth
                        {
                            Name = g.Key,
                            StartedAt = now.AddHours(-1),
                            LastEmitAt = lastAt,
                            Emitted = emitted,
                            Status = status
                        };
                    },
                    StringComparer.OrdinalIgnoreCase);
            return map;
        }
        catch
        {
            return new Dictionary<string, AdapterHealth>(StringComparer.OrdinalIgnoreCase);
        }
    }
}

