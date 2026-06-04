namespace Koan.Jobs.Tests.Specs;

/// <summary>End-to-end smoke: discovery binds test-assembly job types, and a single-action job runs to Completed,
/// mutating its work-item. Verifies the whole pipeline (discover → bind → submit → claim → execute → settle).</summary>
public sealed class SmokeSpec
{
    [Fact]
    public async Task discovery_binds_test_job_types()
    {
        await using var host = await JobsTestHost.StartAsync();

        host.Registry.Count.Should().BeGreaterThan(0, "[KoanDiscoverable] must surface IKoanJob work-items in the test assembly");
        host.Registry.Get(typeof(GreetJob).FullName!).Should().NotBeNull();
        host.Registry.Get(typeof(Pipeline).FullName!).Should().NotBeNull();
    }

    [Fact]
    public async Task single_action_job_runs_and_mutates_work_item()
    {
        GreetJob.Reset();
        await using var host = await JobsTestHost.StartAsync();

        var job = new GreetJob { Name = "Koan" };
        var id = job.Id;

        await job.Job.Submit();
        await host.Drain();

        GreetJob.Executions.Should().Be(1);
        (await GreetJob.Get(id))!.Greeting.Should().Be("Hello, Koan");
        (await host.StatusOf<GreetJob>(id)).Should().Be(JobStatus.Completed);
    }
}
