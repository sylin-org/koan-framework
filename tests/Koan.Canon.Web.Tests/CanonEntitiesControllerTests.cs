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
        var runtime = new RecordingCanonRuntime();
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

        runtime.CanonizeCalls.Should().ContainSingle();
        runtime.CanonizeCalls[0].Entity.Should().BeOfType<TestCanonEntity>();
        runtime.CanonizeCalls[0].Options.Should().NotBeNull();
        runtime.CanonizeCalls[0].Options!.Origin.Should().Be("ingest");
        runtime.CanonizeCalls[0].Options!.ForceRebuild.Should().BeTrue();
        runtime.CanonizeCalls[0].Options!.SkipDistribution.Should().BeTrue();
        runtime.CanonizeCalls[0].Options!.StageBehavior.Should().Be(CanonStageBehavior.StageOnly);
        runtime.CanonizeCalls[0].Options!.RequestedViews.Should().BeEquivalentTo(new[] { "canonical", "lineage" });
        runtime.CanonizeCalls[0].Options!.Tags.Should().ContainKey("priority").WhoseValue.Should().Be("high");
        runtime.CanonizeCalls[0].Options!.Tags.Should().ContainKey("region").WhoseValue.Should().Be("us");
        runtime.CanonizeCalls[0].Options!.CorrelationId.Should().Be("correlation-123");
    }

    [Fact]
    public async Task UpsertMany_ShouldCanonizeEachEntity()
    {
        var runtime = new RecordingCanonRuntime();
        var descriptor = new CanonModelDescriptor(typeof(TestCanonEntity), "test-canon", nameof(TestCanonEntity), WebConstants.Routes.CanonPrefix + "/test-canon", isValueObject: false);
        var catalog = new CanonModelCatalog(new[] { descriptor });
        var controller = new TestCanonController(runtime, catalog)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = await controller.UpsertMany(new[]
        {
            new TestCanonEntity { Name = "one" },
            new TestCanonEntity { Name = "two" }
        }, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var payload = ((OkObjectResult)result).Value.Should().BeAssignableTo<IEnumerable<CanonEntitiesController<TestCanonEntity>.CanonizationResponse<TestCanonEntity>>>().Subject.ToList();
        payload.Should().HaveCount(2);
        runtime.CanonizeCalls.Should().HaveCount(2);
        runtime.CanonizeCalls.Select(call => ((TestCanonEntity)call.Entity).Name).Should().BeEquivalentTo(new[] { "one", "two" });
    }

    [Fact]
    public async Task Upsert_ShouldBubbleRuntimeExceptions()
    {
        var runtime = new RecordingCanonRuntime { ExceptionToThrow = new InvalidOperationException("failure") };
        var descriptor = new CanonModelDescriptor(typeof(TestCanonEntity), "test-canon", nameof(TestCanonEntity), WebConstants.Routes.CanonPrefix + "/test-canon", isValueObject: false);
        var catalog = new CanonModelCatalog(new[] { descriptor });
        var controller = new TestCanonController(runtime, catalog)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var act = async () => await controller.Upsert(new TestCanonEntity(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("failure");
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

    private sealed class RecordingCanonRuntime : ICanonRuntime
    {
        public List<(object Entity, CanonizationOptions? Options)> CanonizeCalls { get; } = new();
        public List<(Type EntityType, string CanonicalId, string[]? Views)> RebuildCalls { get; } = new();
        public List<CanonizationRecord> ReplayRecords { get; } = new();
        public Exception? ExceptionToThrow { get; set; }

        public Task<CanonizationResult<T>> Canonize<T>(T entity, CanonizationOptions? options = null, CancellationToken cancellationToken = default)
            where T : CanonEntity<T>, new()
        {
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            CanonizeCalls.Add((entity, options));
            var metadata = entity.Metadata.Clone();
            return Task.FromResult(new CanonizationResult<T>(entity, CanonizationOutcome.Canonized, metadata, Array.Empty<CanonizationEvent>()));
        }

        public Task RebuildViews<T>(string canonicalId, string[]? views = null, CancellationToken cancellationToken = default)
            where T : CanonEntity<T>, new()
        {
            RebuildCalls.Add((typeof(T), canonicalId, views));
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<CanonizationRecord> Replay(DateTimeOffset? from = null, DateTimeOffset? to = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var record in ReplayRecords)
            {
                yield return record;
                await Task.Yield();
            }
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
