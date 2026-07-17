using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core.Logging;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Vector.Connector.Qdrant;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Koan.Tests.Data.Core.Specs.Vector;

/// <summary>
/// ARCH-0103 §9.14 — the WIRING proof for the warn-on-pin hardening: a real production vector adapter's pinned
/// name-getter must actually CALL <see cref="Koan.Data.Vector.Naming.VectorAdapterNaming.WarnIfPinnedNameDefeatsIsolation{TEntity}"/>.
/// <see cref="VectorNamePinWarningSpec"/> proves the shared warner/predicate in isolation; this proves the call site is
/// live end-to-end — through the real <see cref="QdrantVectorRepository{TEntity,TKey}"/> built by its public factory,
/// with a stub <see cref="IHttpClientFactory"/> so no Qdrant/Docker is needed (the <c>CollectionName</c> getter resolves
/// the URL — and fires the warner — before any network call). Qdrant is the exemplar: <c>MilvusVectorRepository.CollectionName</c>
/// is the byte-identical block, and <c>SearchEngineVectorRepository.IndexName</c> calls the same warner from its pinned
/// branch (both inspection-verified against this proof).
/// </summary>
public sealed class VectorNamePinWiringSpec
{
    private sealed class QdrantPinMarker : IEntity<string> { public string Id { get; set; } = ""; }

    private static readonly float[] Point = [1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f];

    [Fact]
    public async Task Qdrant_pinned_collection_under_a_partition_emits_the_warning_from_the_real_getter()
    {
        var captured = new List<(string? Outcome, (string Key, object? Value)[] Context)>();
        KoanLog.TestSink = (stage, level, action, outcome, context) =>
        {
            if (action != "vector.name.pinned") return;
            if (!context.Any(kv => kv.Key == "entity" && string.Equals(kv.Value?.ToString(), nameof(QdrantPinMarker)))) return;
            lock (captured) captured.Add((outcome, context));
        };
        try
        {
            var services = new ServiceCollection();
            services.AddSingleton<IHttpClientFactory>(new StubHttpClientFactory());
            services.AddSingleton<IOptions<QdrantOptions>>(Options.Create(new QdrantOptions
            {
                Endpoint = "http://localhost:1",
                ConnectionString = "http://localhost:1",
                CollectionName = "pinned-coll",   // the footgun: a static name bypasses the partition+source fold
                Dimension = 8,
            }));
            using var sp = services.BuildServiceProvider();

            // Build the REAL Qdrant repository via its public factory (returns the public IVectorSearchRepository).
            var repo = new QdrantVectorAdapterFactory().Create<QdrantPinMarker, string>(sp);

            using (EntityContext.Partition("acme"))
            {
                // The CollectionName getter resolves the URL (and fires the warner) before the HTTP send; the stub returns
                // 200 so the path proceeds, but even a later failure would not unfire the already-emitted warning.
                try { await repo.Upsert("id1", Point); } catch { /* stub response need not satisfy Qdrant's wire contract */ }
            }
        }
        finally { KoanLog.TestSink = null; }

        List<(string? Outcome, (string Key, object? Value)[] Context)> snapshot;
        lock (captured) snapshot = captured.ToList();

        snapshot.Should().ContainSingle("the real Qdrant pinned-CollectionName getter must call the shared warner once");
        var ctx = snapshot[0].Context.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString());
        snapshot[0].Outcome.Should().Be("isolation-defeated");
        ctx.Should().Contain("entity", nameof(QdrantPinMarker));
        ctx.Should().Contain("option", "CollectionName");
        ctx.Should().Contain("pinnedName", "pinned-coll");
        ctx.Should().Contain("activeDiscriminator", "partition");
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StubHandler());
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"result\":{\"status\":\"ok\"},\"status\":\"ok\",\"time\":0}"),
            });
    }
}
