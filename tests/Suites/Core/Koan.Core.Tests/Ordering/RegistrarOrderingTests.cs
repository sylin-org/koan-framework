#nullable enable
using System;
using System.Linq;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Ordering;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Core.Tests.Ordering;

/// <summary>
/// Tests for <see cref="RegistrarOrdering.Sort"/>. Verifies the contract
/// from CORE-0091: constraint satisfaction, deterministic tie-break,
/// cycle detection, and validation of attribute targets.
/// </summary>
public class RegistrarOrderingTests
{
    [Fact]
    public void Sort_NoConstraints_StableByAssemblyQualifiedName()
    {
        // Three unconstrained initializers — output must be deterministic
        // across runs regardless of input order. AQN ordinal sort is the
        // documented tie-break.
        var input = new[] { typeof(NodeZ), typeof(NodeA), typeof(NodeM) };
        var sorted = RegistrarOrdering.Sort(input);

        sorted.Should().HaveCount(3);
        sorted.Select(t => t.Name).Should().Equal("NodeA", "NodeM", "NodeZ");
    }

    [Fact]
    public void Sort_SingleAfterConstraint_PutsAnnotatedNodeAfterTarget()
    {
        var input = new[] { typeof(BeforeChain_A), typeof(BeforeChain_B) };
        var sorted = RegistrarOrdering.Sort(input);

        // B is [After(A)] so order must be A then B regardless of input.
        sorted.Select(t => t.Name).Should().Equal("BeforeChain_A", "BeforeChain_B");
    }

    [Fact]
    public void Sort_BeforeAndAfterAreEquivalent()
    {
        // BeforeChain_A → [Before(BeforeChain_B)] AND BeforeChain_B → [After(BeforeChain_A)].
        // Both forms add the same edge A -> B. Duplicate edges must collapse;
        // the result is still A then B.
        var input = new[] { typeof(BeforeChain_B), typeof(BeforeChain_A) };
        var sorted = RegistrarOrdering.Sort(input);

        sorted.Select(t => t.Name).Should().Equal("BeforeChain_A", "BeforeChain_B");
    }

    [Fact]
    public void Sort_ChainedConstraints_HonorTransitively()
    {
        // C [After B] [After A]; B [After A]; A unconstrained.
        // Expected order: A, B, C.
        var input = new[] { typeof(Chain_C), typeof(Chain_A), typeof(Chain_B) };
        var sorted = RegistrarOrdering.Sort(input);

        sorted.Select(t => t.Name).Should().Equal("Chain_A", "Chain_B", "Chain_C");
    }

    [Fact]
    public void Sort_TargetNotInInputSet_TreatedAsMootConstraint()
    {
        // OptionalDep_X [After(SomeOtherType)] where SomeOtherType isn't in
        // the input. The constraint should silently drop — reference =
        // intent means an absent module produces no ordering obligation.
        var input = new[] { typeof(OptionalDep_X), typeof(NodeA) };
        var sorted = RegistrarOrdering.Sort(input);

        sorted.Should().HaveCount(2);
        sorted.Should().Contain(typeof(OptionalDep_X));
        sorted.Should().Contain(typeof(NodeA));
    }

    [Fact]
    public void Sort_TwoNodeCycle_ThrowsWithPath()
    {
        // Cycle_Left [Before Cycle_Right]; Cycle_Right [Before Cycle_Left].
        var input = new[] { typeof(Cycle_Left), typeof(Cycle_Right) };

        var act = () => RegistrarOrdering.Sort(input);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*cycle*Cycle_Left*Cycle_Right*");
    }

    [Fact]
    public void Sort_SelfReferenceVia_Before_Throws()
    {
        var input = new[] { typeof(SelfBefore) };

        var act = () => RegistrarOrdering.Sort(input);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*SelfBefore*references itself*");
    }

    [Fact]
    public void Sort_TargetNotIKoanInitializer_Throws()
    {
        var input = new[] { typeof(BadTarget) };

        var act = () => RegistrarOrdering.Sort(input);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*does not implement*IKoanInitializer*");
    }

    [Fact]
    public void Sort_EmptyInput_ReturnsEmpty()
    {
        var sorted = RegistrarOrdering.Sort(Array.Empty<Type>());
        sorted.Should().BeEmpty();
    }

    [Fact]
    public void Sort_SingleNode_ReturnsSingleton()
    {
        var sorted = RegistrarOrdering.Sort(new[] { typeof(NodeA) });
        sorted.Should().HaveCount(1).And.Contain(typeof(NodeA));
    }

    [Fact]
    public void Sort_DuplicateInputs_DeDupes()
    {
        var input = new[] { typeof(NodeA), typeof(NodeA), typeof(NodeA) };
        var sorted = RegistrarOrdering.Sort(input);
        sorted.Should().HaveCount(1);
    }

    // ---------------------------------------------------------------------
    // Test-only initializer fixtures. Empty Initialize impl is fine — the
    // sort never invokes them; it only reads their attributes + types.
    // ---------------------------------------------------------------------

    private abstract class TestInitializer : IKoanInitializer
    {
        public void Initialize(IServiceCollection services) { }
    }

    private sealed class NodeA : TestInitializer { }
    private sealed class NodeM : TestInitializer { }
    private sealed class NodeZ : TestInitializer { }

    private sealed class BeforeChain_A : TestInitializer { }

    [After(typeof(BeforeChain_A))]
    [Before(typeof(BeforeChain_B))]
    private sealed class BeforeChain_AAlsoBefore : TestInitializer { }

    private sealed class BeforeChain_B : TestInitializer { }

    private sealed class Chain_A : TestInitializer { }

    [After(typeof(Chain_A))]
    private sealed class Chain_B : TestInitializer { }

    [After(typeof(Chain_A), typeof(Chain_B))]
    private sealed class Chain_C : TestInitializer { }

    // Targets a type that isn't in the input set; the constraint should
    // silently drop. Use a real IKoanInitializer-implementing type that
    // tests just don't pass in.
    [After(typeof(NodeM))] // tests pass NodeA + this one, NodeM omitted
    private sealed class OptionalDep_X : TestInitializer { }

    [Before(typeof(Cycle_Right))]
    private sealed class Cycle_Left : TestInitializer { }

    [Before(typeof(Cycle_Left))]
    private sealed class Cycle_Right : TestInitializer { }

    [Before(typeof(SelfBefore))]
    private sealed class SelfBefore : TestInitializer { }

    // [After] targeting a class that isn't an IKoanInitializer.
    [After(typeof(string))]
    private sealed class BadTarget : TestInitializer { }
}
