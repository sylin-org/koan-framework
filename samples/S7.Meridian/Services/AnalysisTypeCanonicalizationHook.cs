using System;
using System.Collections.Generic;
using System.Linq;
using Koan.Samples.Meridian.Infrastructure;
using Koan.Samples.Meridian.Models;
using Koan.Web.Hooks;

namespace Koan.Samples.Meridian.Services;

public sealed class AnalysisTypeCanonicalizationHook : IModelHook<AnalysisType>
{
    public int Order => 0;

    public Task OnAfterDeleteAsync(HookContext<AnalysisType> ctx, AnalysisType model) => Task.CompletedTask;
    public Task OnAfterFetchAsync(HookContext<AnalysisType> ctx, AnalysisType? model) => Task.CompletedTask;
    public Task OnAfterPatchAsync(HookContext<AnalysisType> ctx, AnalysisType model) => Task.CompletedTask;
    public Task OnAfterSaveAsync(HookContext<AnalysisType> ctx, AnalysisType model) => Task.CompletedTask;
    public Task OnBeforeDeleteAsync(HookContext<AnalysisType> ctx, AnalysisType model) => Task.CompletedTask;
    public Task OnBeforeFetchAsync(HookContext<AnalysisType> ctx, string id) => Task.CompletedTask;
    public Task OnBeforePatchAsync(HookContext<AnalysisType> ctx, string id, object patch) => Task.CompletedTask;

    public Task OnBeforeSaveAsync(HookContext<AnalysisType> ctx, AnalysisType model)
    {
        model.OutputTemplate = FieldPathCanonicalizer.CanonicalizeTemplatePlaceholders(model.OutputTemplate);
        model.JsonSchema = FieldPathCanonicalizer.CanonicalizeJsonSchema(model.JsonSchema);

        if (model.RequiredSourceTypes.Count > 0)
        {
            model.RequiredSourceTypes = model.RequiredSourceTypes
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        model.UpdatedAt = DateTime.UtcNow;
        return Task.CompletedTask;
    }
}
