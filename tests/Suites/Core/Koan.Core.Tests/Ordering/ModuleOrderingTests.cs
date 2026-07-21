#nullable enable
using System;
using System.Linq;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Ordering;
using Xunit;

namespace Koan.Core.Tests.Ordering;

/// <summary>
/// Tests for <see cref="ModuleOrdering.Sort"/>. Verifies the contract
/// from CORE-0091: constraint satisfaction, deterministic tie-break,
/// cycle detection, and validation of attribute targets.
/// </summary>
public class ModuleOrderingTests
{
    [Fact]
    public void Sort_NoConstraints_StableByAssemblyQualifiedName()
    {
        // Three unconstrained initializers — output must be deterministic
        // across runs regardless of input order. AQN ordinal sort is the
        // documented tie-break.
        var input = new[] { typeof(NodeZ), typeof(NodeA), typeof(NodeM) };
        var sorted = ModuleOrdering.Sort(input);

        sorted.Should().HaveCount(3);
        sorted.Select(t => t.Name).Should().Equal("NodeA", "NodeM", "NodeZ");
    }

    [Fact]
    public void Sort_SingleAfterConstraint_PutsAnnotatedNodeAfterTarget()
    {
        var input = new[] { typeof(BeforeChain_A), typeof(BeforeChain_B) };
        var sorted = ModuleOrdering.Sort(input);

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
        var sorted = ModuleOrdering.Sort(input);

        sorted.Select(t => t.Name).Should().Equal("BeforeChain_A", "BeforeChain_B");
    }

    [Fact]
    public void Sort_ChainedConstraints_HonorTransitively()
    {
        // C [After B] [After A]; B [After A]; A unconstrained.
        // Expected order: A, B, C.
        var input = new[] { typeof(Chain_C), typeof(Chain_A), typeof(Chain_B) };
        var sorted = ModuleOrdering.Sort(input);

        sorted.Select(t => t.Name).Should().Equal("Chain_A", "Chain_B", "Chain_C");
    }

    [Fact]
    public void Sort_TargetNotInInputSet_TreatedAsMootConstraint()
    {
        // OptionalDep_X [After(SomeOtherType)] where SomeOtherType isn't in
        // the input. The constraint should silently drop — reference =
        // intent means an absent module produces no ordering obligation.
        var input = new[] { typeof(OptionalDep_X), typeof(NodeA) };
        var sorted = ModuleOrdering.Sort(input);

        sorted.Should().HaveCount(2);
        sorted.Should().Contain(typeof(OptionalDep_X));
        sorted.Should().Contain(typeof(NodeA));
    }

    [Fact]
    public void Sort_TwoNodeCycle_ThrowsWithPath()
    {
        // Cycle_Left [Before Cycle_Right]; Cycle_Right [Before Cycle_Left].
        var input = new[] { typeof(Cycle_Left), typeof(Cycle_Right) };

        var act = () => ModuleOrdering.Sort(input);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*cycle*Cycle_Left*Cycle_Right*");
    }

    [Fact]
    public void Sort_SelfReferenceVia_Before_Throws()
    {
        var input = new[] { typeof(SelfBefore) };

        var act = () => ModuleOrdering.Sort(input);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*SelfBefore*references itself*");
    }

    [Fact]
    public void Sort_TargetNotKoanModule_Throws()
    {
        var input = new[] { typeof(BadTarget) };

        var act = () => ModuleOrdering.Sort(input);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*does not derive from*KoanModule*");
    }

    [Fact]
    public void Sort_EmptyInput_ReturnsEmpty()
    {
        var sorted = ModuleOrdering.Sort(Array.Empty<Type>());
        sorted.Should().BeEmpty();
    }

    [Fact]
    public void Sort_SingleNode_ReturnsSingleton()
    {
        var sorted = ModuleOrdering.Sort(new[] { typeof(NodeA) });
        sorted.Should().HaveCount(1).And.Contain(typeof(NodeA));
    }

    [Fact]
    public void Sort_DuplicateInputs_DeDupes()
    {
        var input = new[] { typeof(NodeA), typeof(NodeA), typeof(NodeA) };
        var sorted = ModuleOrdering.Sort(input);
        sorted.Should().HaveCount(1);
    }

    // ---------------------------------------------------------------------
    // Test-only module fixtures. The sort never invokes them; it only reads attributes + types.
    // ---------------------------------------------------------------------

    private abstract class TestModule : KoanModule;

    private sealed class NodeA : TestModule { }
    private sealed class NodeM : TestModule { }
    private sealed class NodeZ : TestModule { }

    private sealed class BeforeChain_A : TestModule { }

    [After(typeof(BeforeChain_A))]
    [Before(typeof(BeforeChain_B))]
    private sealed class BeforeChain_AAlsoBefore : TestModule { }

    private sealed class BeforeChain_B : TestModule { }

    private sealed class Chain_A : TestModule { }

    [After(typeof(Chain_A))]
    private sealed class Chain_B : TestModule { }

    [After(typeof(Chain_A), typeof(Chain_B))]
    private sealed class Chain_C : TestModule { }

    // Targets a type that isn't in the input set; the constraint should
    // silently drop. Use a real KoanModule-derived type that
    // tests just don't pass in.
    [After(typeof(NodeM))] // tests pass NodeA + this one, NodeM omitted
    private sealed class OptionalDep_X : TestModule { }

    [Before(typeof(Cycle_Right))]
    private sealed class Cycle_Left : TestModule { }

    [Before(typeof(Cycle_Left))]
    private sealed class Cycle_Right : TestModule { }

    [Before(typeof(SelfBefore))]
    private sealed class SelfBefore : TestModule { }

    // [After] targeting a class that isn't a KoanModule.
    [After(typeof(string))]
    private sealed class BadTarget : TestModule { }
}
