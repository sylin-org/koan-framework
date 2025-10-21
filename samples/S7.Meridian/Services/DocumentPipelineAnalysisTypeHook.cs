using System;
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
        if (string.IsNullOrWhiteSpace(model.AnalysisTypeId))
        {
            ctx.Warn("AnalysisTypeId is required for Meridian pipelines.");
            throw new InvalidOperationException("AnalysisTypeId is required for Meridian pipelines.");
        }

        var ct = ctx.Ct;
        var analysisType = await AnalysisType.Get(model.AnalysisTypeId, ct);
        if (analysisType is null)
        {
            throw new InvalidOperationException($"AnalysisType '{model.AnalysisTypeId}' was not found.");
        }

        var previous = !string.IsNullOrWhiteSpace(model.Id)
            ? await DocumentPipeline.Get(model.Id, ct)
            : null;

        var analysisChanged = previous is null ||
                              !string.Equals(previous.AnalysisTypeId, model.AnalysisTypeId, StringComparison.OrdinalIgnoreCase) ||
                              previous.AnalysisTypeVersion != analysisType.Version;

        model.AnalysisTypeVersion = analysisType.Version;
        model.AnalysisInstructions = (analysisType.Instructions ?? string.Empty).Trim();
        model.AnalysisTags = analysisType.Tags is { Count: > 0 }
            ? new List<string>(analysisType.Tags)
            : new List<string>();
        model.RequiredSourceTypes = analysisType.RequiredSourceTypes is { Count: > 0 }
            ? new List<string>(analysisType.RequiredSourceTypes)
            : new List<string>();

        if (analysisChanged || string.IsNullOrWhiteSpace(model.TemplateMarkdown))
        {
            var template = analysisType.OutputTemplate;
            if (string.IsNullOrWhiteSpace(template))
            {
                _logger.LogWarning("AnalysisType {AnalysisTypeId} has no output template; retaining existing pipeline template.", analysisType.Id);
            }
            else
            {
                model.TemplateMarkdown = template;
            }
        }

        if (string.IsNullOrWhiteSpace(model.TemplateMarkdown))
        {
            model.TemplateMarkdown = "# Meridian Deliverable\n";
        }

        model.UpdatedAt = DateTime.UtcNow;
    }
}
