using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Koan.Cache.Abstractions.Policies;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Policies;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Koan.Cache.Tests;

public sealed class CachePolicyRegistryTests
{
    [Fact]
    public void Rebuild_PopulatesTypeAndMemberPolicies()
    {
        var registry = new CachePolicyRegistry(NullLogger<CachePolicyRegistry>.Instance);
        registry.Rebuild(new[] { typeof(PolicyTarget).Assembly });

        registry.TryGetPolicy(typeof(PolicyTarget), out var typeDescriptor).Should().BeTrue();
        typeDescriptor!.Tags.Should().BeEquivalentTo("alpha", "tenant:42");

        var method = typeof(PolicyTarget).GetMethod(nameof(PolicyTarget.LoadAsync), BindingFlags.Instance | BindingFlags.Public)!;
        registry.TryGetPolicy(method, out var memberDescriptor).Should().BeTrue();
        memberDescriptor!.KeyTemplate.Should().Be("policy:method:{arg}");

        registry.GetAllPolicies().Should().HaveCount(2);
    }

    [Fact]
    public void GetPoliciesFor_ReturnsEmptyWhenNotFound()
    {
        var registry = new CachePolicyRegistry(NullLogger<CachePolicyRegistry>.Instance);
        registry.Rebuild(Array.Empty<Assembly>());

        registry.GetPoliciesFor(typeof(CachePolicyRegistryTests)).Should().BeEmpty();
    }

}

[CachePolicy(CacheScope.Entity, "policy:{Id}", Tags = new[] { "alpha", "tenant:42", "alpha" })]
public sealed class PolicyTarget
{
    [CachePolicy(CacheScope.EntityQuery, "policy:method:{arg}")]
    public void LoadAsync() { }
}
