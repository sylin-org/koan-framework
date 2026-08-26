using AwesomeAssertions;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Core.Model;
using Koan.Data.Core.Pipeline;
using Koan.Data.Hygiene;
using Xunit;

namespace Koan.Tests.Data.Core.Specs.Pipeline;

/// <summary>Field hygiene: [Trim]/[Lowercase]/[Uppercase] normalize annotated string properties on
/// the persisted clone — the caller's instance is never touched (ARCH-0098 clone discipline).</summary>
public sealed class HygieneTransformSpec
{
    private sealed class Person : Entity<Person, string>
    {
        [Identifier] public override string Id { get; set; } = default!;

        [Trim] public string Name { get; set; } = "";

        [Trim, Lowercase] public string Email { get; set; } = "";

        [Uppercase] public string Code { get; set; } = "";

        [Trim] public string? Nickname { get; set; }

        [Lowercase] public int NotAString { get; set; }   // hygiene skips non-strings silently

        [Trim] public string ReadOnly { get; } = "static"; // skipped: no setter
    }

    private static StorageFieldTransformPlan Plan() => new([new HygieneFieldTransformContributor()]);

    [Fact]
    public void write_applies_trim_and_casing_on_the_clone()
    {
        var plan = Plan();
        var compiled = plan.For(typeof(Person));
        compiled.HasTransforms.Should().BeTrue();

        var entity = new Person { Id = "p1", Name = "  Ada  ", Email = "  Ada@Example.COM ", Code = "abc", Nickname = null };
        var clone = compiled.CloneForWrite(entity);

        var persisted = (Person)clone;
        persisted.Name.Should().Be("Ada");
        persisted.Email.Should().Be("ada@example.com");
        persisted.Code.Should().Be("ABC");
        persisted.Nickname.Should().BeNull("null passes through untouched");

        // Caller instance untouched — ARCH-0098 clone discipline.
        entity.Name.Should().Be("  Ada  ");
        entity.Email.Should().Be("  Ada@Example.COM ");
        entity.Code.Should().Be("abc");
    }

    [Fact]
    public void plan_is_empty_for_types_without_hygiene_attributes()
    {
        var plan = Plan();
        plan.For(typeof(Other)).HasTransforms.Should().BeFalse();
    }

    [Fact]
    public void read_is_identity_noop()
    {
        var plan = Plan();
        var compiled = plan.For(typeof(Person));
        var entity = new Person { Id = "p1", Name = "already-clean", Email = "a@b.c", Code = "X1" };
        compiled.ApplyOnRead(entity);
        entity.Name.Should().Be("already-clean", "hygiene is irreversible by design; stored value IS the value");
        entity.Code.Should().Be("X1");
    }

    [Fact]
    public void empty_strings_pass_through()
    {
        var entity = new Person { Id = "p2", Name = "", Email = "", Code = "" };
        // No throw on empty strings; invariant Apply short-circuits.
        var clone = Plan().For(typeof(Person)).CloneForWrite(entity);
        ((Person)clone).Name.Should().Be("");
    }

    private sealed class Other : Entity<Other, string>
    {
        [Identifier] public override string Id { get; set; } = default!;
        public string Plain { get; set; } = "";
    }
}
