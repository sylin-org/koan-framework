using AwesomeAssertions;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Core.Model;
using Koan.Data.Core.Pipeline;
using Xunit;

namespace Koan.Tests.Data.Core.Specs.Pipeline;

public sealed class FieldTransformSeamSpec
{
    private sealed class Doc : Entity<Doc, string>
    {
        [Identifier] public override string Id { get; set; } = default!;
        public string Body { get; set; } = "";
    }

    private sealed class Other : Entity<Other, string>
    {
        [Identifier] public override string Id { get; set; } = default!;
    }

    private sealed class UpcaseTransform : IFieldTransform
    {
        public void ApplyOnWrite(object entity) { if (entity is Doc doc) doc.Body = doc.Body.ToUpperInvariant(); }
        public void ApplyOnRead(object entity) { if (entity is Doc doc) doc.Body = doc.Body.ToLowerInvariant(); }
    }

    private sealed class Contributor(string id = "upcase") : IFieldTransformContributor
    {
        public string Id => id;
        public IFieldTransform? Build(Type entityType) => entityType == typeof(Doc) ? new UpcaseTransform() : null;
    }

    private sealed class RecordingTransform(string id, ICollection<string> calls) : IFieldTransform
    {
        public void ApplyOnWrite(object entity) => calls.Add($"write:{id}");
        public void ApplyOnRead(object entity) => calls.Add($"read:{id}");
    }

    private sealed class RecordingContributor(string id, int order, ICollection<string> calls) : IFieldTransformContributor
    {
        public string Id => id;
        public int Order => order;
        public IFieldTransform? Build(Type entityType)
            => entityType == typeof(Doc) ? new RecordingTransform(id, calls) : null;
    }

    [Fact]
    public void No_contributor_produces_an_empty_plan()
    {
        var plan = new StorageFieldTransformPlan([]);
        plan.For(typeof(Doc)).HasTransforms.Should().BeFalse();
        plan.HasTransformsFor(typeof(Doc)).Should().BeFalse();
    }

    [Fact]
    public void Applicable_contributor_is_compiled_once_for_its_type()
    {
        var plan = new StorageFieldTransformPlan([new Contributor()]);
        plan.HasTransformsFor(typeof(Doc)).Should().BeTrue();
        plan.HasTransformsFor(typeof(Other)).Should().BeFalse();
        plan.ContributorIdsFor(typeof(Doc)).Should().Equal("upcase");
        plan.For(typeof(Doc)).Should().BeSameAs(plan.For(typeof(Doc)));
    }

    [Fact]
    public void Clone_for_write_transforms_only_the_clone()
    {
        var compiled = new StorageFieldTransformPlan([new Contributor()]).For(typeof(Doc));
        var original = new Doc { Body = "ada" };
        var payload = (Doc)compiled.CloneForWrite(original);
        payload.Should().NotBeSameAs(original);
        payload.Body.Should().Be("ADA");
        original.Body.Should().Be("ada");
    }

    [Fact]
    public void Read_reverse_transforms_in_place()
    {
        var compiled = new StorageFieldTransformPlan([new Contributor()]).For(typeof(Doc));
        var entity = new Doc { Body = "PROTECTED" };
        compiled.ApplyOnRead(entity);
        entity.Body.Should().Be("protected");
    }

    [Fact]
    public void Multiple_transforms_write_forward_and_read_in_reverse_order()
    {
        var calls = new List<string>();
        var compiled = new StorageFieldTransformPlan([
            new RecordingContributor("last", 20, calls),
            new RecordingContributor("first", 10, calls),
        ]).For(typeof(Doc));

        compiled.CloneForWrite(new Doc());
        compiled.ApplyOnRead(new Doc());

        calls.Should().Equal("write:first", "write:last", "read:last", "read:first");
    }

    [Fact]
    public void Plans_are_host_owned_and_do_not_share_contributors()
    {
        var firstHost = new StorageFieldTransformPlan([new Contributor()]);
        var secondHost = new StorageFieldTransformPlan([]);
        firstHost.HasTransformsFor(typeof(Doc)).Should().BeTrue();
        secondHost.HasTransformsFor(typeof(Doc)).Should().BeFalse();
    }

    [Fact]
    public void Duplicate_contributor_ids_fail_composition()
    {
        var act = () => new StorageFieldTransformPlan([new Contributor(), new Contributor()]);
        act.Should().Throw<InvalidOperationException>().WithMessage("*upcase*more than once*");
    }

    [Fact]
    public void Entity_cloner_produces_a_distinct_shallow_copy()
    {
        var original = new Doc { Id = "1", Body = "abc" };
        var clone = (Doc)EntityCloner.ShallowClone(original);
        clone.Should().NotBeSameAs(original);
        clone.Id.Should().Be("1");
        clone.Body = "changed";
        original.Body.Should().Be("abc");
    }
}
