using Microsoft.AspNetCore.JsonPatch;
using Newtonsoft.Json.Linq;

namespace Koan.Data.Abstractions.Instructions;

/// <summary>
/// The kind of patch payload being applied.
/// </summary>
public enum PatchKind
{
    JsonPatch6902,
    MergePatch7386,
    PartialJson
}

/// <summary>
/// Transport-agnostic patch request envelope.
/// </summary>
public sealed record PatchRequest<TKey, TEntity>(
    TKey Id,
    PatchKind Kind,
    object Payload
) where TEntity : class, IEntity<TKey> where TKey : notnull;

/// <summary>
/// Executor-facing contract for applying a patch to a model instance.
/// </summary>
public interface IPatchApplicator<TEntity>
{
    void Apply(TEntity target);
}

/// <summary>
/// JSON Patch (RFC 6902) applicator wrapper.
/// </summary>
public sealed class JsonPatchApplicator<TEntity> : IPatchApplicator<TEntity> where TEntity : class
{
    private readonly JsonPatchDocument<TEntity> _doc;
    public JsonPatchApplicator(JsonPatchDocument<TEntity> doc) => _doc = doc;
    public void Apply(TEntity target) => _doc.ApplyTo(target);
}

public enum MergePatchNullPolicy
{
    SetDefault,
    Reject
}

public enum PartialJsonNullPolicy
{
    SetNull,
    Ignore,
    Reject
}

// Canonical patch surface
public sealed record PatchPayload<TKey>(
    TKey Id,
    string? Set,
    string? ETag,
    string? KindHint,
    IReadOnlyList<PatchOp> Ops,
    PatchOptions? Options
) where TKey : notnull;

public sealed record PatchOptions(
    MergePatchNullPolicy MergeNulls = MergePatchNullPolicy.SetDefault,
    PartialJsonNullPolicy PartialNulls = PartialJsonNullPolicy.SetNull,
    ArrayBehavior Arrays = ArrayBehavior.Replace
);

public enum ArrayBehavior { Replace }

public sealed record PatchOp(
    string Op,
    string Path,
    string? From,
    JToken? Value
);
