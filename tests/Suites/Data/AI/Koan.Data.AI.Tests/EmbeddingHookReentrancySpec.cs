using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Models;
using Koan.AI.Contracts.Routing;
using Koan.AI.Contracts.Sources;
using Koan.Core;
using Koan.AI.Contracts;
using Koan.Data.AI.Attributes;
using Koan.Data.AI.Migration;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Data.AI.Tests;

/// <summary>
/// Regression: saving a synchronous <c>[Embedding]</c> entity must NOT recurse. The embedding hook runs inside
/// the entity's <c>AfterUpsert</c> event — the row is already persisted — so it must store the vector only.
/// It previously called <c>VectorData.SaveWithVector</c>, which re-<c>Save()</c>s the entity, re-firing
/// <c>AfterUpsert</c> → the hook → … → <b>StackOverflow</b> (which terminates the process; this test would crash
/// the run, not merely fail). The fix routes the hook through the vector-only <c>VectorData.Save</c>.
/// </summary>
[Collection(nameof(DataAiHostLifecycleCollection))]
public sealed class EmbeddingHookReentrancySpec : IAsyncLifetime
{
    private IntegrationHost? _host;
    private readonly FakeEmbedAdapter _embedder = new();
    private int _asyncDomainUpserts;

    public async ValueTask InitializeAsync()
    {
        // The [Embedding] registry is populated by a source generator that does not run on this test assembly
        // through a ProjectReference, so register the type explicitly before AddKoan() wires its hooks.
        Koan.Data.AI.EmbeddingRegistry.RegisterTypes([typeof(ReentrancyDoc), typeof(AsyncEmbeddingDoc)]);

        _host = await KoanIntegrationHost.Configure()
            .WithSetting("Koan:Environment", "Test")
            .WithSetting("Koan:Data:Sources:Default:Adapter", "inmemory")
            .ConfigureServices(s =>
            {
                s.AddLogging();
                s.AddKoan(() =>
                    AsyncEmbeddingDoc.Lifecycle.AfterUpsert(_ => Interlocked.Increment(ref _asyncDomainUpserts)));
            })
            .StartAsync();

        // Register an in-process fake embedder as a routable AI source (Provider == adapter Id) so the sync
        // [Embedding] hook can resolve Client.Embed and reach the vector-store step where the recursion lived.
        var caps = new Dictionary<string, AiCapabilityConfig>(StringComparer.OrdinalIgnoreCase)
        {
            ["Embedding"] = new AiCapabilityConfig { Model = "test-embed", AutoDownload = false },
        };
        _host.Services.GetRequiredService<IAiAdapterRegistry>().Add(_embedder);
        _host.Services.GetRequiredService<IAiSourceRegistry>().RegisterSource(new AiSourceDefinition
        {
            Name = "fake",
            Provider = "fake",
            Priority = 50,
            Policy = "Fallback",
            Members = new List<AiMemberDefinition>
            {
                new() { Name = "fake::inproc", ConnectionString = "inproc://fake", Order = 0, Capabilities = caps, HealthState = MemberHealthState.Healthy },
            },
            Capabilities = caps,
            Origin = "in-process",
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null) await _host.DisposeAsync();
    }

    [Fact]
    public async Task Saving_a_sync_embedding_entity_stores_the_vector_without_recursing()
    {
        // Sanity: the hook must actually be wired for this type, else the test trivially passes.
        Koan.Data.AI.EmbeddingRegistry.GetRegisteredTypes().Should().Contain(typeof(ReentrancyDoc),
            "the [Embedding] hook must be registered for this entity, or the test exercises nothing");

        // Pre-fix this Save recursed to a StackOverflow; reaching the assertions at all is the core guard.
        await new ReentrancyDoc { Id = "d1", Text = "ripe red tomatoes on the vine" }.Save();

        // And the hook actually stored exactly one vector (vector-only path still works).
        var hit = await Vector<ReentrancyDoc>.Search(new[] { 0.1f, 0.2f, 0.3f }, topK: 5);
        hit.Matches.Should().ContainSingle(m => (string)(object)m.Id == "d1",
            "the AfterUpsert hook must store the embedding once via the vector-only path");
    }

    [Fact]
    public async Task ReEmbed_processes_only_the_supplied_finite_set_and_reports_its_outcome()
    {
        var documents = new[]
        {
            new ReentrancyDoc { Id = "subset-1", Text = "first selected document" },
            new ReentrancyDoc { Id = "subset-2", Text = "second selected document" },
        };

        var before = _embedder.RequestedInputs;
        var result = await EmbeddingMigrator.ReEmbed(documents, batchSize: 1);

        result.Success.Should().BeTrue();
        result.TotalEntities.Should().Be(2);
        result.SuccessfulEntities.Should().Be(2);
        result.FailedEntities.Should().Be(0);
        result.TotalBatches.Should().Be(2);
        result.ProcessedBatches.Should().Be(2);
        (_embedder.RequestedInputs - before).Should().Be(2,
            "the finite-set operation must not discover or re-embed unrelated entities");

        var hit = await Vector<ReentrancyDoc>.Search(new[] { 0.1f, 0.2f, 0.3f }, topK: 10);
        hit.Matches.Select(match => (string)(object)match.Id)
            .Should().Contain(new[] { "subset-1", "subset-2" });
    }

    [Fact]
    public async Task ReEmbed_rejects_an_invalid_batch_before_calling_the_provider()
    {
        var before = _embedder.RequestedInputs;

        var action = () => EmbeddingMigrator.ReEmbed(
            new[] { new ReentrancyDoc { Id = "invalid-batch", Text = "must not be embedded" } },
            batchSize: 0);

        await action.Should().ThrowAsync<ArgumentOutOfRangeException>();
        _embedder.RequestedInputs.Should().Be(before);
    }

    [Fact]
    public async Task Async_embedding_writes_the_vector_without_resaving_the_domain_entity()
    {
        const string id = "async-vector-only";

        await new AsyncEmbeddingDoc { Id = id, Text = "deferred garden notes" }.Save();

        EmbedJob<AsyncEmbeddingDoc>? job = null;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            job = await EmbedJob<AsyncEmbeddingDoc>.Get(EmbedJob<AsyncEmbeddingDoc>.MakeId(id));
            if (job?.Status == EmbedJobStatus.Completed)
            {
                break;
            }

            await Task.Delay(50);
        }

        job.Should().NotBeNull();
        job!.Status.Should().Be(EmbedJobStatus.Completed);
        Volatile.Read(ref _asyncDomainUpserts).Should().Be(1,
            "the worker owns a vector write, not a second persistence operation over the domain Entity");

        var hit = await Vector<AsyncEmbeddingDoc>.Search(new[] { 0.1f, 0.2f, 0.3f }, topK: 5);
        hit.Matches.Should().ContainSingle(match => (string)(object)match.Id == id);
    }
}

