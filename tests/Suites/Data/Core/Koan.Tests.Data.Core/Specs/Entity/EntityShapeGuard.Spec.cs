using Koan.Data.Core.Model;

namespace Koan.Tests.Data.Core.Specs.Entity;

public sealed class EntityShapeGuardSpec
{
    [Fact]
    public void Own_root_passes()
    {
        var act = () => EntityShapeGuard.EnsureValid(typeof(GoodRoot));
        act.Should().NotThrow();
    }

    [Fact]
    public void Generic_base_siblings_pass()
    {
        var a = () => EntityShapeGuard.EnsureValid(typeof(SiblingA));
        var b = () => EntityShapeGuard.EnsureValid(typeof(SiblingB));
        a.Should().NotThrow();
        b.Should().NotThrow();
    }

    [Fact]
    public void Concrete_inheritance_throws_with_clear_message()
    {
        var act = () => EntityShapeGuard.EnsureValid(typeof(DerivedFromConcrete));
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*DerivedFromConcrete*")
            .WithMessage("*ConcreteRoot*")
            .WithMessage("*Sharing Shape Across Entities*");
    }

    [Fact]
    public void Concrete_inheritance_throws_for_custom_key()
    {
        var act = () => EntityShapeGuard.EnsureValid(typeof(IntDerived));
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Case_colliding_public_properties_throw_with_one_rename_correction()
    {
        var act = () => EntityShapeGuard.EnsureValid(typeof(CaseCollidingRoot));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*CaseCollidingRoot*")
            .WithMessage("*'Id'*")
            .WithMessage("*'id'*")
            .WithMessage("*Rename one property*");
    }

    private sealed class GoodRoot : Entity<GoodRoot> { public string? Tag { get; set; } }
    private sealed class CaseCollidingRoot : Entity<CaseCollidingRoot> { public string? id { get; set; } }

    private abstract class ShapeBase<T> : Entity<T> where T : ShapeBase<T> { public string? Shared { get; set; } }
    private sealed class SiblingA : ShapeBase<SiblingA> { }
    private sealed class SiblingB : ShapeBase<SiblingB> { public string? Extra { get; set; } }

    private class ConcreteRoot : Entity<ConcreteRoot> { public string? Tag { get; set; } }
    private sealed class DerivedFromConcrete : ConcreteRoot { public string? Extra { get; set; } }

    private class IntRoot : Entity<IntRoot, int> { public override int Id { get; set; } }
    private sealed class IntDerived : IntRoot { public string? Extra { get; set; } }
}
