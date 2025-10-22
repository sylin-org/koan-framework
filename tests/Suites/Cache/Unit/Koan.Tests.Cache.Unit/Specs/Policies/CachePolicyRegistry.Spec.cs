using System.Reflection;
using Koan.Cache.Abstractions.Policies;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Policies;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace Koan.Tests.Cache.Unit.Specs.Policies;

public sealed class CachePolicyRegistrySpec
{
    private readonly ITestOutputHelper _output;

    public CachePolicyRegistrySpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public Task Rebuild_populates_type_and_member_policies()
        => Spec(nameof(Rebuild_populates_type_and_member_policies), () =>
        {
            var registry = new CachePolicyRegistry(NullLogger<CachePolicyRegistry>.Instance);
            registry.Rebuild(new[] { typeof(PolicyTarget).Assembly });

            registry.TryGetPolicy(typeof(PolicyTarget), out var typeDescriptor).Should().BeTrue();
            typeDescriptor!.Tags.Should().BeEquivalentTo("alpha", "tenant:42");

            var method = typeof(PolicyTarget).GetMethod(nameof(PolicyTarget.LoadAsync))!;
            registry.TryGetPolicy(method, out var memberDescriptor).Should().BeTrue();
            memberDescriptor!.KeyTemplate.Should().Be("policy:method:{arg}");

            registry.GetAllPolicies().Should().HaveCount(2);
        });

    [Fact]
    public Task GetPoliciesFor_returns_empty_when_not_found()
        => Spec(nameof(GetPoliciesFor_returns_empty_when_not_found), () =>
        {
            var registry = new CachePolicyRegistry(NullLogger<CachePolicyRegistry>.Instance);
            registry.Rebuild(Array.Empty<Assembly>());

            registry.GetPoliciesFor(typeof(CachePolicyRegistrySpec)).Should().BeEmpty();
        });

    private Task Spec(string scenario, Action body)
        => TestPipeline.For<CachePolicyRegistrySpec>(_output, scenario)
            .Assert(_ =>
            {
                body();
                return ValueTask.CompletedTask;
            })
            .RunAsync();

}

[CachePolicy(CacheScope.Entity, "policy:{Id}", Tags = new[] { "alpha", "tenant:42", "alpha" })]
public sealed class PolicyTarget
{
    [CachePolicy(CacheScope.EntityQuery, "policy:method:{arg}")]
    public void LoadAsync()
    {
    }
}
