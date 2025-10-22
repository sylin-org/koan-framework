using System;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Koan.Jobs.Model;
using Koan.Jobs.Options;
using Koan.Jobs.Progress;
using Koan.Jobs.Store;
using Koan.Jobs.Events;

namespace Koan.Jobs.Execution;

internal sealed class JobExecutionContext
{
    public JobExecutionContext(
        IServiceProvider services,
        IJobStore store,
        JobProgressBroker progressBroker,
        IJobEventPublisher eventPublisher,
        JobsOptions options,
        ILogger logger)
    {
        Services = services;
        Store = store;
        ProgressBroker = progressBroker;
        EventPublisher = eventPublisher;
        Options = options;
        Logger = logger;
    }

    public IServiceProvider Services { get; }
    public IJobStore Store { get; }
    public JobProgressBroker ProgressBroker { get; }
    public IJobEventPublisher EventPublisher { get; }
    public JobsOptions Options { get; }
    public ILogger Logger { get; }
    public JsonSerializerOptions SerializerOptions => Options.SerializerOptions;

    public T Resolve<T>() where T : notnull => Services.GetRequiredService<T>();
}

internal sealed record JobExecutionOutcome(
    JobExecutionStatus Status,
    object? Result,
    Exception? Error);
