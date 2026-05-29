using Koan.Data.Core.Model;

namespace Koan.Tests.Data.Core.Specs.Entity;

public sealed class EntityShapeGuardSpec
{
    [Fact]
    public void Own_root_passes()
    {
        var act = () => EntityShapeGuard.EnsureOwnRoot(typeof(GoodRoot));
        act.Should().NotThrow();
    }

    [Fact]
    public void Generic_base_siblings_pass()
    {
        var a = () => EntityShapeGuard.EnsureOwnRoot(typeof(SiblingA));
        var b = () => EntityShapeGuard.EnsureOwnRoot(typeof(SiblingB));
        a.Should().NotThrow();
        b.Should().NotThrow();
    }

    [Fact]
    public void Concrete_inheritance_throws_with_clear_message()
    {
        var act = () => EntityShapeGuard.EnsureOwnRoot(typeof(DerivedFromConcrete));
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*DerivedFromConcrete*")
            .WithMessage("*ConcreteRoot*")
            .WithMessage("*Sharing Shape Across Entities*");
    }

    [Fact]
    public void Concrete_inheritance_throws_for_custom_key()
    {
        var act = () => EntityShapeGuard.EnsureOwnRoot(typeof(IntDerived));
        act.Should().Throw<InvalidOperationException>();
    }

    private sealed class GoodRoot : Entity<GoodRoot> { public string? Tag { get; set; } }

    private abstract class ShapeBase<T> : Entity<T> where T : ShapeBase<T> { public string? Shared { get; set; } }
    private sealed class SiblingA : ShapeBase<SiblingA> { }
    private sealed class SiblingB : ShapeBase<SiblingB> { public string? Extra { get; set; } }

    private class ConcreteRoot : Entity<ConcreteRoot> { public string? Tag { get; set; } }
    private sealed class DerivedFromConcrete : ConcreteRoot { public string? Extra { get; set; } }

    private class IntRoot : Entity<IntRoot, int> { public override int Id { get; set; } }
    private sealed class IntDerived : IntRoot { public string? Extra { get; set; } }
}
