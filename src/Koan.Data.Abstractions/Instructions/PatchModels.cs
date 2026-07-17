using Newtonsoft.Json.Linq;

namespace Koan.Data.Abstractions.Instructions;

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

/// <summary>
/// Provider-neutral patch operation envelope. Protocol projections normalize into this contract.
/// </summary>
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
