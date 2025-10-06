using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Koan.Canon.Domain.Annotations;
using Koan.Canon.Domain.Audit;
using Koan.Canon.Domain.Metadata;
using Koan.Canon.Domain.Model;
using Koan.Canon.Domain.Runtime;
using Koan.Canon.Web.Catalog;
using Koan.Canon.Web.Controllers;
using Koan.Canon.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Koan.Canon.Web.Tests;

public class CanonModelsControllerTests
{
    [Fact]
    public void GetAll_ShouldReturnSummaries()
    {
        var descriptor = new CanonModelDescriptor(typeof(TestCanonEntity), "test", "Test", WebConstants.Routes.CanonPrefix + "/test", isValueObject: false);
        var catalog = new CanonModelCatalog(new[] { descriptor });
        var metadata = new CanonPipelineMetadata(
            typeof(TestCanonEntity),
            new[] { CanonPipelinePhase.Intake },
            hasSteps: true,
            new[] { nameof(TestCanonEntity.ExternalId) },
            new Dictionary<string, AggregationPolicyKind>(),
            new Dictionary<string, AggregationPolicyDescriptor>(),
            auditEnabled: false);
        var configuration = new CanonRuntimeConfiguration(CanonizationOptions.Default, new Dictionary<Type, ICanonPipelineDescriptor>(), new Dictionary<Type, CanonPipelineMetadata> { [typeof(TestCanonEntity)] = metadata }, 10, new StubPersistence(), new NullAuditSink());

        var controller = new CanonModelsController(catalog, configuration);

        var result = controller.GetAll();

        result.Result.Should().BeAssignableTo<OkObjectResult>();
        var summaries = ((OkObjectResult)result.Result!).Value.Should().BeAssignableTo<IEnumerable<CanonModelsController.CanonModelSummary>>().Subject.ToList();
        summaries.Should().ContainSingle(summary => summary.Slug == "test" && summary.HasPipeline && summary.AggregationKeys.Contains(nameof(TestCanonEntity.ExternalId)));
    }

    [Fact]
    public void GetBySlug_ShouldReturnDetail()
    {
        var descriptor = new CanonModelDescriptor(typeof(TestCanonEntity), "test", "Test", WebConstants.Routes.CanonPrefix + "/test", isValueObject: false);
        var catalog = new CanonModelCatalog(new[] { descriptor });
        var metadata = new CanonPipelineMetadata(
            typeof(TestCanonEntity),
            new[] { CanonPipelinePhase.Intake, CanonPipelinePhase.Validation },
            hasSteps: true,
            new[] { nameof(TestCanonEntity.ExternalId) },
            new Dictionary<string, AggregationPolicyKind> { [nameof(TestCanonEntity.DisplayName)] = AggregationPolicyKind.Latest },
            new Dictionary<string, AggregationPolicyDescriptor>
            {
                [nameof(TestCanonEntity.DisplayName)] = new AggregationPolicyDescriptor(
                    AggregationPolicyKind.Latest,
                    Array.Empty<string>(),
                    AggregationPolicyKind.Latest)
            },
            auditEnabled: true);
        var configuration = new CanonRuntimeConfiguration(CanonizationOptions.Default, new Dictionary<Type, ICanonPipelineDescriptor>(), new Dictionary<Type, CanonPipelineMetadata> { [typeof(TestCanonEntity)] = metadata }, 10, new StubPersistence(), new NullAuditSink());

        var controller = new CanonModelsController(catalog, configuration);

        var result = controller.GetBySlug("test");

        result.Result.Should().BeAssignableTo<OkObjectResult>();
        var detail = ((OkObjectResult)result.Result!).Value.Should().BeOfType<CanonModelsController.CanonModelDetail>().Subject;
        detail.Slug.Should().Be("test");
        detail.Phases.Should().BeEquivalentTo(new[] { CanonPipelinePhase.Intake, CanonPipelinePhase.Validation });
        detail.AggregationKeys.Should().Contain(nameof(TestCanonEntity.ExternalId));
        detail.AggregationPolicies.Should().ContainKey(nameof(TestCanonEntity.DisplayName));
        detail.AuditEnabled.Should().BeTrue();
    }

    [Fact]
    public void GetBySlug_WithUnknownSlug_ShouldReturnNotFound()
    {
        var catalog = new CanonModelCatalog(Array.Empty<CanonModelDescriptor>());
        var configuration = new CanonRuntimeConfiguration(CanonizationOptions.Default, new Dictionary<Type, ICanonPipelineDescriptor>(), new Dictionary<Type, CanonPipelineMetadata>(), 10, new StubPersistence(), new NullAuditSink());

        var controller = new CanonModelsController(catalog, configuration);

        var result = controller.GetBySlug("missing");

        result.Result.Should().BeAssignableTo<NotFoundResult>();
    }

    private sealed class StubPersistence : ICanonPersistence
    {
        public Task<TModel> PersistCanonicalAsync<TModel>(TModel entity, CancellationToken cancellationToken)
            where TModel : CanonEntity<TModel>, new()
            => throw new NotImplementedException();

        public Task<CanonStage<TModel>> PersistStageAsync<TModel>(CanonStage<TModel> stage, CancellationToken cancellationToken)
            where TModel : CanonEntity<TModel>, new()
            => throw new NotImplementedException();

        public Task<CanonIndex?> GetIndexAsync(string entityType, string key, CancellationToken cancellationToken)
            => Task.FromResult<CanonIndex?>(null);

        public Task UpsertIndexAsync(CanonIndex index, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class NullAuditSink : ICanonAuditSink
    {
        public Task WriteAsync(IReadOnlyList<CanonAuditEntry> entries, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class TestCanonEntity : CanonEntity<TestCanonEntity>
    {
        [AggregationKey]
        public string ExternalId { get; set; } = string.Empty;

        [AggregationPolicy]
        public string? DisplayName { get; set; }
    }
}
