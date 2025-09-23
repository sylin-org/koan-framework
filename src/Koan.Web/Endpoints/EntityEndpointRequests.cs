using System.Collections.Generic;
using Microsoft.AspNetCore.JsonPatch;

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
    public string? Accept { get; init; }
    public string? BasePath { get; init; }
    public IReadOnlyDictionary<string, string?> QueryParameters { get; init; } = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
}

public sealed class EntityQueryRequest
{
    public required EntityRequestContext Context { get; init; }
    public string? FilterJson { get; init; }
    public string? Set { get; init; }
    public bool IgnoreCase { get; init; }
    public string? Accept { get; init; }
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

public sealed class EntityUpsertRequest<TEntity> where TEntity : class
{
    public required EntityRequestContext Context { get; init; }
    public required TEntity Model { get; init; }
    public string? Set { get; init; }
    public string? Accept { get; init; }
}

public sealed class EntityUpsertManyRequest<TEntity> where TEntity : class
{
    public required EntityRequestContext Context { get; init; }
    public required IReadOnlyCollection<TEntity> Models { get; init; }
    public string? Set { get; init; }
}

public sealed class EntityDeleteRequest<TKey>
{
    public required EntityRequestContext Context { get; init; }
    public required TKey Id { get; init; }
    public string? Set { get; init; }
    public string? Accept { get; init; }
}

public sealed class EntityDeleteManyRequest<TKey>
{
    public required EntityRequestContext Context { get; init; }
    public required IReadOnlyCollection<TKey> Ids { get; init; }
    public string? Set { get; init; }
}

public sealed class EntityDeleteByQueryRequest
{
    public required EntityRequestContext Context { get; init; }
    public required string Query { get; init; }
    public string? Set { get; init; }
}

public sealed class EntityDeleteAllRequest
{
    public required EntityRequestContext Context { get; init; }
    public string? Set { get; init; }
}

public sealed class EntityPatchRequest<TEntity, TKey> where TEntity : class
{
    public required EntityRequestContext Context { get; init; }
    public required TKey Id { get; init; }
    public required JsonPatchDocument<TEntity> Patch { get; init; }
    public string? Set { get; init; }
    public string? Accept { get; init; }
}



