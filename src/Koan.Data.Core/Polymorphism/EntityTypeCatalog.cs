using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Koan.Core.Hosting.Registry;
using Koan.Data.Abstractions;

namespace Koan.Data.Core.Polymorphism;

/// <summary>Bounded, weakly keyed allowlist of Entity-family type identities.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public static class EntityTypeCatalog
{
    private static readonly ConditionalWeakTable<Type, Registration> Registrations = new();
    private static readonly ConditionalWeakTable<Type, RootCatalog> Roots = new();
    private static readonly Lazy<bool> DiscoveryLoaded = new(
        LoadDiscovered,
        LazyThreadSafetyMode.ExecutionAndPublication);

    public static string TypeId(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        return EnsureRegistered(entityType)
            ?? throw new InvalidOperationException(
                $"Entity type '{entityType.FullName}' must be a concrete, closed class before it can be persisted.");
    }

    public static Type Resolve(Type rootType, string storedId)
    {
        ArgumentNullException.ThrowIfNull(rootType);
        rootType = EntityRootDescriptor.For(rootType).RootType;
        if (string.IsNullOrWhiteSpace(storedId))
            throw new InvalidDataException("An Entity runtime-type identifier cannot be empty.");
        var root = Roots.GetValue(rootType, static _ => new RootCatalog());
        if (root.TryResolve(storedId, out var resolved)) return resolved;

        EnsureDiscovered();
        if (root.TryResolve(storedId, out resolved)) return resolved;

        root.EnsureReflectionFallback(rootType);
        if (root.TryResolve(storedId, out resolved)) return resolved;

        throw new InvalidDataException(
            $"Stored Entity type '{storedId}' is not a registered concrete variant of '{rootType.FullName}'. " +
            "Refuse to materialize it as the root because doing so could erase variant fields on the next save.");
    }

    public static void Register(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        _ = EnsureRegistered(entityType);
    }

    public static bool HasVariants(Type rootType)
    {
        ArgumentNullException.ThrowIfNull(rootType);
        rootType = EntityRootDescriptor.For(rootType).RootType;
        var root = Roots.GetValue(rootType, static _ => new RootCatalog());
        if (root.HasVariants) return true;
        EnsureDiscovered();
        return root.HasVariants;
    }

    private static string? EnsureRegistered(Type entityType) =>
        Registrations.GetValue(entityType, static type => new Registration(type)).Value;

    private static string? RegisterCore(Type entityType)
    {
        if (!entityType.IsClass || entityType.IsAbstract || entityType.ContainsGenericParameters) return null;
        var descriptor = EntityRootDescriptor.For(entityType);
        if (descriptor.IsVariant) _ = EnsureRegistered(descriptor.RootType);

        var assembly = entityType.Assembly.GetName().Name
            ?? throw new InvalidOperationException($"Entity assembly for '{entityType.FullName}' has no simple name.");
        var fullName = entityType.FullName
            ?? throw new InvalidOperationException("Anonymous Entity types cannot be persisted.");
        var id = $"{assembly}:{fullName}";
        Roots.GetValue(descriptor.RootType, static _ => new RootCatalog())
            .Register(id, entityType, descriptor.IsVariant);
        return id;
    }

    private static void EnsureDiscovered() => _ = DiscoveryLoaded.Value;

    private static bool LoadDiscovered()
    {
        foreach (var candidate in KoanRegistry.GetDiscoveredImplementors(typeof(IEntity))) TryRegister(candidate);
        return true;
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Dynamic/plugin compatibility fallback; generated Koan discovery is the trim-safe primary path.")]
    private static bool LoadReflectionFallback(Type rootType)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic) continue;
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException error)
            {
                types = error.Types.Where(static type => type is not null).Cast<Type>().ToArray();
            }
            catch { continue; }
            foreach (var candidate in types) TryRegisterForRoot(candidate, rootType);
        }
        return true;
    }

    private static void TryRegisterForRoot(Type candidate, Type rootType)
    {
        if (!candidate.IsClass || candidate.IsAbstract || candidate.ContainsGenericParameters ||
            !rootType.IsAssignableFrom(candidate)) return;
        EntityRootDescriptor descriptor;
        try { descriptor = EntityRootDescriptor.For(candidate); }
        catch (InvalidOperationException) { return; }
        if (descriptor.RootType == rootType) Register(candidate);
    }

    private static void TryRegister(Type candidate)
    {
        if (!candidate.IsClass || candidate.IsAbstract || candidate.ContainsGenericParameters) return;
        try { _ = EntityRootDescriptor.For(candidate); }
        catch (InvalidOperationException) { return; }
        Register(candidate);
    }

    private sealed class Registration
    {
        private readonly Lazy<string?> _value;
        public Registration(Type type) => _value = new Lazy<string?>(
            () => RegisterCore(type), LazyThreadSafetyMode.ExecutionAndPublication);
        public string? Value => _value.Value;
    }

    private sealed class RootCatalog
    {
        private readonly object _gate = new();
        private readonly ConcurrentDictionary<string, WeakReference<Type>> _types = new(StringComparer.Ordinal);
        private Lazy<bool>? _reflectionFallback;
        private int _hasVariants;
        public bool HasVariants => Volatile.Read(ref _hasVariants) != 0;

        public void Register(string id, Type type, bool variant)
        {
            lock (_gate)
            {
                if (_types.TryGetValue(id, out var reference))
                {
                    if (!reference.TryGetTarget(out var existing)) _types[id] = new WeakReference<Type>(type);
                    else
                    if (existing != type)
                        throw new InvalidOperationException(
                            $"Entity type identifier '{id}' is shared by '{existing.AssemblyQualifiedName}' and " +
                            $"'{type.AssemblyQualifiedName}'. Use unique assembly-simple and full type names.");
                }
                else
                {
                    RemoveCollected();
                    if (_types.Count >= Infrastructure.Constants.Defaults.EntityTypesPerRoot)
                        throw new InvalidOperationException(
                            $"Entity root '{EntityRootDescriptor.For(type).RootType.FullName}' exceeds the bounded family catalog of " +
                            $"{Infrastructure.Constants.Defaults.EntityTypesPerRoot} types. Split the family deliberately.");
                    _types.TryAdd(id, new WeakReference<Type>(type));
                }
            }
            if (variant) Volatile.Write(ref _hasVariants, 1);
        }

        private void RemoveCollected()
        {
            foreach (var pair in _types)
            {
                if (pair.Value.TryGetTarget(out _)) continue;
                ((ICollection<KeyValuePair<string, WeakReference<Type>>>)_types).Remove(pair);
            }
        }

        public bool TryResolve(string id, out Type type)
        {
            if (_types.TryGetValue(id, out var reference) && reference.TryGetTarget(out type!)) return true;
            type = null!;
            return false;
        }

        public void EnsureReflectionFallback(Type rootType)
        {
            var created = new Lazy<bool>(() => LoadReflectionFallback(rootType), LazyThreadSafetyMode.ExecutionAndPublication);
            _ = Interlocked.CompareExchange(ref _reflectionFallback, created, null)?.Value ?? created.Value;
        }
    }
}
