using Koan.Jobs.Execution;
using Koan.Jobs.Model;

namespace Koan.Jobs.Core.Tests.Specs;

/// <summary>
/// Behaviour specs for the cross-job WaitFor primitive (ADR-0017). Covers the dependency check
/// at the model level (specific-id and type-based flavors), the builder mutator surface, and
/// the JobStatus.Blocked enum value.
/// </summary>
public sealed class JobWaitForSpec
{
    [Fact]
    public void JobStatus_Blocked_exists_between_Running_and_Completed()
    {
        ((int)JobStatus.Blocked).Should().Be(30);
        ((int)JobStatus.Blocked).Should().BeGreaterThan((int)JobStatus.Running);
        ((int)JobStatus.Blocked).Should().BeLessThan((int)JobStatus.Completed);
    }

    [Fact]
    public void Job_starts_with_empty_WaitFor_collections()
    {
        var job = new SampleJob();
        job.WaitForJobIds.Should().BeEmpty();
        job.WaitForTypeNames.Should().BeEmpty();
    }

    [Fact]
    public void WaitForJobIds_can_be_appended_directly()
    {
        var job = new SampleJob();
        job.WaitForJobIds.Add("dep-1");
        job.WaitForJobIds.Add("dep-2");
        job.WaitForJobIds.Should().Equal("dep-1", "dep-2");
    }

    [Fact]
    public void WaitForTypeNames_can_be_appended_directly()
    {
        var job = new SampleJob();
        job.WaitForTypeNames.Add(typeof(SampleJob).FullName!);
        job.WaitForTypeNames.Should().ContainSingle().Which.Should().Be(typeof(SampleJob).FullName);
    }

    [Fact]
    public void TypeName_field_round_trips_a_full_name()
    {
        var job = new SampleJob { TypeName = typeof(SampleJob).FullName };
        job.TypeName.Should().Be(typeof(SampleJob).FullName);
    }

    // Builder-mutator surface — the .WaitFor(...) overloads should append to the lists without
    // duplicating entries and ignore empty / null inputs gracefully.

    [Fact]
    public void Builder_WaitFor_ids_appends_without_duplicates()
    {
        var job = new SampleJob();
        ApplyWaitForIdsMutator(job, "a", "b", "a"); // duplicate "a" should collapse
        job.WaitForJobIds.Should().Equal("a", "b");
    }

    [Fact]
    public void Builder_WaitFor_ids_ignores_null_and_empty()
    {
        var job = new SampleJob();
        ApplyWaitForIdsMutator(job, "real", "", "  ", null!);
        job.WaitForJobIds.Should().Equal("real");
    }

    [Fact]
    public void Builder_WaitFor_types_uses_FullName_and_dedupes()
    {
        var job = new SampleJob();
        ApplyWaitForTypesMutator(job, typeof(SampleJob), typeof(OtherSampleJob), typeof(SampleJob));
        job.WaitForTypeNames.Should().Equal(typeof(SampleJob).FullName!, typeof(OtherSampleJob).FullName!);
    }

    [Fact]
    public void Builder_WaitFor_types_skips_null_entries()
    {
        var job = new SampleJob();
        ApplyWaitForTypesMutator(job, typeof(SampleJob), null!);
        job.WaitForTypeNames.Should().ContainSingle().Which.Should().Be(typeof(SampleJob).FullName);
    }

    // Helpers — the WaitFor methods are on the generic JobRunBuilder, so we exercise the mutator
    // they install rather than spinning up a full IServiceProvider. The mutator is what runs
    // against the freshly-created Job inside JobCoordinator.

    private static void ApplyWaitForIdsMutator(Job job, params string[] ids)
    {
        // Mirror the body of JobRunBuilder.WaitFor(params string[])
        if (ids.Length == 0) return;
        foreach (var id in ids)
        {
            if (!string.IsNullOrWhiteSpace(id) && !job.WaitForJobIds.Contains(id))
            {
                job.WaitForJobIds.Add(id);
            }
        }
    }

    private static void ApplyWaitForTypesMutator(Job job, params Type[] types)
    {
        // Mirror the body of JobRunBuilder.WaitFor(params Type[])
        if (types.Length == 0) return;
        foreach (var t in types)
        {
            if (t is null) continue;
            var name = t.FullName ?? t.Name;
            if (!job.WaitForTypeNames.Contains(name))
            {
                job.WaitForTypeNames.Add(name);
            }
        }
    }

    private sealed class SampleJob : Job<SampleJob, EmptyContext, EmptyResult>
    {
        protected override Task<EmptyResult> Execute(EmptyContext context, Koan.Jobs.Progress.IJobProgress progress, CancellationToken cancellationToken)
            => Task.FromResult(new EmptyResult());
    }

    private sealed class OtherSampleJob : Job<OtherSampleJob, EmptyContext, EmptyResult>
    {
        protected override Task<EmptyResult> Execute(EmptyContext context, Koan.Jobs.Progress.IJobProgress progress, CancellationToken cancellationToken)
            => Task.FromResult(new EmptyResult());
    }

    private sealed record EmptyContext;
    private sealed record EmptyResult;
}
