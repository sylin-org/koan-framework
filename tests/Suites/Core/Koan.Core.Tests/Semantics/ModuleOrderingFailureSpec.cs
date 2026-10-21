using AwesomeAssertions;
using Koan.Core.Ordering;
using Koan.Core.Semantics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Core.Tests.Semantics;

/// <summary>
/// ARCH-0138: an invalid [Before]/[After] declaration is a registration failure owned by the declaring module, so the
/// bootstrapper records it and throws KoanBootException like every other registration failure.
/// </summary>
public sealed class ModuleOrderingFailureSpec
{
    private const string OwnerId = "Sylin.Koan.Spec.Ordering.Invalid";

    [Fact]
    public void An_invalid_ordering_target_is_a_problem_owned_by_the_declaring_module()
    {
        var runtime = SemanticModuleRuntime.Create(new SemanticHostConstitution(
            [
                new SemanticComponentDescriptor(OwnerId, typeof(InvalidTargetModule), static () => new InvalidTargetModule()),
                new SemanticComponentDescriptor("Sylin.Koan.Spec.Ordering.Plain", typeof(PlainModule), static () => new PlainModule()),
            ],
            [],
            [],
            [],
            isDegraded: false));

        var register = () => runtime.Register(new ServiceCollection());

        var failure = register.Should().Throw<SemanticModuleRuntime.SemanticRuntimeException>().Which;
        failure.Problem.Owner.Should().Be(new SemanticId(OwnerId));
        failure.Problem.Reason.Should().Be("module-ordering-invalid");
        failure.Message.Should().Contain("[Before]").And.Contain(nameof(InvalidTargetModule));
    }

    [Fact]
    public void A_cycle_is_owned_by_a_module_on_the_cycle()
    {
        var runtime = SemanticModuleRuntime.Create(new SemanticHostConstitution(
            [
                new SemanticComponentDescriptor("Sylin.Koan.Spec.Ordering.CycleA", typeof(CycleA), static () => new CycleA()),
                new SemanticComponentDescriptor("Sylin.Koan.Spec.Ordering.CycleB", typeof(CycleB), static () => new CycleB()),
            ],
            [],
            [],
            [],
            isDegraded: false));

        var register = () => runtime.Register(new ServiceCollection());

        var failure = register.Should().Throw<SemanticModuleRuntime.SemanticRuntimeException>().Which;
        failure.Problem.Reason.Should().Be("module-ordering-invalid");
        failure.Problem.Owner.Value.Should().BeOneOf("Sylin.Koan.Spec.Ordering.CycleA", "Sylin.Koan.Spec.Ordering.CycleB");
        failure.Message.Should().Contain("cycle");
    }

    [Before(typeof(string))]
    private sealed class InvalidTargetModule : KoanModule;

    private sealed class PlainModule : KoanModule;

    [Before(typeof(CycleB))]
    private sealed class CycleA : KoanModule;

    [Before(typeof(CycleA))]
    private sealed class CycleB : KoanModule;
}
