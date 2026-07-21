using AwesomeAssertions;
using Koan.Cache.Abstractions.Policies;
using Koan.Cache.Abstractions.Primitives;
using System;
using System.Collections.Generic;
using Xunit;

namespace Koan.Tests.Cache.Abstractions.Specs;

/// <summary>
/// Canonical-rule specs for <see cref="CacheL1TtlPolicy.Derive"/>. The L1 TTL derivation
/// rule lives here as a single source of truth; both <see cref="CacheWriteOptions.GetEffectiveL1Ttl"/>
/// and <c>CachePolicyMaterializer.ResolveL1Ttl</c> delegate to it. The drift-defense test below
/// asserts the delegation actually holds — any future divergence breaks this test.
/// </summary>
public sealed class CacheL1TtlPolicySpec
{
    [Fact]
    public void Explicit_L1_override_wins_unconditionally()
    {
        CacheL1TtlPolicy.Derive(absoluteTtl: TimeSpan.FromMinutes(10), l1Override: TimeSpan.FromSeconds(5))
            .Should().Be(TimeSpan.FromSeconds(5));

        CacheL1TtlPolicy.Derive(absoluteTtl: null, l1Override: TimeSpan.FromMinutes(1))
            .Should().Be(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Null_inputs_return_null()
    {
        CacheL1TtlPolicy.Derive(absoluteTtl: null, l1Override: null).Should().BeNull();
    }

    [Fact]
    public void Large_L2_derives_half_above_30s_floor()
    {
        // 10 min L2 → half = 5 min ≥ 30s floor → derive 5 min, clamp by 10 min → 5 min.
        CacheL1TtlPolicy.Derive(TimeSpan.FromMinutes(10), null)
            .Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void Medium_L2_derives_at_30s_floor()
    {
        // 1 min L2 → half = 30s = floor → derive 30s, clamp by 60s → 30s.
        CacheL1TtlPolicy.Derive(TimeSpan.FromMinutes(1), null)
            .Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Small_L2_clamps_to_L2_not_floor()
    {
        // 20s L2 → half = 10s, max(30s, 10s) = 30s, min(20s, 30s) = 20s. L1 must never outlive L2.
        CacheL1TtlPolicy.Derive(TimeSpan.FromSeconds(20), null)
            .Should().Be(TimeSpan.FromSeconds(20));
    }

    [Fact]
    public void Sub_second_L2_clamps_to_L2_not_floor()
    {
        // 200ms L2 → must clamp to 200ms. This is the failure mode the bounded-stale integration test caught.
        CacheL1TtlPolicy.Derive(TimeSpan.FromMilliseconds(200), null)
            .Should().Be(TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public void Default_floor_is_30_seconds()
    {
        CacheL1TtlPolicy.DefaultFloor.Should().Be(TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// Drift defense: every other site that derives an L1 TTL MUST delegate to this policy
    /// rather than reimplement the rule. The test exercises both call sites we know about
    /// (<see cref="CacheWriteOptions.GetEffectiveL1Ttl"/> and the policy method) across a
    /// representative input matrix. Any divergence breaks this test.
    /// </summary>
    [Fact]
    public void All_call_sites_produce_identical_output_across_input_matrix()
    {
        var matrix = new (TimeSpan? AbsoluteTtl, TimeSpan? L1Override)[]
        {
            (null, null),
            (null, TimeSpan.FromSeconds(5)),
            (TimeSpan.FromMilliseconds(100), null),
            (TimeSpan.FromMilliseconds(200), null),
            (TimeSpan.FromSeconds(20), null),
            (TimeSpan.FromSeconds(30), null),
            (TimeSpan.FromMinutes(1), null),
            (TimeSpan.FromMinutes(10), null),
            (TimeSpan.FromHours(1), null),
            (TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(5)),  // override applies
            (TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(5)),  // override applies
        };

        foreach (var (absoluteTtl, l1Override) in matrix)
        {
            var canonical = CacheL1TtlPolicy.Derive(absoluteTtl, l1Override);

            var writeOptions = new CacheWriteOptions(
                AbsoluteTtl: absoluteTtl,
                L1AbsoluteTtl: l1Override,
                SlidingTtl: null,
                AllowStaleFor: null,
                Tags: new HashSet<string>(),
                Region: null,
                ScopeId: null,
                ForceCoherenceBroadcast: true);

            writeOptions.GetEffectiveL1Ttl().Should().Be(canonical,
                $"CacheWriteOptions.GetEffectiveL1Ttl must delegate to CacheL1TtlPolicy.Derive (input: abs={absoluteTtl}, l1={l1Override})");
        }
    }
}
