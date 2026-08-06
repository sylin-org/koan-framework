using System.Linq.Expressions;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.ComponentModel.DataAnnotations.Schema;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Mapping.Runtime;

namespace Koan.Data.Core;

/// <summary>Compiles and validates immutable mapping declarations before provider dispatch.</summary>
public static class MappingPlanCompiler
{
    public static MappingDescriptor Convention(string source, Type entityType, MappingConvention convention)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(entityType);
        ArgumentNullException.ThrowIfNull(convention);
        var key = entityType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .SingleOrDefault(static property => property.GetCustomAttribute<IdentifierAttribute>(inherit: true) is not null)
            ?? entityType.GetProperty("Id", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MappingCompilationException(source, entityType,
                "The managed mapping convention requires an Id property or one [Identifier] property.");
        var keyPath = MappingPath.Of(key.Name);
        var keyBinding = new MappingBindingDescriptor(
            $"Key:{keyPath}->{convention.KeyName}",
            keyPath,
            MappingRole.Key,
            key.PropertyType,
            new PhysicalPath(convention.KeyName),
            MappingValueShape.Scalar);
        var objectBinding = new MappingBindingDescriptor(
            $"Object:$->{convention.ObjectName}",
            MappingPath.Root,
            MappingRole.Object,
            entityType,
            new PhysicalPath(convention.ObjectName),
            MappingValueShape.Object);
        return new MappingDescriptor(
            source,
            entityType,
            convention.Container,
            new MappingIdentityDescriptor(keyPath, key.PropertyType, [keyBinding]),
            [keyBinding, objectBinding]);
    }

