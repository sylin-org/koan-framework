using System;
using System.Collections.Generic;
using Koan.Samples.Meridian.Infrastructure;
using Koan.Samples.Meridian.Models;
using Koan.Web.Hooks;
using Microsoft.Extensions.Logging;

namespace Koan.Samples.Meridian.Services;

public sealed class DocumentPipelineAnalysisTypeHook : IModelHook<DocumentPipeline>
{
    private readonly ILogger<DocumentPipelineAnalysisTypeHook> _logger;

    public DocumentPipelineAnalysisTypeHook(ILogger<DocumentPipelineAnalysisTypeHook> logger)
    {
        _logger = logger;
    }

    public int Order => 0;

    public Task OnAfterDeleteAsync(HookContext<DocumentPipeline> ctx, DocumentPipeline model) => Task.CompletedTask;
    public Task OnAfterFetchAsync(HookContext<DocumentPipeline> ctx, DocumentPipeline? model) => Task.CompletedTask;
    public Task OnAfterPatchAsync(HookContext<DocumentPipeline> ctx, DocumentPipeline model) => Task.CompletedTask;
    public Task OnAfterSaveAsync(HookContext<DocumentPipeline> ctx, DocumentPipeline model) => Task.CompletedTask;
    public Task OnBeforeDeleteAsync(HookContext<DocumentPipeline> ctx, DocumentPipeline model) => Task.CompletedTask;
    public Task OnBeforeFetchAsync(HookContext<DocumentPipeline> ctx, string id) => Task.CompletedTask;
    public Task OnBeforePatchAsync(HookContext<DocumentPipeline> ctx, string id, object patch) => Task.CompletedTask;

    public async Task OnBeforeSaveAsync(HookContext<DocumentPipeline> ctx, DocumentPipeline model)
    {
        var ct = ctx.Ct;
        var previous = !string.IsNullOrWhiteSpace(model.Id)
            ? await DocumentPipeline.Get(model.Id, ct)
            : null;

        // AnalysisType enforcement
        if (string.IsNullOrWhiteSpace(model.AnalysisTypeId))
        {
            ctx.Warn("AnalysisTypeId is required for Meridian pipelines.");
            throw new InvalidOperationException("AnalysisTypeId is required for Meridian pipelines.");
        }

        var analysisType = await AnalysisType.Get(model.AnalysisTypeId, ct);
        if (analysisType is null)
        {
            throw new InvalidOperationException($"AnalysisType '{model.AnalysisTypeId}' was not found.");
        }

        var analysisChanged = previous is null ||
                              !string.Equals(previous.AnalysisTypeId, model.AnalysisTypeId, StringComparison.OrdinalIgnoreCase) ||
                              previous.AnalysisTypeVersion != analysisType.Version;

        model.AnalysisTypeVersion = analysisType.Version;
        model.AnalysisInstructions = (analysisType.Instructions ?? string.Empty).Trim();
        model.AnalysisTags = analysisType.Tags is { Count: > 0 }
            ? new List<string>(analysisType.Tags)
            : new List<string>();

        // DeliverableType (optional but preferred)
        DeliverableType? deliverableType = null;
        var deliverableChanged = false;

        if (!string.IsNullOrWhiteSpace(model.DeliverableTypeId))
        {
            deliverableType = await DeliverableType.Get(model.DeliverableTypeId, ct);
            if (deliverableType is null)
            {
                throw new InvalidOperationException($"DeliverableType '{model.DeliverableTypeId}' was not found.");
            }

            deliverableChanged = previous is null ||
                                 !string.Equals(previous.DeliverableTypeId, model.DeliverableTypeId, StringComparison.OrdinalIgnoreCase) ||
                                 previous.DeliverableTypeVersion != deliverableType.Version;

            model.DeliverableTypeVersion = deliverableType.Version;
        }
        else
        {
            // Default to analysis template if no deliverable type provided
            model.DeliverableTypeId = model.AnalysisTypeId;
            model.DeliverableTypeVersion = model.AnalysisTypeVersion;
        }

        if (deliverableType is not null && (deliverableChanged || string.IsNullOrWhiteSpace(model.SchemaJson)))
        {
            if (!string.IsNullOrWhiteSpace(deliverableType.JsonSchema))
            {
                model.SchemaJson = deliverableType.JsonSchema;
            }
        }
        else if (analysisChanged && string.IsNullOrWhiteSpace(model.SchemaJson))
        {
            // Use schema from AnalysisType if available
            if (!string.IsNullOrWhiteSpace(analysisType.JsonSchema))
            {
                model.SchemaJson = analysisType.JsonSchema;
            }
        }

        model.SchemaJson = FieldPathCanonicalizer.CanonicalizeJsonSchema(model.SchemaJson);

        if (deliverableType is not null && (deliverableChanged || string.IsNullOrWhiteSpace(model.TemplateMarkdown)))
        {
            if (!string.IsNullOrWhiteSpace(deliverableType.TemplateMd))
            {
                model.TemplateMarkdown = FieldPathCanonicalizer.CanonicalizeTemplatePlaceholders(deliverableType.TemplateMd);
            }
        }
        else if (analysisChanged || string.IsNullOrWhiteSpace(model.TemplateMarkdown))
        {
            var template = analysisType.OutputTemplate;
            if (string.IsNullOrWhiteSpace(template))
            {
                _logger.LogWarning("AnalysisType {AnalysisTypeId} has no output template; retaining existing pipeline template.", analysisType.Id);
            }
            else
            {
                model.TemplateMarkdown = FieldPathCanonicalizer.CanonicalizeTemplatePlaceholders(template);
            }
        }

        if (string.IsNullOrWhiteSpace(model.TemplateMarkdown))
        {
            model.TemplateMarkdown = "# Meridian Deliverable\n";
        }
        else
        {
            model.TemplateMarkdown = FieldPathCanonicalizer.CanonicalizeTemplatePlaceholders(model.TemplateMarkdown);
        }

        model.UpdatedAt = DateTime.UtcNow;
    }
}
