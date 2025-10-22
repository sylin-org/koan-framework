using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Koan.Cache.Abstractions.Policies;

public interface ICachePolicyRegistry
{
    IReadOnlyList<CachePolicyDescriptor> GetPoliciesFor(Type type);
    IReadOnlyList<CachePolicyDescriptor> GetPoliciesFor(MemberInfo member);
    IReadOnlyList<CachePolicyDescriptor> GetAllPolicies();
    bool TryGetPolicy(Type type, [NotNullWhen(true)] out CachePolicyDescriptor? descriptor);
    bool TryGetPolicy(MemberInfo member, [NotNullWhen(true)] out CachePolicyDescriptor? descriptor);
}
