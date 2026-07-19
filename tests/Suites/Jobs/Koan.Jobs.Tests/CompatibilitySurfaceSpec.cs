using AwesomeAssertions;
using Koan.Data.Core.Model;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Jobs.Tests;

public sealed class CompatibilitySurfaceSpec
{
    [Fact]
    public void Host_owned_runtime_implementations_are_not_public_surface()
    {
        Type[] runtimeTypes =
        [
            typeof(JobCoordinator),
            typeof(JobOrchestrator),
            typeof(JobScheduler),
            typeof(JobTypeRegistry),
            typeof(JobTypeBinding),
            typeof(DataJobLedger),
            typeof(InMemoryJobLedger),
            typeof(RoutingJobLedger),
            typeof(LaneFairSelector),
        ];

        runtimeTypes.Should().OnlyContain(type => type.IsNotPublic,
            "applications compose IJobCoordinator/IJobLedger while Jobs owns their host runtime");
    }

    [Fact]
    public void DataStore_requirement_rejects_a_host_without_durable_data()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new JobTypeRegistry([typeof(DurableRequirementProbe<int>)]));
        services.AddKoanJobs(options => options.EnableWorker = false);
        using var provider = services.BuildServiceProvider();

        Action resolve = () => provider.GetRequiredService<IJobLedger>();

        resolve.Should().Throw<InvalidOperationException>()
            .WithMessage("*cannot honor [JobPersistence(DataStore)]*")
            .WithMessage("*no durable Data adapter*");
    }
}

[JobPersistence(JobPersistenceMode.DataStore)]
public sealed class DurableRequirementProbe<T> : Entity<DurableRequirementProbe<T>>, IKoanJob<DurableRequirementProbe<T>>
{
    public static Task Execute(DurableRequirementProbe<T> job, JobContext context, CancellationToken ct)
        => Task.CompletedTask;
}
