using System.ComponentModel;
using Koan.Cache.Abstractions.Primitives;
using Koan.Core.Naming;
using Koan.Core.Semantics.Segmentation;

namespace Koan.Cache.Identity;

/// <summary>Cache-owned physical realization of the host's hard segmentation dimensions.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
internal sealed class CacheIdentityPlan(SegmentationPlan segmentation) : ISegmentationRealization
{
    private const string Separator = "::";
    private static readonly SegmentationRealizationDescriptor Realization = new(
        "cache",
        "key-tag-suffix",
        [
            "coherence",
            "entity.key",
            "eviction",
            "generic.key",
            "singleflight",
            "tag"
        ]);

    public SegmentationRealizationDescriptor SegmentationRealization => Realization;

    public CacheIdentityBinding Bind(
        CacheKey key,
        IReadOnlyCollection<string>? tags,
        Type? subject,
        string operation)
    {
        var scope = subject is null ? segmentation.Untyped : segmentation.For(subject);
        var bindings = scope.Bind(operation);
        if (bindings.IsEmpty)
            return new CacheIdentityBinding(key, tags ?? Array.Empty<string>());

        var axes = new Dictionary<string, string>(bindings.Length, StringComparer.Ordinal);
        foreach (var binding in bindings)
            axes.Add(binding.DimensionId, binding.Value);

        var physicalKey = new CacheKey(AmbientAxisComposer.Append(
            key.Value,
            axes,
            ParticlePosition.Trailing,
            Separator));
        var physicalTags = QualifyTags(tags, axes);
        return new CacheIdentityBinding(physicalKey, physicalTags);
    }

    public IReadOnlyList<string> BindTags(
        IReadOnlyCollection<string> tags,
        Type? subject,
        string operation)
    {
        var scope = subject is null ? segmentation.Untyped : segmentation.For(subject);
        var bindings = scope.Bind(operation);
        if (bindings.IsEmpty) return tags as IReadOnlyList<string> ?? tags.ToArray();

        var axes = new Dictionary<string, string>(bindings.Length, StringComparer.Ordinal);
        foreach (var binding in bindings)
            axes.Add(binding.DimensionId, binding.Value);
        return QualifyTags(tags, axes);
    }

    private static IReadOnlyList<string> QualifyTags(
        IReadOnlyCollection<string>? tags,
        IReadOnlyDictionary<string, string> axes)
    {
        if (tags is null || tags.Count == 0) return [];
        var qualified = new string[tags.Count];
        var index = 0;
        foreach (var tag in tags)
            qualified[index++] = AmbientAxisComposer.Append(
                tag,
                axes,
                ParticlePosition.Trailing,
                Separator);
        return qualified;
    }
}

[EditorBrowsable(EditorBrowsableState.Never)]
internal readonly record struct CacheIdentityBinding(
    CacheKey Key,
    IReadOnlyCollection<string> Tags);
