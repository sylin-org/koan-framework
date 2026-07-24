using System.Collections.Concurrent;
using System.Reflection;
using Koan.Core.Hosting.Registry;
using Koan.Data.Abstractions;

namespace Koan.Data.Core.Polymorphism;

/// <summary>
/// Allowlisted Entity-family type identities. Stored identifiers are never passed to CLR type loading APIs.
/// </summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public static class EntityTypeCatalog
{
    private static readonly ConcurrentDictionary<Type, Lazy<string?>> Registrations = new();
    private static readonly ConcurrentDictionary<Type, ConcurrentDictionary<string, Type>> TypesByRoot = new();
    private static readonly ConcurrentDictionary<Type, bool> FamilyStatus = new();
    private static readonly Lazy<bool> DiscoveryLoaded = new(
        LoadDiscovered,
        LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly ConcurrentDictionary<Type, Lazy<bool>> ReflectionFallbackLoaded = new();

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
        {
            throw new InvalidDataException("An Entity runtime-type identifier cannot be empty.");
        }

        if (TypesByRoot.TryGetValue(rootType, out var types) &&
            types.TryGetValue(storedId, out var resolved))
        {
            return resolved;
        }

        EnsureDiscovered();
        if (TypesByRoot.TryGetValue(rootType, out types) &&
            types.TryGetValue(storedId, out resolved))
        {
            return resolved;
        }

        // Runtime fallback for assemblies loaded after the generated manifest. It runs at most once per root,
        // remains outside the materialization hot path, and is primarily a dynamic/plugin compatibility seam.
        EnsureReflectionFallback(rootType);
        if (TypesByRoot.TryGetValue(rootType, out types) &&
            types.TryGetValue(storedId, out resolved))
        {
            return resolved;
        }

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
        if (FamilyStatus.TryGetValue(rootType, out var hasVariants))
        {
            return hasVariants;
        }

        EnsureDiscovered();
        if (FamilyStatus.TryGetValue(rootType, out hasVariants))
        {
            return hasVariants;
        }

        FamilyStatus.TryAdd(rootType, false);
        return false;
    }

    private static string? EnsureRegistered(Type entityType)
        => Registrations.GetOrAdd(
            entityType,
            static type => new Lazy<string?>(
                () => RegisterCore(type),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    private static string? RegisterCore(Type entityType)
    {
        if (!entityType.IsClass || entityType.IsAbstract || entityType.ContainsGenericParameters)
        {
            return null;
        }

        var descriptor = EntityRootDescriptor.For(entityType);
        if (descriptor.IsVariant)
        {
            // Family records include the root identity as well as variant identities. Registering any generated
            // variant therefore makes a root hint resolvable in a fresh process before the root is first written.
            _ = EnsureRegistered(descriptor.RootType);
        }

        var assembly = entityType.Assembly.GetName().Name
            ?? throw new InvalidOperationException($"Entity assembly for '{entityType.FullName}' has no simple name.");
        var fullName = entityType.FullName
            ?? throw new InvalidOperationException("Anonymous Entity types cannot be persisted.");
        var id = $"{assembly}:{fullName}";

        var map = TypesByRoot.GetOrAdd(
            descriptor.RootType,
            static _ => new ConcurrentDictionary<string, Type>(StringComparer.Ordinal));

        if (!map.TryAdd(id, entityType) &&
            map.TryGetValue(id, out var existing) &&
            existing != entityType)
        {
            throw new InvalidOperationException(
                $"Entity type identifier '{id}' is shared by '{existing.AssemblyQualifiedName}' and " +
                $"'{entityType.AssemblyQualifiedName}'. Use unique assembly-simple and full type names.");
        }

        if (descriptor.IsVariant)
        {
            FamilyStatus[descriptor.RootType] = true;
        }
        return id;
    }

    private static void EnsureDiscovered()
        => _ = DiscoveryLoaded.Value;

    private static bool LoadDiscovered()
    {
        foreach (var candidate in KoanRegistry.GetDiscoveredImplementors(typeof(IEntity)))
        {
            TryRegister(candidate);
        }

        return true;
    }

    private static void EnsureReflectionFallback(Type rootType)
        => _ = ReflectionFallbackLoaded.GetOrAdd(
            rootType,
            static root => new Lazy<bool>(
                () => LoadReflectionFallback(root),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Dynamic/plugin compatibility fallback; generated Koan discovery is the trim-safe primary path.")]
    private static bool LoadReflectionFallback(Type rootType)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic)
            {
                continue;
            }

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(static type => type is not null).Cast<Type>().ToArray();
            }
            catch
            {
                continue;
            }

            foreach (var candidate in types)
            {
                TryRegisterForRoot(candidate, rootType);
            }
        }

        return true;
    }

    private static void TryRegisterForRoot(Type candidate, Type rootType)
    {
        if (!candidate.IsClass || candidate.IsAbstract || candidate.ContainsGenericParameters ||
            !rootType.IsAssignableFrom(candidate))
        {
            return;
        }

        EntityRootDescriptor descriptor;
        try
        {
            descriptor = EntityRootDescriptor.For(candidate);
        }
        catch (InvalidOperationException)
        {
            // Unrelated malformed Entity types receive their correction when selected. One application's
            // malformed type must not prevent another root catalog from starting.
            return;
        }

        if (descriptor.RootType == rootType)
        {
            Register(candidate);
        }
    }

    private static void TryRegister(Type candidate)
    {
        if (!candidate.IsClass || candidate.IsAbstract || candidate.ContainsGenericParameters)
        {
            return;
        }

        try
        {
            _ = EntityRootDescriptor.For(candidate);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        Register(candidate);
    }
}
