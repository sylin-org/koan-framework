using Koan.Data.Abstractions.Instructions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Tests.Data.Core.Support;
using AwesomeAssertions;

namespace Koan.Tests.Data.Core.Specs.Entity;

/// <summary>
/// NK-045 (DATA-0132): Entity.Patch and PatchMerge honour the null policy they take, on reference and value-type
/// members, through the same applicators Web uses; a rejected null throws before any write.
/// </summary>
public sealed class EntityPatchNullPolicySpec
{
    private static async Task<PatchProbe> Seed()
        => await new PatchProbe { Notes = "kept", Priority = 7 }.Save();

    [Fact]
    public async Task Partial_json_null_policies_are_honoured()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync();
        using var route = EntityContext.Partition($"patch-partial-{Guid.CreateVersion7():n}");

        var ignored = await Seed();
        await PatchProbe.Patch(ignored.Id, new { Notes = (string?)null }, PartialJsonNullPolicy.Ignore);
        (await PatchProbe.Get(ignored.Id))!.Notes.Should().Be("kept", "Ignore leaves a null member unchanged");

        var rejected = await Seed();
        var reject = () => PatchProbe.Patch(rejected.Id, new { Notes = (string?)null, Priority = 1 }, PartialJsonNullPolicy.Reject);
        await reject.Should().ThrowAsync<InvalidOperationException>();
        var unchanged = (await PatchProbe.Get(rejected.Id))!;
        unchanged.Notes.Should().Be("kept");
        unchanged.Priority.Should().Be(7, "a rejected patch writes nothing");

        var nulled = await Seed();
        await PatchProbe.Patch(nulled.Id, new { Notes = (string?)null });
        (await PatchProbe.Get(nulled.Id))!.Notes.Should().BeNull("SetNull is the partial-JSON default");
    }

    [Fact]
    public async Task Merge_patch_null_policies_are_honoured_on_value_types()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync();
        using var route = EntityContext.Partition($"patch-merge-{Guid.CreateVersion7():n}");

        var defaulted = await Seed();
        await PatchProbe.PatchMerge(defaulted.Id, new { Priority = (int?)null });
        (await PatchProbe.Get(defaulted.Id))!.Priority.Should().Be(0, "SetDefault gives default(int)");

        var rejected = await Seed();
        var reject = () => PatchProbe.PatchMerge(rejected.Id, new { Priority = (int?)null }, MergePatchNullPolicy.Reject);
        await reject.Should().ThrowAsync<InvalidOperationException>();
        (await PatchProbe.Get(rejected.Id))!.Priority.Should().Be(7);
    }

    private sealed class PatchProbe : Entity<PatchProbe>
    {
        public string? Notes { get; set; }
        public int Priority { get; set; }
    }
}
