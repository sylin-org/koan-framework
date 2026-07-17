using System;
using Newtonsoft.Json.Linq;
using Koan.Data.Abstractions.Instructions;

namespace Koan.Data.Core.Patch;

public sealed class MergePatchApplicator<TEntity>
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

public sealed class PartialJsonApplicator<TEntity>
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
