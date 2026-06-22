using System;
using AwesomeAssertions;
using Koan.Data.Abstractions;
using Koan.Data.Core.Model;
using Koan.Data.Core.Pipeline;
using Xunit;

namespace Koan.Tests.Data.Core.Specs.Pipeline;

/// <summary>
/// ARCH-0098 phase 3a — the generic round-trip field-transform seam (the classification axis is its first
/// consumer, but this is validated axis-free). Pins: the off-gate (empty plan, byte-identical), per-type
/// applicability, clone-on-write (the original is never mutated; the persisted clone is), reverse-on-read
/// (in place), the public <c>HasTransformsFor</c> probe the cache uses, idempotent registration, and the
/// shallow-clone primitive.
/// </summary>
[Collection("field-transform-registry")]   // serialize: the registry + plan memo are process-global static state
public sealed class FieldTransformSeamSpec : IDisposable
{
    public FieldTransformSeamSpec() => StorageFieldTransformRegistry.Reset();
    public void Dispose() => StorageFieldTransformRegistry.Reset();

    private sealed class Doc : Entity<Doc, string>
    {
        [Identifier] public override string Id { get; set; } = default!;
        public string Body { get; set; } = "";
    }

    private sealed class Other : Entity<Other, string>
    {
        [Identifier] public override string Id { get; set; } = default!;
    }

    /// <summary>Upper-cases Body on write, lower-cases it on read — so the round-trip direction is observable.</summary>
    private sealed class UpcaseTransform : IFieldTransform
    {
        public void ApplyOnWrite(object entity) { if (entity is Doc d) d.Body = d.Body.ToUpperInvariant(); }
        public void ApplyOnRead(object entity) { if (entity is Doc d) d.Body = d.Body.ToLowerInvariant(); }
    }

    private static void RegisterForDoc()
        => StorageFieldTransformRegistry.Register(new FieldTransformContributor(
            "upcase", t => t == typeof(Doc) ? new UpcaseTransform() : null));

    [Fact]
    public void Off_path_is_an_empty_plan()
    {
        StorageFieldTransformRegistry.IsEmpty.Should().BeTrue();
        StorageFieldTransformPlan.For(typeof(Doc)).HasTransforms.Should().BeFalse();
        StorageFieldTransformRegistry.HasTransformsFor(typeof(Doc)).Should().BeFalse();
    }

    [Fact]
    public void A_registered_transform_is_picked_up()
    {
        RegisterForDoc();
        StorageFieldTransformPlan.For(typeof(Doc)).HasTransforms.Should().BeTrue();
        StorageFieldTransformRegistry.HasTransformsFor(typeof(Doc)).Should().BeTrue();
    }

    [Fact]
    public void A_contributor_returns_null_for_an_inapplicable_type()
    {
        RegisterForDoc();   // applies only to Doc
        StorageFieldTransformRegistry.HasTransformsFor(typeof(Other)).Should().BeFalse();
    }

    [Fact]
    public void CloneForWrite_transforms_a_clone_and_leaves_the_original_plaintext()
    {
        RegisterForDoc();
        var original = new Doc { Body = "ada" };

        var payload = (Doc)StorageFieldTransformPlan.For(typeof(Doc)).CloneForWrite(original);

        payload.Should().NotBeSameAs(original);   // a distinct instance is persisted
        payload.Body.Should().Be("ADA");          // the clone is transformed (encrypted)
        original.Body.Should().Be("ada");         // the caller's instance is untouched
    }

    [Fact]
    public void ApplyOnRead_transforms_in_place()
    {
        RegisterForDoc();
        var entity = new Doc { Body = "ENCRYPTED" };
        StorageFieldTransformPlan.For(typeof(Doc)).ApplyOnRead(entity);
        entity.Body.Should().Be("encrypted");     // restored in place on the returned entity
    }

    [Fact]
    public void Registration_is_idempotent_by_id()
    {
        RegisterForDoc();
        RegisterForDoc();   // duplicate id → no-op
        StorageFieldTransformRegistry.All.Count.Should().Be(1);
    }

    [Fact]
    public void Registration_invalidates_the_plan_memo()
    {
        StorageFieldTransformPlan.For(typeof(Doc)).HasTransforms.Should().BeFalse();   // built + memoized empty
        RegisterForDoc();
        StorageFieldTransformPlan.For(typeof(Doc)).HasTransforms.Should().BeTrue();    // memo invalidated
    }

    [Fact]
    public void Register_rejects_an_empty_id()
    {
        var act = () => StorageFieldTransformRegistry.Register(new FieldTransformContributor("", _ => null));
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void EntityCloner_produces_a_distinct_shallow_copy()
    {
        var original = new Doc { Id = "1", Body = "abc" };
        var clone = (Doc)EntityCloner.ShallowClone(original);

        clone.Should().NotBeSameAs(original);
        clone.Id.Should().Be("1");
        clone.Body.Should().Be("abc");

        clone.Body = "changed";            // reassigning the clone's field never touches the original
        original.Body.Should().Be("abc");
    }
}
