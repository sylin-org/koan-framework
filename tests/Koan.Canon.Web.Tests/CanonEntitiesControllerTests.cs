using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Koan.Canon.Domain.Metadata;
using Koan.Canon.Domain.Model;
using Koan.Canon.Domain.Runtime;
using Koan.Canon.Web.Catalog;
using Koan.Canon.Web.Controllers;
using Koan.Canon.Web.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Koan.Canon.Web.Tests;

public class CanonEntitiesControllerTests
{
    [Fact]
    public async Task Upsert_ShouldInvokeRuntimeWithParsedOptions()
    {
        var runtime = new FakeCanonRuntime();
    var descriptor = new CanonModelDescriptor(typeof(TestCanonEntity), "test-canon", nameof(TestCanonEntity), WebConstants.Routes.CanonPrefix + "/test-canon", isValueObject: false);
        var catalog = new CanonModelCatalog(new[] { descriptor });
        var controller = new TestCanonController(runtime, catalog)
        {
            ControllerContext = new ControllerContext { HttpContext = CreateHttpContext() }
        };

        var result = await controller.Upsert(new TestCanonEntity { Name = "sample" }, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var payload = ((OkObjectResult)result).Value.Should().BeOfType<CanonEntitiesController<TestCanonEntity>.CanonizationResponse<TestCanonEntity>>().Subject;
        payload.Canonical.Name.Should().Be("sample");
        payload.Outcome.Should().Be(CanonizationOutcome.Canonized);

        runtime.LastEntity.Should().BeOfType<TestCanonEntity>();
        runtime.LastOptions.Should().NotBeNull();
        runtime.LastOptions!.Origin.Should().Be("ingest");
        runtime.LastOptions!.ForceRebuild.Should().BeTrue();
        runtime.LastOptions!.SkipDistribution.Should().BeTrue();
        runtime.LastOptions!.StageBehavior.Should().Be(CanonStageBehavior.StageOnly);
        runtime.LastOptions!.RequestedViews.Should().BeEquivalentTo(new[] { "canonical", "lineage" });
        runtime.LastOptions!.Tags.Should().ContainKey("priority").WhoseValue.Should().Be("high");
        runtime.LastOptions!.Tags.Should().ContainKey("region").WhoseValue.Should().Be("us");
        runtime.LastOptions!.CorrelationId.Should().Be("correlation-123");
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?origin=ingest&forceRebuild=true&skipDistribution=true&stageBehavior=StageOnly&views=canonical,lineage&tag.priority=high");
        context.Request.Headers["X-Correlation-ID"] = new StringValues("correlation-123");
        context.Request.Headers["X-Canon-Tag-Region"] = new StringValues("us");
        return context;
    }

    private sealed class TestCanonController : CanonEntitiesController<TestCanonEntity>
    {
        public TestCanonController(ICanonRuntime runtime, ICanonModelCatalog catalog) : base(runtime, catalog)
        {
        }
    }

    private sealed class TestCanonEntity : CanonEntity<TestCanonEntity>
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class FakeCanonRuntime : ICanonRuntime
    {
        public object? LastEntity { get; private set; }
        public CanonizationOptions? LastOptions { get; private set; }

        public Task<CanonizationResult<T>> Canonize<T>(T entity, CanonizationOptions? options = null, CancellationToken cancellationToken = default)
            where T : CanonEntity<T>, new()
        {
            LastEntity = entity;
            LastOptions = options;
            var metadata = entity.Metadata.Clone();
            return Task.FromResult(new CanonizationResult<T>(entity, CanonizationOutcome.Canonized, metadata, Array.Empty<CanonizationEvent>()));
        }

        public Task RebuildViews<T>(string canonicalId, string[]? views = null, CancellationToken cancellationToken = default)
            where T : CanonEntity<T>, new()
            => Task.CompletedTask;

        public async IAsyncEnumerable<CanonizationRecord> Replay(DateTimeOffset? from = null, DateTimeOffset? to = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield break;
        }

        public IDisposable RegisterObserver(ICanonPipelineObserver observer) => new DummyDisposable();

        private sealed class DummyDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
