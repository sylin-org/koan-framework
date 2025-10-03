using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Koan.Core.Hosting.App;
using Koan.Jobs.Core.Tests.Infrastructure;
using Koan.Jobs.Execution;
using Koan.Jobs.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Koan.Jobs.Core.Tests;

/// <summary>
/// Tests for [Timestamp] auto-update functionality on Job entities.
/// Validates DATA-0080: [Timestamp] Attribute Auto-Update implementation.
/// </summary>
public sealed class TimestampTests
{
    [Fact]
    public async Task Job_Creation_SetsLastModifiedAutomatically()
    {
        using var scope = CreateScope();

        var beforeCreate = DateTimeOffset.UtcNow.AddMilliseconds(-100);

        // Create job without explicitly setting LastModified
        var job = await SampleJob.Start(new SampleJobContext("auto-test")).Run();

        var afterCreate = DateTimeOffset.UtcNow.AddMilliseconds(100);

        // Verify LastModified was automatically set
        job.LastModified.Should().NotBe(default(DateTimeOffset));
        job.LastModified.Should().BeAfter(beforeCreate);
        job.LastModified.Should().BeBefore(afterCreate);
    }

    [Fact]
    public async Task Job_WithWorker_TimestampUpdatesOnStatusChanges()
    {
        using var scope = CreateScope();
        var provider = scope.Provider;

        // Create job that will be executed
        var job = await SampleJob.Start(new SampleJobContext("status-test")).Run();
        var initialTimestamp = job.LastModified;

        using var workerCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var worker = ActivatorUtilities.CreateInstance<JobWorkerService>(provider);
        var workerTask = Task.Run(() => worker.ExecuteCoreAsync(workerCts.Token));

        try
        {
            // Wait for job to complete
            await job.Wait(TimeSpan.FromSeconds(5));

            var completed = await job.Refresh();

            // LastModified should be updated after execution
            completed.LastModified.Should().BeAfter(initialTimestamp);
            completed.Status.Should().Be(JobStatus.Completed);
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
            // Expected during shutdown
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
