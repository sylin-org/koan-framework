using System;
using System.Collections.Generic;
using System.Linq;
using Koan.Canon.Domain.Annotations;
using Koan.Canon.Domain.Metadata;
using Koan.Canon.Domain.Runtime;
using Koan.Canon.Web.Catalog;
using Koan.Canon.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Koan.Canon.Web.Controllers;

/// <summary>
/// Exposes discovery metadata for canonical models registered with the runtime.
/// </summary>
[ApiController]
[Route(WebConstants.Routes.Models)]
public sealed class CanonModelsController : ControllerBase
{
    private readonly ICanonModelCatalog _catalog;
    private readonly CanonRuntimeConfiguration _configuration;

    public CanonModelsController(ICanonModelCatalog catalog, CanonRuntimeConfiguration configuration)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    [HttpGet]
    public ActionResult<IEnumerable<CanonModelSummary>> GetAll()
    {
        var summaries = _catalog.All
            .Select(descriptor => CreateSummary(descriptor))
            .OrderBy(summary => summary.Slug)
            .ToArray();
        return Ok(summaries);
    }

    [HttpGet("{slug}")]
    public ActionResult<CanonModelDetail> GetBySlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return BadRequest(new { error = "Slug must be provided." });
        }

        if (!_catalog.TryGetBySlug(slug, out var descriptor))
        {
            return NotFound();
        }

        var metadata = ResolveMetadata(descriptor.ModelType);
        var aggregationMetadata = CanonModelAggregationMetadata.For(descriptor.ModelType);
        var detail = new CanonModelDetail(
            descriptor.Slug,
            descriptor.DisplayName,
            descriptor.Route,
            descriptor.ModelType.FullName ?? descriptor.ModelType.Name,
            descriptor.IsValueObject,
            metadata?.HasSteps ?? false,
            metadata?.Phases ?? Array.Empty<CanonPipelinePhase>(),
            aggregationMetadata.AggregationKeyNames,
            metadata?.AggregationPolicies ?? aggregationMetadata.PolicyByName,
            metadata?.AggregationPolicyDetails ?? aggregationMetadata.PolicyDescriptorsByName,
            metadata?.AuditEnabled ?? aggregationMetadata.AuditEnabled);

        return Ok(detail);
    }

    private CanonModelSummary CreateSummary(CanonModelDescriptor descriptor)
    {
        var metadata = ResolveMetadata(descriptor.ModelType);
        var aggregationMetadata = CanonModelAggregationMetadata.For(descriptor.ModelType);
        return new CanonModelSummary(
            descriptor.Slug,
            descriptor.DisplayName,
            descriptor.Route,
            descriptor.IsValueObject,
            metadata?.HasSteps ?? false,
            aggregationMetadata.AggregationKeyNames,
            metadata?.AuditEnabled ?? aggregationMetadata.AuditEnabled);
    }

    private CanonPipelineMetadata? ResolveMetadata(Type modelType)
        => _configuration.PipelineMetadata.TryGetValue(modelType, out var metadata) ? metadata : null;

    public sealed record CanonModelSummary(
        string Slug,
        string DisplayName,
        string Route,
        bool IsValueObject,
        bool HasPipeline,
        IReadOnlyList<string> AggregationKeys,
        bool AuditEnabled);

    public sealed record CanonModelDetail(
        string Slug,
        string DisplayName,
        string Route,
        string Type,
        bool IsValueObject,
        bool HasPipeline,
        IReadOnlyList<CanonPipelinePhase> Phases,
        IReadOnlyList<string> AggregationKeys,
        IReadOnlyDictionary<string, AggregationPolicyKind> AggregationPolicies,
        IReadOnlyDictionary<string, AggregationPolicyDescriptor> AggregationPolicyDetails,
        bool AuditEnabled);
}
