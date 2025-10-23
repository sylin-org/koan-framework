using System;
using System.Collections.Generic;
using System.Linq;
using Koan.Samples.Meridian.Infrastructure;
using Koan.Samples.Meridian.Models;
using Koan.Web.Hooks;

namespace Koan.Samples.Meridian.Services;

public sealed class DeliverableTypeCanonicalizationHook : IModelHook<DeliverableType>
{
    public int Order => 0;

    public Task OnAfterDeleteAsync(HookContext<DeliverableType> ctx, DeliverableType model) => Task.CompletedTask;
    public Task OnAfterFetchAsync(HookContext<DeliverableType> ctx, DeliverableType? model) => Task.CompletedTask;
    public Task OnAfterPatchAsync(HookContext<DeliverableType> ctx, DeliverableType model) => Task.CompletedTask;
    public Task OnAfterSaveAsync(HookContext<DeliverableType> ctx, DeliverableType model) => Task.CompletedTask;
    public Task OnBeforeDeleteAsync(HookContext<DeliverableType> ctx, DeliverableType model) => Task.CompletedTask;
    public Task OnBeforeFetchAsync(HookContext<DeliverableType> ctx, string id) => Task.CompletedTask;
    public Task OnBeforePatchAsync(HookContext<DeliverableType> ctx, string id, object patch) => Task.CompletedTask;

    public Task OnBeforeSaveAsync(HookContext<DeliverableType> ctx, DeliverableType model)
    {
        model.TemplateMd = FieldPathCanonicalizer.CanonicalizeTemplatePlaceholders(model.TemplateMd);
        model.JsonSchema = FieldPathCanonicalizer.CanonicalizeJsonSchema(model.JsonSchema);

        if (model.FieldMergePolicies.Count > 0)
        {
            FieldPathCanonicalizer.CanonicalizeKeys(model.FieldMergePolicies);

            foreach (var policy in model.FieldMergePolicies.Values)
            {
                if (policy.SourcePrecedence is { Count: > 0 })
                {
                    for (var i = 0; i < policy.SourcePrecedence.Count; i++)
                    {
                        policy.SourcePrecedence[i] = FieldPathCanonicalizer.Canonicalize(policy.SourcePrecedence[i]);
                    }
                }

                if (!string.IsNullOrWhiteSpace(policy.LatestByFieldPath))
                {
                    policy.LatestByFieldPath = FieldPathCanonicalizer.Canonicalize(policy.LatestByFieldPath);
                }
            }
        }

        if (model.SourceMappings.Count > 0)
        {
            foreach (var mapping in model.SourceMappings)
            {
                if (mapping.FieldMappings.Count == 0)
                {
                    continue;
                }

                var snapshot = mapping.FieldMappings.ToList();
                mapping.FieldMappings.Clear();

                foreach (var (key, value) in snapshot)
                {
                    var canonicalKey = FieldPathCanonicalizer.Canonicalize(key);
                    var canonicalValue = FieldPathCanonicalizer.Canonicalize(value);

                    if (canonicalKey == "$")
                    {
                        continue;
                    }

                    mapping.FieldMappings[canonicalKey] = canonicalValue;
                }
            }
        }

        model.UpdatedAt = DateTime.UtcNow;
        return Task.CompletedTask;
    }
}
