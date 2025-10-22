using System;
using Newtonsoft.Json.Linq;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Instructions;

namespace Koan.Data.Core.Patch;

public static class PatchApplicators
{
    /// <summary>
    /// Creates an applicator for the given patch kind and payload.
    /// Payload formats:
    ///  - JsonPatch6902: <c>JsonPatchDocument&lt;TEntity&gt;</c>
    ///  - MergePatch7386: <c>JToken</c> (object)
    ///  - PartialJson: <c>JToken</c> (object)
    /// </summary>
    public static IPatchApplicator<TEntity> Create<TEntity, TKey>(PatchKind kind, object payload, MergePatchNullPolicy mergeNulls, PartialJsonNullPolicy partialNulls)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        switch (kind)
        {
            case PatchKind.JsonPatch6902:
                return payload is Microsoft.AspNetCore.JsonPatch.JsonPatchDocument<TEntity> patch
                    ? new Koan.Data.Abstractions.Instructions.JsonPatchApplicator<TEntity>(patch)
                    : throw new ArgumentException("Payload must be JsonPatchDocument<TEntity>");
            case PatchKind.MergePatch7386:
                return new MergePatchApplicator<TEntity>((JToken)payload, mergeNulls);
            case PatchKind.PartialJson:
                return new PartialJsonApplicator<TEntity>((JToken)payload, partialNulls);
            default:
                throw new NotSupportedException($"Unsupported patch kind: {kind}");
        }
    }
}

public sealed class MergePatchApplicator<TEntity> : IPatchApplicator<TEntity>
{
    private readonly JToken _patch;
    private readonly MergePatchNullPolicy _nulls;
    public MergePatchApplicator(JToken patch, MergePatchNullPolicy nulls) { _patch = patch; _nulls = nulls; }

    public void Apply(TEntity target)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        var targetObj = JObject.FromObject(target);
        ApplyMerge(targetObj, _patch);
        var updated = targetObj.ToObject<TEntity>()!;
        Copy(updated, target);
    }

    private void ApplyMerge(JObject target, JToken patch)
    {
        if (patch.Type != JTokenType.Object) throw new ArgumentException("Merge-patch payload must be an object");
        foreach (var prop in ((JObject)patch).Properties())
        {
            var name = prop.Name;
            var value = prop.Value;
            if (value.Type == JTokenType.Null)
            {
                // Normalize to explicit null assignment to ensure target property is cleared on Populate
                target[name] = JValue.CreateNull();
            }
            else if (value.Type == JTokenType.Object)
            {
                var tgtChild = target[name] as JObject;
                if (tgtChild is null)
                {
                    target[name] = value.DeepClone();
                }
                else
                {
                    ApplyMerge(tgtChild, value);
                }
            }
            else
            {
                target[name] = value.DeepClone();
            }
        }
    }

    private static void Copy(TEntity src, TEntity dest)
    {
        if (src is null) throw new ArgumentNullException(nameof(src));
        if (dest is null) throw new ArgumentNullException(nameof(dest));
        // Use JObject round-trip to avoid reflection trim warnings and preserve mapping
        var srcObj = JObject.FromObject(src);
        // Don't allow Id overwrite
        srcObj.Remove("Id");
        var destObj = JObject.FromObject(dest);
        destObj.Merge(srcObj, new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace, MergeNullValueHandling = MergeNullValueHandling.Merge });
        using var reader = destObj.CreateReader();
        var serializer = Newtonsoft.Json.JsonSerializer.CreateDefault();
        serializer.Populate(reader, dest);
    }
}

public sealed class PartialJsonApplicator<TEntity> : IPatchApplicator<TEntity>
{
    private readonly JToken _patch;
    private readonly PartialJsonNullPolicy _nulls;
    public PartialJsonApplicator(JToken patch, PartialJsonNullPolicy nulls) { _patch = patch; _nulls = nulls; }

    public void Apply(TEntity target)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        if (_patch.Type != JTokenType.Object) throw new ArgumentException("Partial JSON payload must be an object");
        var targetObj = JObject.FromObject(target);
        ApplyPartial(targetObj, (JObject)_patch);
        var updated = targetObj.ToObject<TEntity>()!;
        Copy(updated, target);
    }

    private void ApplyPartial(JObject target, JObject patch)
    {
        foreach (var prop in patch.Properties())
        {
            var name = prop.Name;
            var value = prop.Value;
            if (value.Type == JTokenType.Null)
            {
                switch (_nulls)
                {
                    case PartialJsonNullPolicy.SetNull:
                        target[name] = JValue.CreateNull();
                        break;
                    case PartialJsonNullPolicy.Ignore:
                        break;
                    case PartialJsonNullPolicy.Reject:
                        throw new InvalidOperationException($"Null not allowed for property '{name}' in partial JSON mode.");
                }
                continue;
            }

            if (value.Type == JTokenType.Object)
            {
                var tgtChild = target[name] as JObject;
                if (tgtChild is null)
                {
                    target[name] = value.DeepClone();
                }
                else
                {
                    ApplyPartial(tgtChild, (JObject)value);
                }
            }
            else
            {
                // Arrays and primitives: replace
                target[name] = value.DeepClone();
            }
        }
    }

    private static void Copy(TEntity src, TEntity dest)
    {
        if (src is null) throw new ArgumentNullException(nameof(src));
        if (dest is null) throw new ArgumentNullException(nameof(dest));
        var srcObj = JObject.FromObject(src);
        srcObj.Remove("Id");
        var destObj = JObject.FromObject(dest);
        destObj.Merge(srcObj, new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace, MergeNullValueHandling = MergeNullValueHandling.Merge });
        using var reader = destObj.CreateReader();
        var serializer = Newtonsoft.Json.JsonSerializer.CreateDefault();
        serializer.Populate(reader, dest);
    }
}
