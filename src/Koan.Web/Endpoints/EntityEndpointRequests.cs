using System.Collections.Generic;
using Koan.Web.Attributes;
using Koan.Data.Abstractions.Instructions;

namespace Koan.Web.Endpoints;

public sealed class EntityCollectionRequest
{
    public required EntityRequestContext Context { get; init; }
    public string? FilterJson { get; init; }
    public string? Set { get; init; }
    public bool IgnoreCase { get; init; }
    public string? With { get; init; }
    public string? Shape { get; init; }
    public bool ForcePagination { get; init; }
    public bool ApplyPagination { get; init; }
    public bool PaginationRequested { get; init; }
    public bool ClientRequestedAll { get; init; }
    public required PaginationPolicy Policy { get; init; }
    public bool IncludeTotalCount { get; init; }
    public int AbsoluteMaxRecords { get; init; }
    public string? Accept { get; init; }
    public string? BasePath { get; init; }
    // SEC-0004 (§C): opt the collection response into the per-row `access` sidecar (REST ?access=true). Default
    // false keeps the bare array for existing consumers.
    public bool IncludeAccess { get; init; }
    public IReadOnlyDictionary<string, string?> QueryParameters { get; init; } = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The endpoint declared <c>q</c> a provider-native query (WEB-0074). False for every caller that did not say so,
    /// including the MCP edge: a <c>q</c> still set when the read executes is then refused, never run.
    /// </summary>
    public bool AllowRawQuery { get; init; }

    /// <summary>
    /// DATA-0170: a declared collection's read, which continues from a key in its declared order and counts nothing, in place of a
    /// numbered page; null for every other collection.
    /// </summary>
    public KeysetRead? Keyset { get; init; }
}

/// <summary>
/// DATA-0170: what a declared collection continues by: its order (a <c>Keyset&lt;TEntity&gt;</c>), the key its <c>after</c> opened to, the
/// rows a page holds, and how the key of the next page is sealed for the caller.
/// </summary>
public sealed class KeysetRead
{
    internal KeysetRead(object order, Koan.Data.Core.Querying.KeysetKey? after, int size, Func<Koan.Data.Core.Querying.KeysetKey, string> seal)
    {
        Order = order;
        After = after;
        Size = size;
        Seal = seal;
    }

    internal object Order { get; }

    internal Koan.Data.Core.Querying.KeysetKey? After { get; }

    internal int Size { get; }

    internal Func<Koan.Data.Core.Querying.KeysetKey, string> Seal { get; }
}

public sealed class EntityQueryRequest
{
    public required EntityRequestContext Context { get; init; }
    public string? FilterJson { get; init; }
    public string? Set { get; init; }
    public bool IgnoreCase { get; init; }
    public string? Accept { get; init; }

    /// <summary>
    /// WEB-0076: the paging bounds of the endpoint. The body query is always paged and clamped to them; when absent the
    /// configured defaults (<c>Koan:Web:Pagination</c>) apply.
    /// </summary>
    public PaginationPolicy? Policy { get; init; }

    /// <summary>The endpoint declared <c>q</c> a provider-native query (WEB-0074); see <see cref="EntityCollectionRequest.AllowRawQuery"/>.</summary>
    public bool AllowRawQuery { get; init; }
}

public sealed class EntityGetNewRequest
{
    public required EntityRequestContext Context { get; init; }
    public string? Accept { get; init; }
}

public sealed class EntityGetByIdRequest<TKey>
{
    public required EntityRequestContext Context { get; init; }
    public required TKey Id { get; init; }
    public string? Set { get; init; }
    public string? With { get; init; }
    public string? Accept { get; init; }
}

public sealed class EntityUpsertRequest<TEntity, TKey>
    where TEntity : class
    where TKey : notnull
{
    public required EntityRequestContext Context { get; init; }
    public required TEntity Model { get; init; }

    // WEB-0073: route-id authority. Set by verbs whose contract pins the id to the route (PUT);
    // the endpoint service applies it to the model before the create-vs-update split.
    public TKey? RouteId { get; init; }
    public string? Set { get; init; }
    public string? Accept { get; init; }
    // AN11: rehearse the mutation — run the hook/validation pipeline + project the delta, commit nothing.
    public bool DryRun { get; init; }

    /// <summary>Whether the write only creates (WEB-0087): a row already holding the id answers 409 id-taken, or 404 where the caller's read
    /// hides it.</summary>
    public bool Insert { get; init; }

    /// <summary>The members the body named, where the operation declares what it writes (WEB-0087): record access admits each of them, where
    /// it otherwise admits a replacement of the whole entity.</summary>
    public IReadOnlyCollection<string>? Writes { get; init; }
}

public sealed class EntityUpsertManyRequest<TEntity> where TEntity : class
{
    public required EntityRequestContext Context { get; init; }
    public required IReadOnlyCollection<TEntity> Models { get; init; }
    public string? Set { get; init; }
    public bool DryRun { get; init; }
}

public sealed class EntityDeleteRequest<TKey>
{
    public required EntityRequestContext Context { get; init; }
    public required TKey Id { get; init; }
    public string? Set { get; init; }
    public string? Accept { get; init; }
    public bool DryRun { get; init; }
}

public sealed class EntityDeleteManyRequest<TKey>
{
    public required EntityRequestContext Context { get; init; }
    public required IReadOnlyCollection<TKey> Ids { get; init; }
    public string? Set { get; init; }
    public bool DryRun { get; init; }
}

public sealed class EntityDeleteByQueryRequest
{
    public required EntityRequestContext Context { get; init; }
    public required string Query { get; init; }
    public string? Set { get; init; }
    public bool DryRun { get; init; }
}

public sealed class EntityDeleteAllRequest
{
    public required EntityRequestContext Context { get; init; }
    public string? Set { get; init; }
    public bool DryRun { get; init; }
}

public enum EntityPatchKind
{
    JsonPatch6902,
    MergePatch7386,
    PartialJson
}

public sealed class EntityPatchRequest<TEntity, TKey> where TEntity : class
{
    public required EntityRequestContext Context { get; init; }
    public required TKey Id { get; init; }
    // Generalized patch support
    public object? Patch { get; init; }
    public EntityPatchKind Kind { get; init; }
    public string? Set { get; init; }
    public string? Accept { get; init; }
    public bool DryRun { get; init; }
    // Request-scoped null-policy override; when absent the governed path falls back to IOptions<KoanWebOptions>.
    public Koan.Data.Abstractions.Instructions.PatchOptions? Options { get; init; }

    /// <summary>The members a JSON body named, where the operation declares what it writes (WEB-0087): record access admits each of them,
    /// where it otherwise admits a replacement of the whole entity.</summary>
    public IReadOnlyCollection<string>? Writes { get; init; }
}