/// <summary>A 1-arg <see cref="Entity{TEntity}"/> with a synchronous <c>[Embedding]</c>, routed to the in-process vector floor.</summary>
[Embedding(Template = "{Text}", Model = "test-embed")]
[VectorAdapter("inmemory")]
public sealed class ReentrancyDoc : Entity<ReentrancyDoc>
{
    public string Text { get; set; } = "";
}

[Embedding(Async = true, Template = "{Text}", Model = "test-embed")]
[VectorAdapter("inmemory")]
public sealed class AsyncEmbeddingDoc : Entity<AsyncEmbeddingDoc>
{
    public string Text { get; set; } = "";
}

internal sealed class FakeEmbedAdapter : IEmbedAdapter
{
    private int _requestedInputs;

    public int RequestedInputs => Volatile.Read(ref _requestedInputs);
    public string Id => "fake";
    public string Name => "fake";
    public string Type => "fake";
    public IReadOnlySet<string> Capabilities { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { AiCapability.Embed };

    public Task<IReadOnlyList<AiModelDescriptor>> ListModels(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AiModelDescriptor>>(new[]
        {
            new AiModelDescriptor { Name = "test-embed", AdapterId = Id, EmbeddingDim = 3 },
        });

    public Task<AiEmbeddingsResponse> Embed(AiEmbeddingsRequest request, CancellationToken ct = default)
    {
        Interlocked.Add(ref _requestedInputs, request.Input.Count);
        return Task.FromResult(new AiEmbeddingsResponse
        {
            Vectors = request.Input.Select(_ => new[] { 0.1f, 0.2f, 0.3f }).ToList(),
            Dimension = 3,
            Model = "test-embed",
        });
    }
}