    public static MappingPlan Compile(MappingDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        try
        {
            var planId = Identify(descriptor);
            descriptor = ExpandStructuredBindings(descriptor);
            Validate(descriptor);
            var entityFactory = CompileFactory(
                descriptor.EntityType,
                descriptor.Bindings.Any(static binding =>
                    binding.LogicalPath.IsRoot && binding.Codec is IRootObjectMappingCodec { CanDecode: true }));
            var identityIds = descriptor.Identity.Parts.Select(static part => part.Id).ToHashSet(StringComparer.Ordinal);
            CompositeIdentityPlan? composite = null;
            var compiled = new List<MappingBindingPlan>(descriptor.Bindings.Count);

            if (descriptor.Identity.IsComposite)
            {
                var entityKey = MappingMemberAccess.Compile(descriptor.EntityType, descriptor.Identity.LogicalPath, requireWrite: true);
                var componentAccess = new MappingMemberAccess[descriptor.Identity.Parts.Count];
                var componentBindings = new MappingBindingPlan[descriptor.Identity.Parts.Count];
                for (var index = 0; index < descriptor.Identity.Parts.Count; index++)
                {
                    var part = descriptor.Identity.Parts[index];
                    var relative = MappingPath.Of(part.LogicalPath.Segments.Skip(descriptor.Identity.LogicalPath.Segments.Count).ToArray());
                    var access = MappingMemberAccess.Compile(descriptor.Identity.LogicalType, relative, requireWrite: false);
                    componentAccess[index] = access;
                    var localAccess = access;
                    componentBindings[index] = CompileBinding(
                        descriptor,
                        part,
                        entity =>
                        {
                            var key = entityKey.Get(entity);
                            return key is null ? null : localAccess.Get(key);
                        },
                        assign: null);
                    compiled.Add(componentBindings[index]);
                }
                composite = new CompositeIdentityPlan(entityKey, componentAccess, componentBindings, descriptor.Identity.LogicalType);
            }

            foreach (var binding in descriptor.Bindings)
            {
                if (descriptor.Identity.IsComposite && identityIds.Contains(binding.Id)) continue;
                if (binding.LogicalPath.IsRoot)
                {
                    compiled.Add(CompileBinding(descriptor, binding, static entity => entity, assign: null));
                    continue;
                }

                var canonical = binding.Authority == MappingAuthority.Canonical;
                var access = MappingMemberAccess.Compile(descriptor.EntityType, binding.LogicalPath, requireWrite: canonical);
                compiled.Add(CompileBinding(descriptor, binding, access.Get, canonical ? access.Set : null));
            }

            var ordered = descriptor.Bindings
                .Select(binding => compiled.Single(candidate => candidate.Id == binding.Id))
                .ToArray();
            var plan = new MappingPlan(planId, descriptor, ordered, entityFactory, composite);
            plan.InitializeIndexes(MappingIndexCompiler.Compile(plan));
            return plan;
        }
        catch (MappingCompilationException) { throw; }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or NotSupportedException)
        {
            throw new MappingCompilationException(descriptor.Source, descriptor.EntityType, error.Message);
        }
    }

    /// <summary>
    /// Computes the decision identity from the explicit declaration without reflecting over or compiling the entity.
    /// Execution preserves this identity after it expands structured bindings and compiles hot-path accessors.
    /// </summary>
    public static string Identify(MappingDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return StableId(descriptor);
    }

    private static MappingBindingPlan CompileBinding(
        MappingDescriptor descriptor,
        MappingBindingDescriptor binding,
        Func<object, object?> read,
        Action<object, object?>? assign)
    {
        StructuredValuePlan? structured = null;
        if (binding.Shape == MappingValueShape.Object && binding.Codec is null)
        {
            IReadOnlySet<string>? exclusions = null;
            if (binding.LogicalPath.IsRoot)
            {
                exclusions = descriptor.Identity.LogicalPath.Segments.Count == 0
                    ? new HashSet<string>(StringComparer.Ordinal)
                    : new HashSet<string>([descriptor.Identity.LogicalPath.Segments[0]], StringComparer.Ordinal);
            }
            structured = StructuredValuePlan.Compile(binding.LogicalType, exclusions);
        }
        return new MappingBindingPlan(binding, read, assign, structured);
    }

    private static void Validate(MappingDescriptor descriptor)
    {
        if (descriptor.Bindings.Count == 0)
            throw Error(descriptor, "Declare at least one binding.");
        if (descriptor.Identity.Parts.Count == 0)
            throw Error(descriptor, "Declare one complete Key.");
        if (descriptor.Identity.Parts.Any(part => part.Role != MappingRole.Key || !descriptor.Bindings.Any(binding => binding.Id == part.Id)))
            throw Error(descriptor, "Every identity part must be a Key binding in this descriptor.");

        var keyBindings = descriptor.Bindings.Where(static binding => binding.Role == MappingRole.Key).ToArray();
        if (keyBindings.Length != descriptor.Identity.Parts.Count)
            throw Error(descriptor, "Every Key binding must participate in the complete identity.");

        if (descriptor.Identity.IsComposite)
        {
            if (descriptor.Identity.Parts.Count < 2)
                throw Error(descriptor, "Composite identity requires at least two parts.");
            if (descriptor.Identity.Parts.Any(part => !descriptor.Identity.LogicalPath.IsPrefixOf(part.LogicalPath) ||
                                                     part.LogicalPath.Equals(descriptor.Identity.LogicalPath)))
                throw Error(descriptor, "Composite identity parts must be properties beneath the selected Key value.");
            if (descriptor.Identity.Parts.Any(part => Nullable.GetUnderlyingType(part.LogicalType) is not null))
                throw Error(descriptor, "Composite identity parts cannot use nullable value types.");
            if (descriptor.Identity.Parts.Any(static part => part.Generation == MappingGeneration.Provider))
                throw Error(descriptor, "Composite generated identity is not supported; declare one application-supplied complete key.");
        }
        else if (!descriptor.Identity.Parts[0].LogicalPath.Equals(descriptor.Identity.LogicalPath))
            throw Error(descriptor, "A single identity binding must locate the selected Key property directly.");

        var logical = new HashSet<MappingPath>();
        foreach (var binding in descriptor.Bindings)
        {
            if (!logical.Add(binding.LogicalPath))
                throw Error(descriptor, $"Logical path '{binding.LogicalPath}' has duplicate authority.");
            ValidateBinding(descriptor, binding);
        }

        var rootObject = descriptor.Bindings.SingleOrDefault(static binding =>
            binding.Role == MappingRole.Object && binding.LogicalPath.IsRoot);
        if (rootObject is not null && descriptor.Bindings.Any(binding =>
                binding.Role != MappingRole.Key && binding.Authority == MappingAuthority.Canonical && !ReferenceEquals(binding, rootObject)))
            throw Error(descriptor, "Root Object is the authority for all non-key values and cannot overlap Property bindings.");

        for (var leftIndex = 0; leftIndex < descriptor.Bindings.Count; leftIndex++)
        {
            var left = descriptor.Bindings[leftIndex];
            for (var rightIndex = leftIndex + 1; rightIndex < descriptor.Bindings.Count; rightIndex++)
            {
                var right = descriptor.Bindings[rightIndex];
                if ((left.Shape == MappingValueShape.Object && left.LogicalPath.IsPrefixOf(right.LogicalPath)) ||
                    (right.Shape == MappingValueShape.Object && right.LogicalPath.IsPrefixOf(left.LogicalPath)))
                {
                    var allowedRootKey = (left.LogicalPath.IsRoot && right.Role == MappingRole.Key) ||
                                         (right.LogicalPath.IsRoot && left.Role == MappingRole.Key);
                    var allowedDerived = left.Authority == MappingAuthority.Derived || right.Authority == MappingAuthority.Derived;
                    if (!allowedRootKey && !allowedDerived)
                        throw Error(descriptor, $"Logical paths '{left.LogicalPath}' and '{right.LogicalPath}' overlap writable authority.");
                }
                ValidatePhysicalPair(descriptor, left, right);
            }
        }
    }

    private static void ValidateBinding(MappingDescriptor descriptor, MappingBindingDescriptor binding)
    {
        var scalar = IsScalar(binding.LogicalType);
        if (binding.Shape == MappingValueShape.Scalar && !scalar && binding.Codec is null)
            throw Error(descriptor, $"Logical path '{binding.LogicalPath}' is complex; use Object or a scalar codec.");
        if (binding.Shape == MappingValueShape.Object && scalar && binding.Codec is null)
            throw Error(descriptor, $"Logical path '{binding.LogicalPath}' is scalar; use Name or Path.");
        if (binding.Shape == MappingValueShape.Object && binding.PhysicalPath.IsNested &&
            binding.Authority == MappingAuthority.Canonical)
            throw Error(descriptor, $"Object binding '{binding.LogicalPath}' must name one physical root value.");
        if (binding.Generation == MappingGeneration.Provider && binding.Direction == MappingDirection.ReadOnly)
        {
            // Legal and explicit: provider supplies the value and Koan hydrates it.
        }
        if (binding.Codec is not { } codec) return;
        if (codec.LogicalType != binding.LogicalType)
            throw Error(descriptor,
                $"Codec '{codec.Id}' logical type '{codec.LogicalType.FullName}' does not match '{binding.LogicalType.FullName}'.");
        if (!codec.CanDecode)
            throw Error(descriptor, $"Codec '{codec.Id}' must decode for hydration.");
        if (binding.Direction == MappingDirection.ReadWrite && !codec.CanEncode)
            throw Error(descriptor, $"Writable binding '{binding.LogicalPath}' requires symmetric codec '{codec.Id}'.");
        if (binding.Role == MappingRole.Key && !codec.CanEncode)
            throw Error(descriptor, $"Key codec '{codec.Id}' must encode lookup and write predicates.");
        if (binding.LogicalPath.IsRoot &&
            (codec is not IRootObjectMappingCodec rootCodec ||
             !rootCodec.ExcludedLogicalPaths.Contains(descriptor.Identity.LogicalPath)))
            throw Error(descriptor,
                "A root Object codec must prove that it excludes the independent Key from its authority.");
    }

    private static void ValidatePhysicalPair(
        MappingDescriptor descriptor,
        MappingBindingDescriptor left,
        MappingBindingDescriptor right)
    {
        if (!string.Equals(left.PhysicalPath.Name, right.PhysicalPath.Name, StringComparison.Ordinal)) return;
        if (left.PhysicalPath.Equals(right.PhysicalPath))
            throw Error(descriptor, $"Physical path '{left.PhysicalPath}' has duplicate authority.");
        var allowedObjectProjection =
            (left.Shape == MappingValueShape.Object && left.Authority == MappingAuthority.Canonical &&
             left.PhysicalPath.IsPrefixOf(right.PhysicalPath) && right.Authority == MappingAuthority.Derived) ||
            (right.Shape == MappingValueShape.Object && right.Authority == MappingAuthority.Canonical &&
             right.PhysicalPath.IsPrefixOf(left.PhysicalPath) && left.Authority == MappingAuthority.Derived);
        if (allowedObjectProjection) return;
        if (!left.PhysicalPath.IsNested || !right.PhysicalPath.IsNested ||
            left.PhysicalPath.IsPrefixOf(right.PhysicalPath) || right.PhysicalPath.IsPrefixOf(left.PhysicalPath))
            throw Error(descriptor, $"Physical paths '{left.PhysicalPath}' and '{right.PhysicalPath}' are ambiguous.");
    }

    private static MappingDescriptor ExpandStructuredBindings(MappingDescriptor descriptor)
    {
        var expanded = descriptor.Bindings.ToList();
        foreach (var objectBinding in descriptor.Bindings.Where(static binding =>
                     binding.Shape == MappingValueShape.Object &&
                     binding.Authority == MappingAuthority.Canonical &&
                     (binding.Codec is null || binding.Codec is IRootObjectMappingCodec)))
        {
            var visited = new HashSet<Type>();
            Expand(
                objectBinding.LogicalType,
                objectBinding.LogicalPath,
                objectBinding.PhysicalPath,
                descriptor.Identity.LogicalPath,
                objectBinding.LogicalPath.IsRoot,
                expanded,
                visited,
                depth: 0);
        }
        return expanded.Count == descriptor.Bindings.Count
            ? descriptor
            : new MappingDescriptor(descriptor.Source, descriptor.EntityType, descriptor.Container, descriptor.Identity, expanded);
    }

    private static void Expand(
        Type type,
        MappingPath logicalBase,
        PhysicalPath physicalBase,
        MappingPath identity,
        bool root,
        List<MappingBindingDescriptor> bindings,
        HashSet<Type> visited,
        int depth)
    {
        var effective = Nullable.GetUnderlyingType(type) ?? type;
        if (depth >= 32 || !visited.Add(effective)) return;
        foreach (var property in effective.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                     .Where(static property => property.GetIndexParameters().Length == 0 && property.GetMethod is not null)
                     .Where(static property => property.GetCustomAttribute<NotMappedAttribute>(inherit: true) is null &&
                                               property.GetCustomAttribute<IgnoreStorageAttribute>(inherit: true) is null)
                     .OrderBy(static property => property.MetadataToken))
        {
            var logical = logicalBase.Append(MappingPath.Of(property.Name));
            if (root && (logical.Equals(identity) || identity.IsPrefixOf(logical))) continue;
            var relative = logical.Segments.Skip(logicalBase.Segments.Count).ToArray();
            var physical = new PhysicalPath(
                physicalBase.Name,
                physicalBase.Segments.Concat(relative).ToArray());
            var shape = IsScalar(property.PropertyType) ? MappingValueShape.Scalar : MappingValueShape.Object;
            if (bindings.Any(binding => binding.LogicalPath.Equals(logical))) continue;
            if (shape == MappingValueShape.Object && IsCollection(property.PropertyType))
            {
                // Collection predicates are defined only over scalar elements. A collection of records,
                // dictionaries, transitions, or another object graph remains part of the authoritative root
                // object, but it is not a separately queryable physical path. Compiling a derived Object binding
                // for it incorrectly imposed mapped-object constructor rules on ordinary JSON values (for example
                // KeyValuePair<,> and positional records) even though the binding can never participate in
                // hydration or writes.
                var element = CollectionElementType(property.PropertyType);
                if (element is null || !IsScalar(element)) continue;
            }
            else if (shape == MappingValueShape.Object)
            {
                Expand(property.PropertyType, logical, physical, identity, root: false, bindings, visited, depth + 1);
                continue;
            }
            var id = $"Derived:{logical}->{physical}";
            bindings.Add(new MappingBindingDescriptor(
                id,
                logical,
                MappingRole.Property,
                property.PropertyType,
                physical,
                shape,
                MappingDirection.ReadOnly,
                MappingGeneration.Application,
                MappingAuthority.Derived));
        }
        visited.Remove(effective);
    }

    private static Func<object> CompileFactory(Type type, bool allowUninitialized)
    {
        if (type.IsAbstract || type.IsInterface)
            throw new InvalidOperationException($"Mapped entity type '{type.FullName}' must be concrete.");
        var ctor = type.GetConstructor(Type.EmptyTypes);
        if (ctor is null && allowUninitialized)
            return () => System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);
        if (ctor is null)
            throw new InvalidOperationException($"Mapped entity type '{type.FullName}' requires a public parameterless constructor.");
        return Expression.Lambda<Func<object>>(Expression.Convert(Expression.New(ctor), typeof(object))).Compile();
    }

    private static string StableId(MappingDescriptor descriptor)
    {
        var text = new StringBuilder(descriptor.Source).Append('|').Append(descriptor.EntityType.AssemblyQualifiedName)
            .Append('|').Append(descriptor.Container);
        foreach (var binding in descriptor.Bindings)
            text.Append('|').Append(binding.Id).Append('|').Append(binding.Shape).Append('|').Append(binding.Direction)
                .Append('|').Append(binding.Generation).Append('|').Append(binding.Authority).Append('|').Append(binding.Codec?.Id);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()));
        return $"map-v1-{Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant()}";
    }

    private static bool IsScalar(Type type)
    {
        var effective = Nullable.GetUnderlyingType(type) ?? type;
        return effective.IsPrimitive || effective.IsEnum || effective == typeof(string) || effective == typeof(decimal) ||
               effective == typeof(Guid) || effective == typeof(DateTime) || effective == typeof(DateTimeOffset) ||
               effective == typeof(DateOnly) || effective == typeof(TimeOnly) || effective == typeof(TimeSpan) ||
               effective == typeof(byte[]);
    }

    private static bool IsCollection(Type type)
    {
        var effective = Nullable.GetUnderlyingType(type) ?? type;
        return effective != typeof(string) && effective != typeof(byte[]) &&
               typeof(System.Collections.IEnumerable).IsAssignableFrom(effective);
    }

    private static Type? CollectionElementType(Type type)
    {
        var effective = Nullable.GetUnderlyingType(type) ?? type;
        if (effective.IsArray) return effective.GetElementType();
        if (effective.IsGenericType && effective.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            return effective.GetGenericArguments()[0];
        return effective.GetInterfaces()
            .FirstOrDefault(static candidate => candidate.IsGenericType &&
                                                candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            ?.GetGenericArguments()[0];
    }

    private static MappingCompilationException Error(MappingDescriptor descriptor, string correction) =>
        new(descriptor.Source, descriptor.EntityType, correction);
}
