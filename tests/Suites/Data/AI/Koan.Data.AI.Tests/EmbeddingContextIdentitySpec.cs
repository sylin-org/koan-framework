using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Context;
using Koan.Data.AI.Attributes;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Koan.Data.AI.Tests;

/// <summary>
/// The embedding queue is global so its worker can claim across Data axes. Its durable identity must therefore include
/// the captured Koan context: equal Entity ids in two distinct contexts are two independent jobs, never
/// last-writer-wins.
/// </summary>
[Collection(nameof(DataAiHostLifecycleCollection))]
public sealed class EmbeddingContextIdentitySpec : IAsyncLifetime
{
    private IntegrationHost? _host;

    public async ValueTask InitializeAsync()
    {
        EmbeddingRegistry.RegisterTypes([typeof(ContextQueuedDocument)]);
        _host = await KoanIntegrationHost.Configure()
            .WithSetting("Koan:Environment", "Test")
            .WithSetting("Koan:Data:Sources:Default:Adapter", "inmemory")
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddKoan();
                services.Configure<EmbeddingWorkerOptions>(options => options.Enabled = false);
                services.TryAddEnumerable(
                    ServiceDescriptor.Singleton<IKoanContextCarrier, QueueTenantContextCarrier>());
            })
            .StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null) await _host.DisposeAsync();
    }

    [Fact]
    public async Task Same_entity_id_in_two_contexts_creates_two_isolated_queue_rows()
    {
        var entityId = Guid.NewGuid().ToString("n");

        using (QueueTenant.Use("tenant-a"))
            await new ContextQueuedDocument { Id = entityId, Text = "alpha" }.Save();
        using (QueueTenant.Use("tenant-b"))
            await new ContextQueuedDocument { Id = entityId, Text = "bravo" }.Save();

        var jobs = (await EmbedJob<ContextQueuedDocument>.All())
            .Where(job => job.EntityId == entityId)
            .ToArray();

        jobs.Should().HaveCount(2);
        jobs.Select(job => job.Id).Should().OnlyHaveUniqueItems();
        jobs.Select(job => job.AmbientCarrier![QueueTenantContextCarrier.StableAxisKey])
            .Should().BeEquivalentTo("v1:id:tenant-a", "v1:id:tenant-b");
        jobs.Select(job => job.Id).Should().OnlyContain(id =>
            id!.StartsWith("koan-context-embedjob:v1:", StringComparison.Ordinal)
            && !id.Contains("tenant-a", StringComparison.Ordinal)
            && !id.Contains("tenant-b", StringComparison.Ordinal));

        var selected = jobs[0];
        selected.Status = EmbedJobStatus.FailedPermanent;
        await selected.Save();

        (await EmbedJobExtensions.RequeueJobById<ContextQueuedDocument>(selected.Id!)).Should().BeTrue();
        (await EmbedJob<ContextQueuedDocument>.Get(selected.Id!))!.Status.Should().Be(EmbedJobStatus.Pending);
    }
}

[Embedding(Async = true, Template = "{Text}")]
public sealed class ContextQueuedDocument : Entity<ContextQueuedDocument>
{
    public string Text { get; set; } = "";
}

internal sealed record QueueTenantContext(string Id);

internal static class QueueTenant
{
    public static IDisposable Use(string id) => KoanContext.Push(new QueueTenantContext(id));
}

internal sealed class QueueTenantContextCarrier : IKoanContextCarrier
{
    internal const string StableAxisKey = "test:queue-tenant";

    public string AxisKey => StableAxisKey;
    public ContextIngressTrust MinimumIngressTrust => ContextIngressTrust.Authenticated;
    public string? Capture() => KoanContext.Get<QueueTenantContext>() is { } current
        ? "v1:id:" + current.Id
        : null;
    public IDisposable Restore(string captured)
        => captured.StartsWith("v1:id:", StringComparison.Ordinal) && captured.Length > "v1:id:".Length
            ? QueueTenant.Use(captured["v1:id:".Length..])
            : throw KoanContextCarrierException.MalformedPayload(AxisKey);
    public IDisposable Suppress() => KoanContext.Suppress<QueueTenantContext>();
}
