using System.Collections.Concurrent;
using System.Linq;

namespace Koan.Data.Abstractions.Pipeline;

/// <summary>
/// The boot-time index of <see cref="ManagedFieldDescriptor"/>s (DATA-0105 §4). A cross-cutting module
/// registers its descriptors from its <c>KoanAutoRegistrar.Initialize</c> (Reference = Intent); the data core,
/// the relational serializer, and the cache key read them generically.
///
/// <para>Deliberate static index (not DI): the relational Serialize-stage contract resolver needs static reach
/// deep in adapter serialization where no DI scope exists. Documented as a declared deviation in DATA-0105 §4.</para>
///
/// <para><b>Off = structurally absent:</b> when no module registers, <see cref="IsEmpty"/> is <c>true</c> and
/// every consumer short-circuits to its byte-identical pre-change path. Registration is a boot-only operation
/// (registrars run before any data op); each registration invalidates the per-type memo.</para>
/// </summary>
public static class ManagedFieldRegistry
{
    private static readonly object _gate = new();
    private static readonly List<ManagedFieldDescriptor> _descriptors = new();
    private static readonly ConcurrentDictionary<Type, ManagedFieldDescriptor[]> _byType = new();
    private static readonly ConcurrentDictionary<Type, ManagedFieldDescriptor[]> _equalityByType = new();
    private static volatile bool _isEmpty = true;

    /// <summary>Whether no managed field is registered — the hot-path off gate. Cheap volatile read.</summary>
    public static bool IsEmpty => _isEmpty;

    /// <summary>Every registered descriptor, priority-ordered (e.g. for the cache-key axis and the boot report).</summary>
    public static IReadOnlyList<ManagedFieldDescriptor> All
    {
        get { lock (_gate) return _descriptors.OrderBy(d => d.Priority).ToArray(); }
    }

    /// <summary>
    /// Register a managed field. Boot-only. Idempotent by <see cref="ManagedFieldDescriptor.StorageName"/>
    /// (a duplicate is a no-op, so a re-entrant Reference = Intent registrar is safe). Fails closed if the
    /// storage name is not camel-case-stable (see <see cref="ValidateStorageName"/>).
    /// </summary>
    public static void Register(ManagedFieldDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ValidateStorageName(descriptor.StorageName);
        lock (_gate)
        {
            if (_descriptors.Any(d => string.Equals(d.StorageName, descriptor.StorageName, StringComparison.Ordinal)))
                return;
            _descriptors.Add(descriptor);
            _byType.Clear();        // invalidate the per-type memos (boot-only ⇒ rare)
            _equalityByType.Clear();
            _isEmpty = false;
        }
    }

    /// <summary>The managed fields applicable to <paramref name="entityType"/>, Type-plane memoized.</summary>
    public static IReadOnlyList<ManagedFieldDescriptor> ForType(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        if (_isEmpty) return Array.Empty<ManagedFieldDescriptor>();
        return _byType.GetOrAdd(entityType, static t =>
        {
            ManagedFieldDescriptor[] snapshot;
            lock (_gate) snapshot = _descriptors.ToArray();
            // Stable order by explicit priority (DATA-0105 §3); ties keep registration order. Moot for the single
            // tenant field today, deterministic once a second managed field exists.
            return snapshot.Where(d => d.AppliesTo(t)).OrderBy(d => d.Priority).ToArray();
        });
    }

    /// <summary>
    /// The ONE equality-axis selection (DATA-0106): the managed fields applicable to <paramref name="entityType"/>
    /// that opt into the auto equality read-filter (<see cref="ManagedFieldDescriptor.AutoReadFilter"/> == true).
    /// This is the single source for "which axes are an equality scope" — consumed by the read-filter contributor
    /// (a <c>Filter.Eq</c>), the cache-key segment, and the storage-key particle alike, so no plane re-derives the
    /// selection. Type-plane memoized; off ⇒ empty.
    /// </summary>
    public static IReadOnlyList<ManagedFieldDescriptor> EqualityFields(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        if (_isEmpty) return Array.Empty<ManagedFieldDescriptor>();
        return _equalityByType.GetOrAdd(entityType, static t =>
            ((IEnumerable<ManagedFieldDescriptor>)ForType(t)).Where(d => d.AutoReadFilter).ToArray());
    }

    /// <summary>
    /// A storage name must be a fixed point of camel-case naming so the write literal and a camel-cased read
    /// leaf (SqlServer applies <c>CamelCaseNamingStrategy</c> to every filter leaf) are identical. In practice:
    /// lead with <c>'_'</c> or contain no uppercase letters. Fails closed at boot otherwise.
    /// </summary>
    public static void ValidateStorageName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A managed field StorageName must be a non-empty value.", nameof(name));
        var stable = name[0] == '_' || !name.Any(char.IsUpper);
        if (!stable)
            throw new ArgumentException(
                $"Managed field StorageName '{name}' is not camel-case-stable. It must lead with '_' or contain no " +
                "uppercase letters, so the write literal and a camel-cased read leaf stay identical across adapters.",
                nameof(name));
    }

    /// <summary>Test-support: clear all registrations and the memo (mirrors <c>TestHooks.ResetDataConfigs</c>).</summary>
    public static void Reset()
    {
        lock (_gate)
        {
            _descriptors.Clear();
            _byType.Clear();
            _equalityByType.Clear();
            _isEmpty = true;
        }
    }
}
