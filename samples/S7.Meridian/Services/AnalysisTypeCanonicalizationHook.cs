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

    public Task OnAfterDelete(HookContext<AnalysisType> ctx, AnalysisType model) => Task.CompletedTask;
    public Task OnAfterFetch(HookContext<AnalysisType> ctx, AnalysisType? model) => Task.CompletedTask;
    public Task OnAfterPatch(HookContext<AnalysisType> ctx, AnalysisType model) => Task.CompletedTask;
    public Task OnAfterSave(HookContext<AnalysisType> ctx, AnalysisType model) => Task.CompletedTask;
    public Task OnBeforeDelete(HookContext<AnalysisType> ctx, AnalysisType model) => Task.CompletedTask;
    public Task OnBeforeFetch(HookContext<AnalysisType> ctx, string id) => Task.CompletedTask;
    public Task OnBeforePatch(HookContext<AnalysisType> ctx, string id, object patch) => Task.CompletedTask;

    public Task OnBeforeSave(HookContext<AnalysisType> ctx, AnalysisType model)
    {
        model.OutputTemplate = FieldPathCanonicalizer.CanonicalizeTemplatePlaceholders(model.OutputTemplate);
        model.JsonSchema = FieldPathCanonicalizer.CanonicalizeJsonSchema(model.JsonSchema);

        model.UpdatedAt = DateTime.UtcNow;
        return Task.CompletedTask;
    }
}
