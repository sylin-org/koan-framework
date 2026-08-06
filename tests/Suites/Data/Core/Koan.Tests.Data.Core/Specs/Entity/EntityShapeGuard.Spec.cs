using Koan.Data.Core.Model;
using Koan.Data.Core.Polymorphism;
using Koan.Tests.Data.Core.Support;

namespace Koan.Tests.Data.Core.Specs.Entity;

public sealed class EntityShapeGuardSpec
{
    [Fact]
    public void Own_root_passes()
    {
        var act = () => EntityRootDescriptor.For(typeof(GoodRoot));
        act.Should().NotThrow();
    }

    [Fact]
    public void Generic_base_siblings_pass()
    {
        var a = () => EntityRootDescriptor.For(typeof(SiblingA));
        var b = () => EntityRootDescriptor.For(typeof(SiblingB));
        a.Should().NotThrow();
        b.Should().NotThrow();
    }

    [Fact]
    public void Generated_family_variant_passes()
    {
        var act = () => EntityRootDescriptor.For(typeof(PolymorphicEntityTestMedia.Anime));

        act.Should().NotThrow();
    }

    [Fact]
    public void Generated_family_variant_passes_for_custom_key()
    {
        var act = () => EntityRootDescriptor.For(typeof(PolymorphicIntEntityTestRoot.Variant));

        act.Should().NotThrow();
    }

    [Fact]
    public void Direct_concrete_inheritance_throws_with_the_family_correction()
    {
        var act = () => EntityRootDescriptor.For(typeof(PolymorphicEntityTestMedia.DirectAnime));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*DirectAnime*")
            .WithMessage("*PolymorphicEntityTestMedia<DirectAnime>*");
    }

    [Fact]
    public void Case_colliding_public_properties_throw_with_one_rename_correction()
    {
        var act = () => EntityRootDescriptor.For(typeof(CaseCollidingRoot));

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

}
