using Koan.Data.Abstractions.Pipeline;
using Xunit;
using AwesomeAssertions;

namespace Koan.Data.SoftDelete.Tests;

/// <summary>
/// ARCH-0101 §4 — the bypass is BOUNDED by construction: target-scoped to the exact <c>(entityType, id)</c> the escape
/// verb named, so a cascade / lifecycle-handler delete of a DIFFERENT entity during the same async flow is NOT
/// bypassed (it takes the normal soft-delete path). A type-agnostic "bypass everything" flag would be a delete hole.
/// </summary>
public sealed class OperationOverrideBypassSpec
{
    private sealed class A { }
    private sealed class B { }

    [Fact]
    public void Bypass_matches_only_the_exact_type_and_id_it_targets()
    {
        OperationOverrideBypass.IsBypassedFor(typeof(A), "x").Should().BeFalse();   // nothing active

        using (OperationOverrideBypass.Enter(typeof(A), "x"))
        {
            OperationOverrideBypass.IsBypassedFor(typeof(A), "x").Should().BeTrue();    // the exact target
            OperationOverrideBypass.IsBypassedFor(typeof(A), "y").Should().BeFalse();   // same type, other id (a cascade peer)
            OperationOverrideBypass.IsBypassedFor(typeof(B), "x").Should().BeFalse();   // other type, same id
        }

        OperationOverrideBypass.IsBypassedFor(typeof(A), "x").Should().BeFalse();   // restored on dispose
    }

    [Fact]
    public void Nested_bypass_targets_restore_the_outer_target_on_dispose()
    {
        using (OperationOverrideBypass.Enter(typeof(A), "outer"))
        {
            using (OperationOverrideBypass.Enter(typeof(A), "inner"))
                OperationOverrideBypass.IsBypassedFor(typeof(A), "inner").Should().BeTrue();
            // the outer target is restored; the inner is no longer bypassed
            OperationOverrideBypass.IsBypassedFor(typeof(A), "outer").Should().BeTrue();
            OperationOverrideBypass.IsBypassedFor(typeof(A), "inner").Should().BeFalse();
        }
    }
}
