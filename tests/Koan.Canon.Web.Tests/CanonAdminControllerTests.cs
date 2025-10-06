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
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Koan.Canon.Web.Tests;

public class CanonAdminControllerTests
{
    [Fact]
    public async Task GetRecords_ShouldReturnReplayPayload()
    {
        var runtime = new RecordingCanonRuntime();
        runtime.ReplayRecords.Add(new CanonizationRecord
        {
            CanonicalId = "canon-1",
            EntityType = typeof(TestCanonEntity).FullName!,
            Phase = CanonPipelinePhase.Intake,
            Outcome = CanonizationOutcome.Canonized,
            StageStatus = CanonStageStatus.Completed,
            Metadata = new CanonMetadata()
        });

        var catalog = new CanonModelCatalog(Array.Empty<CanonModelDescriptor>());
        var controller = new CanonAdminController(runtime, catalog);

        var result = await controller.GetRecords(null, null, CancellationToken.None);

        result.Result.Should().BeAssignableTo<OkObjectResult>();
        var records = ((OkObjectResult)result.Result!).Value.Should().BeAssignableTo<IEnumerable<CanonizationRecord>>().Subject.ToList();
        records.Should().HaveCount(1);
        records[0].CanonicalId.Should().Be("canon-1");
    }

    [Fact]
    public async Task RebuildViews_ShouldInvokeRuntimeAndReturnAccepted()
    {
        var runtime = new RecordingCanonRuntime();
        var descriptor = new CanonModelDescriptor(typeof(TestCanonEntity), "test", "Test", WebConstants.Routes.CanonPrefix + "/test", isValueObject: false);
        var catalog = new CanonModelCatalog(new[] { descriptor });
        var controller = new CanonAdminController(runtime, catalog);

        var request = new CanonAdminController.RebuildViewsRequest("canon-1", new[] { "view-a" });
        var result = await controller.RebuildViews("test", request, CancellationToken.None);

        result.Should().BeOfType<AcceptedResult>();
        runtime.RebuildCalls.Should().ContainSingle(call => call.EntityType == typeof(TestCanonEntity) && call.CanonicalId == "canon-1" && call.Views!.Single() == "view-a");
    }

    private sealed class RecordingCanonRuntime : ICanonRuntime
    {
        public List<CanonizationRecord> ReplayRecords { get; } = new();
        public List<(Type EntityType, string CanonicalId, string[]? Views)> RebuildCalls { get; } = new();

        public Task<CanonizationResult<T>> Canonize<T>(T entity, CanonizationOptions? options = null, CancellationToken cancellationToken = default)
            where T : CanonEntity<T>, new()
        {
            throw new NotImplementedException();
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

        public IDisposable RegisterObserver(ICanonPipelineObserver observer)
            => new DummyDisposable();

        private sealed class DummyDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    private sealed class TestCanonEntity : CanonEntity<TestCanonEntity>
    {
    }
}
