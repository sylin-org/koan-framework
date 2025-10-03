using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Koan.Core.Hosting.App;
using Koan.Jobs;
using Koan.Jobs.Core.Tests.Infrastructure;
using Koan.Jobs.Execution;
using Koan.Jobs.Model;
using Koan.Jobs.Progress;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Koan.Jobs.Core.Tests;

public sealed class JobRuntimeTests
{
    [Fact]
    public async Task Run_InMemoryJob_CompletesAndPublishesProgress()
    {
        using var scope = CreateScope();
        var provider = scope.Provider;

        var job = await SampleJob.Start(new SampleJobContext("alpha")).Run();

        var updates = new List<JobProgressUpdate>();
        using var subscription = job.OnProgress(update =>
        {
            lock (updates)
            {
                updates.Add(update);
            }

            return Task.CompletedTask;
        });

        using var workerCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var worker = ActivatorUtilities.CreateInstance<JobWorkerService>(provider);
        var workerTask = Task.Run(() => worker.ExecuteCoreAsync(workerCts.Token));

        try
        {
            var result = await job.Wait(TimeSpan.FromSeconds(5));
            result.Should().Be("ALPHA");

            var snapshot = await job.Refresh();
            snapshot.Status.Should().Be(JobStatus.Completed);
            snapshot.Result.Should().Be("ALPHA");
            updates.Should().NotBeEmpty();
            updates[^1].Percentage.Should().BeApproximately(1.0, 0.001);
        }
        finally
        {
            workerCts.Cancel();
        }

        try
        {
            await workerTask;
        }
        catch (OperationCanceledException) when (workerCts.IsCancellationRequested)
        {
            // expected during shutdown
        }
    }

    [Fact]
    public async Task Run_FlakyJob_RetriesUntilSuccess()
    {
        using var scope = CreateScope();
        var provider = scope.Provider;
        FlakyJob.Reset();

        var job = await FlakyJob.Start(new SampleJobContext("beta")).Audit().Run();

        using var workerCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var worker = ActivatorUtilities.CreateInstance<JobWorkerService>(provider);
        var workerTask = Task.Run(() => worker.ExecuteCoreAsync(workerCts.Token));

        try
        {
            var result = await job.Wait(TimeSpan.FromSeconds(5));
            result.Should().Be("beta");

            var refreshed = await job.Refresh();
            refreshed.Status.Should().Be(JobStatus.Completed);
            refreshed.LastError.Should().BeNull();

            var coordinator = provider.GetRequiredService<IJobCoordinator>();
            var executions = await coordinator.GetExecutionsAsync(job.Id, CancellationToken.None);
            executions.Should().HaveCount(2);
        }
        finally
        {
            workerCts.Cancel();
        }

        try
        {
            await workerTask;
        }
        catch (OperationCanceledException) when (workerCts.IsCancellationRequested)
        {
        }
    }

    private static JobTestScope CreateScope()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
        services.AddLogging();
        services.AddKoanJobs();

        var provider = services.BuildServiceProvider();
        return new JobTestScope(provider);
    }

    private sealed class JobTestScope : IDisposable
    {
        private readonly IServiceProvider? _previousAppHost;
        public ServiceProvider Provider { get; }

        public JobTestScope(ServiceProvider provider)
        {
            Provider = provider;
            _previousAppHost = AppHost.Current;
            AppHost.Current = provider;
        }

        public void Dispose()
        {
            AppHost.Current = _previousAppHost;
            Provider.Dispose();
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Koan.Jobs.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
