using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Koan.Cache.Abstractions.Policies;
using Microsoft.Extensions.Logging;
using System.IO;

namespace Koan.Cache.Policies;

internal sealed class CachePolicyRegistry : ICachePolicyRegistry
{
    private readonly ILogger<CachePolicyRegistry> _logger;
    private ImmutableDictionary<Type, ImmutableArray<CachePolicyDescriptor>> _typePolicies = ImmutableDictionary<Type, ImmutableArray<CachePolicyDescriptor>>.Empty;
    private ImmutableDictionary<MemberInfo, CachePolicyDescriptor> _memberPolicies = ImmutableDictionary<MemberInfo, CachePolicyDescriptor>.Empty;
    private ImmutableArray<CachePolicyDescriptor> _allPolicies = ImmutableArray<CachePolicyDescriptor>.Empty;

    public CachePolicyRegistry(ILogger<CachePolicyRegistry> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<CachePolicyDescriptor> GetPoliciesFor(Type type)
        => _typePolicies.TryGetValue(type, out var value) ? value : ImmutableArray<CachePolicyDescriptor>.Empty;

    public IReadOnlyList<CachePolicyDescriptor> GetPoliciesFor(MemberInfo member)
        => _memberPolicies.TryGetValue(member, out var value)
            ? new[] { value }
            : Array.Empty<CachePolicyDescriptor>();

    public IReadOnlyList<CachePolicyDescriptor> GetAllPolicies()
        => _allPolicies;

    public bool TryGetPolicy(Type type, [NotNullWhen(true)] out CachePolicyDescriptor? descriptor)
    {
        if (_typePolicies.TryGetValue(type, out var descriptors) && descriptors.Length > 0)
        {
            descriptor = descriptors[0];
            return true;
        }

        descriptor = null;
        return false;
    }

    public bool TryGetPolicy(MemberInfo member, [NotNullWhen(true)] out CachePolicyDescriptor? descriptor)
        => _memberPolicies.TryGetValue(member, out descriptor);

    [RequiresUnreferencedCode("Cache policy registry rebuild reflects assemblies for cache policy attributes.")]
    public void Rebuild(IEnumerable<Assembly> assemblies)
    {
        if (assemblies is null)
        {
            throw new ArgumentNullException(nameof(assemblies));
        }

        var typeBuilder = ImmutableDictionary.CreateBuilder<Type, ImmutableArray<CachePolicyDescriptor>>();
        var memberBuilder = ImmutableDictionary.CreateBuilder<MemberInfo, CachePolicyDescriptor>();
        var allBuilder = ImmutableArray.CreateBuilder<CachePolicyDescriptor>();

        foreach (var assembly in assemblies)
        {
            if (assembly.IsDynamic)
            {
                continue;
            }

            IEnumerable<Type> exportedTypes;
            try
            {
                exportedTypes = assembly.GetExportedTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                exportedTypes = ex.Types.OfType<Type>();
            }

            foreach (var type in exportedTypes)
            {
                if (type is null)
                {
                    continue;
                }

                var typeAttributes = GetCachePolicyAttributes(type, type.FullName ?? type.Name ?? type.ToString()).ToArray();
                if (typeAttributes.Length > 0)
                {
                    var descriptors = typeAttributes.Select(attr => CreateDescriptor(attr, null, type)).ToImmutableArray();
                    if (descriptors.Length > 0)
                    {
                        typeBuilder[type] = descriptors;
                        allBuilder.AddRange(descriptors);
                    }
                }

                foreach (var member in type.GetMembers(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    var memberAttributes = GetCachePolicyAttributes(member, $"{type.FullName ?? type.Name}.{member.Name}").ToArray();
                    if (memberAttributes.Length == 0)
                    {
                        continue;
                    }

                    // Most members expected to have a single policy.
                    var descriptor = CreateDescriptor(memberAttributes[0], member, type);
                    memberBuilder[member] = descriptor;
                    allBuilder.Add(descriptor);
                }
            }
        }

        _typePolicies = typeBuilder.ToImmutable();
        _memberPolicies = memberBuilder.ToImmutable();
        _allPolicies = allBuilder.ToImmutable();

        _logger.LogInformation("Cache policy registry rebuilt. {TypePolicyCount} type policies, {MemberPolicyCount} member policies.",
            _typePolicies.Count, _memberPolicies.Count);
    }

    private IEnumerable<CachePolicyAttribute> GetCachePolicyAttributes(MemberInfo target, string targetName)
    {
        try
        {
            return target.GetCustomAttributes(typeof(CachePolicyAttribute), inherit: true)
                .OfType<CachePolicyAttribute>();
        }
        catch (Exception ex) when (ex is TypeLoadException or ReflectionTypeLoadException or FileNotFoundException or FileLoadException or BadImageFormatException)
        {
            _logger.LogWarning(ex, "Skipping cache policy attributes for {Target} due to reflection failure: {Message}", targetName, ex.Message);
            return Array.Empty<CachePolicyAttribute>();
        }
    }

    private static CachePolicyDescriptor CreateDescriptor(CachePolicyAttribute attribute, MemberInfo? member, Type? declaringType)
    {
        var tags = attribute.Tags is null || attribute.Tags.Length == 0
            ? Array.Empty<string>()
            : attribute.Tags.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var metadata = attribute.Metadata is null || attribute.Metadata.Count == 0
            ? new Dictionary<string, string>()
            : attribute.Metadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal);

        return new CachePolicyDescriptor(
            attribute.Scope,
            attribute.KeyTemplate,
            attribute.Strategy,
            attribute.Consistency,
            attribute.AbsoluteTtl,
            attribute.SlidingTtl,
            attribute.AllowStaleFor,
            attribute.ForcePublishInvalidation,
            tags,
            attribute.Region,
            attribute.ScopeId,
            metadata,
            member,
            declaringType);
    }
}
