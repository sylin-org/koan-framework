using System;
using System.Collections.Generic;
using System.Linq;
using Koan.Data.Abstractions.Pipeline;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Koan.Data.Core;

/// <summary>
/// The one shared managed-field ⇄ JSON bridge (ARCH-0103 §5, §9 — lifted from the relational trio's
/// <c>ManagedFieldContractResolver</c>). It centralises the knowledge of which invisible <c>__</c>-keys a type carries
/// (<see cref="ManagedFieldRegistry"/>) and how they ride a serialized record, so both JSON-serializing storage families
/// stamp the framework-managed discriminator the same way instead of each inventing its own:
/// <list type="bullet">
/// <item><b>The relational trio</b> (SQLite / Postgres / SqlServer) uses it as a <see cref="DefaultContractResolver"/>:
/// it injects each applicable managed field into the entity's persisted JSON column from the ambient write scope
/// (<see cref="ManagedFieldWriteScope.Effective"/>); the database then extracts them via <c>json_extract</c>.</item>
/// <item><b>The <c>KeyValueStore</c> JSON-text family</b> (Json / Redis) uses the static <see cref="InjectManaged"/> /
/// <see cref="ExtractManaged"/> pair: it merges an explicit <b>per-record</b> managed dictionary into / out of the
/// serialized value (the record carries its own stamped values, decoupled from the ambient scope at persist time), and
/// the family's in-memory hybrid read-filter then matches it.</item>
/// </list>
///
/// <para><b>Off ⇒ byte-identical:</b> when no module registers a managed field (<see cref="ManagedFieldRegistry.IsEmpty"/>)
/// every path short-circuits — the contract resolver emits no synthetic property, <see cref="InjectManaged"/> adds nothing,
/// and <see cref="ExtractManaged"/> returns <c>null</c> — so the serialized form is exactly the pre-rebuild bytes. The
/// managed keys lead with <c>'_'</c> (a fixed point of camel-casing) so the write literal and the read literal stay
/// identical on adapters that camel-case property names.</para>
/// </summary>
public sealed class ManagedFieldJsonInjector : DefaultContractResolver
{
    // ==================== Relational face (the Serialize-stage contract-resolver hook) ====================

    /// <summary>
    /// Appends one synthetic, serialize-only <see cref="JsonProperty"/> per managed field applicable to
    /// <paramref name="type"/>, reading its value from <see cref="ManagedFieldWriteScope.Effective"/>. Configured per
    /// adapter via the inherited <see cref="DefaultContractResolver.NamingStrategy"/> (PascalCase default for SQLite/PG;
    /// CamelCase for SqlServer). On deserialize the synthetic property is not writable, so the stored key is ignored.
    /// </summary>
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

    // ==================== KeyValueStore face (per-record explicit dictionary) ====================

    /// <summary>
    /// Merge a record's stamped managed values (the <c>KvRecord.Managed</c> snapshot taken at write time) onto its
    /// serialized JSON as top-level <c>__</c>-keys — the JSON-text family's write-stamp. Unlike the relational
    /// contract-resolver face this reads the values from the <paramref name="managed"/> dictionary, not the ambient
    /// scope, so persisting a whole store (Json rewrites its file each write) stamps each record with its OWN values.
    /// <para>Off / host-context (<c>null</c> or empty <paramref name="managed"/>) ⇒ no key added ⇒ byte-identical.</para>
    /// </summary>
    public static void InjectManaged(JObject json, IReadOnlyDictionary<string, object?>? managed)
    {
        if (managed is null || managed.Count == 0) return;
        foreach (var kv in managed)
            json[kv.Key] = kv.Value is null ? JValue.CreateNull() : JToken.FromObject(kv.Value);
    }

    /// <summary>
    /// Extract the <c>__</c>-keys declared for <paramref name="entityType"/> back out of a serialized record into a
    /// managed dictionary (so the family's hybrid evaluator and cross-scope write guard can read them). Returns
    /// <c>null</c> off-axis (<see cref="ManagedFieldRegistry.IsEmpty"/> or the type carries no managed field) — the
    /// byte-identical path. The unknown keys are left on the <see cref="JObject"/>; the entity deserialization ignores
    /// them (they are not POCO members), exactly as the relational read does.
    /// </summary>
    public static IReadOnlyDictionary<string, object?>? ExtractManaged(JObject json, Type entityType)
    {
        if (ManagedFieldRegistry.IsEmpty) return null;
        var managed = ManagedFieldRegistry.ForType(entityType);
        if (managed.Count == 0) return null;

        Dictionary<string, object?>? dict = null;
        foreach (var d in managed)
        {
            if (!json.TryGetValue(d.StorageName, out var tok)) continue;
            dict ??= new Dictionary<string, object?>(StringComparer.Ordinal);
            dict[d.StorageName] = tok.Type == JTokenType.Null ? null : tok.ToObject(d.ClrType);
        }
        return dict;
    }
}
