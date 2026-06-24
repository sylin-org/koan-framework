using Koan.Data.Abstractions.Pipeline;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Koan.Data.Relational;

/// <summary>
/// The Serialize-stage hook (DATA-0105 §3b, Seam 2) that injects <b>managed fields</b> into the persisted JSON
/// envelope of the relational trio (SQLite / Postgres / SqlServer). It appends a synthetic <see cref="JsonProperty"/>
/// per managed field applicable to the entity type; the property reads its value from
/// <see cref="ManagedFieldWriteScope.Current"/> (the per-op snapshot the chokepoint set), so a write under a
/// managed scope emits e.g. <c>"__koan_tenant":"acme"</c> alongside the entity's real properties — with no change
/// to any adapter's write SQL.
///
/// <para><b>Off = byte-identical:</b> when no module registers (<see cref="ManagedFieldRegistry.IsEmpty"/>) or no
/// scope is active, no synthetic property is emitted, so the serialized form is identical to before. The literal
/// <see cref="JsonProperty.PropertyName"/> is set directly (not re-processed by the naming strategy on write); the
/// leading-underscore convention keeps the read-side camel-cased leaf identical too.</para>
///
/// <para>One class, configured per adapter via the inherited <see cref="DefaultContractResolver.NamingStrategy"/>
/// (PascalCase default for SQLite/PG; CamelCase for SqlServer) — two instances, not a shared singleton. On
/// deserialize the synthetic property is not writable, so the unknown stored key is ignored cleanly.</para>
/// </summary>
public sealed class ManagedFieldContractResolver : DefaultContractResolver
{
    protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
    {
        var props = base.CreateProperties(type, memberSerialization);
        if (ManagedFieldRegistry.IsEmpty) return props;          // off ⇒ byte-identical

        var managed = ManagedFieldRegistry.ForType(type);
        if (managed.Count == 0) return props;

        foreach (var d in managed)
        {
            var name = d.StorageName;
            if (props.Any(p => string.Equals(p.PropertyName, name, StringComparison.Ordinal)))
                continue;   // a real property already owns the name (managed names lead with '_', so this is defensive)

            props.Add(new JsonProperty
            {
                PropertyName = name,
                UnderlyingName = name,
                PropertyType = typeof(object),
                DeclaringType = type,
                Readable = true,
                Writable = false,            // serialize-only; on read the stored key is ignored
                ValueProvider = new ScopeValueProvider(name),
                ShouldSerialize = _ =>
                    ManagedFieldWriteScope.Effective is { } s && s.TryGetValue(name, out var v) && v is not null,
            });
        }
        return props;
    }

    /// <summary>
    /// Reads a managed field's value for the current write from the per-op scope snapshot. Reads <b>Effective</b>
    /// (guarded isolation values ∪ unguarded operation overrides, ARCH-0101 §4) so a soft-delete <c>__deleted=true</c>
    /// state stamp is persisted alongside the isolation stamp — the conflict guard (adapter-side) still reads only
    /// <c>Current</c>, so the override is injected but never wrongly guarded.
    /// </summary>
    private sealed class ScopeValueProvider(string name) : IValueProvider
    {
        public object? GetValue(object target)
            => ManagedFieldWriteScope.Effective is { } s && s.TryGetValue(name, out var v) ? v : null;

        public void SetValue(object target, object? value) { /* not writable — the stored key is ignored on read */ }
    }
}
