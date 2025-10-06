using System;
using System.Collections.Generic;
using System.Reflection;
using Koan.Cache.Abstractions.Primitives;

namespace Koan.Cache.Abstractions.Policies;

public sealed record CachePolicyDescriptor(
    CacheScope Scope,
    string KeyTemplate,
    CacheStrategy Strategy,
    CacheConsistencyMode Consistency,
    TimeSpan? AbsoluteTtl,
    TimeSpan? SlidingTtl,
    TimeSpan? AllowStaleFor,
    bool ForcePublishInvalidation,
    IReadOnlyList<string> Tags,
    string? Region,
    string? ScopeId,
    IReadOnlyDictionary<string, string> Metadata,
    MemberInfo? TargetMember,
    Type? DeclaringType)
{
    public CacheEntryOptions ToOptions()
        => new()
        {
            AbsoluteTtl = AbsoluteTtl,
            SlidingTtl = SlidingTtl,
            AllowStaleFor = AllowStaleFor,
            ForcePublishInvalidation = ForcePublishInvalidation,
            Consistency = Consistency,
            Tags = new HashSet<string>(Tags, StringComparer.OrdinalIgnoreCase),
            Region = Region,
            ScopeId = ScopeId,
            Metadata = new Dictionary<string, string>(Metadata, StringComparer.Ordinal)
        };
}
