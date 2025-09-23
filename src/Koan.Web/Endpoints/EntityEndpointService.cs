using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Web.Filtering;
using Koan.Web.Hooks;
using Koan.Web.Infrastructure;

namespace Koan.Web.Endpoints;

internal sealed class EntityEndpointService<TEntity, TKey> : IEntityEndpointService<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly IDataService _dataService;
    private readonly ILogger<EntityEndpointService<TEntity, TKey>>? _logger;

    public EntityEndpointService(
        IDataService dataService,
        ILogger<EntityEndpointService<TEntity, TKey>>? logger = null)
    {
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _logger = logger;
    }

    public async Task<EntityCollectionResult<TEntity>> GetCollectionAsync(EntityCollectionRequest request)
    {
        var context = request.Context;
        var repo = _dataService.GetRepository<TEntity, TKey>();
        context.Capabilities = Capabilities(repo);

        var runner = CreateRunner(context);
        var hookContext = new HookContext<TEntity>(context);

        if (!await runner.BuildOptionsAsync(hookContext, context.Options))
        {
            return CollectionShortCircuit(context, hookContext);
        }

        if (!await runner.BeforeCollectionAsync(hookContext, context.Options))
        {
            return CollectionShortCircuit(context, hookContext);
        }

        IReadOnlyList<TEntity> repositoryItems;
        int total;
        try
        {
            (repositoryItems, total) = await QueryCollectionAsync(repo, request, context.Options, context.CancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            var bad = new BadRequestObjectResult(new { error = ex.Message });
            return new EntityCollectionResult<TEntity>(context, Array.Empty<TEntity>(), 0, null, bad);
        }

        var list = repositoryItems.ToList();
        if (request.ForcePagination || ShouldPaginate(context, request))
        {
            (list, total) = ApplyPagination(list, context.Options.Page, context.Options.PageSize, total);
            context.Headers["Koan-InMemory-Paging"] = "true";
            context.Headers["X-Page"] = context.Options.Page.ToString();
            context.Headers["X-Page-Size"] = context.Options.PageSize.ToString();
            context.Headers["X-Total-Count"] = total.ToString();
            var totalPages = context.Options.PageSize > 0 ? (int)Math.Ceiling((double)total / context.Options.PageSize) : 0;
            context.Headers["X-Total-Pages"] = totalPages.ToString();
            if (!string.IsNullOrWhiteSpace(request.BasePath) && request.QueryParameters.Count > 0 && totalPages > 0)
            {
                var links = BuildLinkHeaders(request.BasePath!, request.QueryParameters, context.Options.Page, context.Options.PageSize, totalPages);
                if (links.Length > 0)
                {
                    context.Headers["Link"] = string.Join(", ", links);
                }
            }
        }

        if (!await runner.AfterCollectionAsync(hookContext, list))
        {
            return CollectionShortCircuit(context, hookContext);
        }

        object payload = list;
        if (!string.IsNullOrWhiteSpace(request.With) && request.With.Contains("all", StringComparison.OrdinalIgnoreCase))
        {
            payload = await EnrichRelationshipsAsync(list, context.CancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(request.Shape))
        {
            payload = ApplyShape(request.Shape, list);
        }

        ApplyViewHeader(context, request.Accept);

        var emit = await runner.EmitCollectionAsync(hookContext, payload);
        payload = emit.replaced ? emit.payload : payload;
        CopyHookHeaders(context, hookContext);

        return new EntityCollectionResult<TEntity>(context, list, total, payload);
    }

    public async Task<EntityCollectionResult<TEntity>> QueryAsync(EntityQueryRequest request)
    {
        var context = request.Context;
        var repo = _dataService.GetRepository<TEntity, TKey>();
        context.Capabilities = Capabilities(repo);

        var runner = CreateRunner(context);
        var hookContext = new HookContext<TEntity>(context);

        if (!await runner.BuildOptionsAsync(hookContext, context.Options))
        {
            return CollectionShortCircuit(context, hookContext);
        }

        if (!await runner.BeforeCollectionAsync(hookContext, context.Options))
        {
            return CollectionShortCircuit(context, hookContext);
        }

        IReadOnlyList<TEntity> repositoryItems;
        int total;
        try
        {
            (repositoryItems, total) = await QueryCollectionFromBodyAsync(repo, request, context.Options, context.CancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            var bad = new BadRequestObjectResult(new { error = ex.Message });
            return new EntityCollectionResult<TEntity>(context, Array.Empty<TEntity>(), 0, null, bad);
        }

        var list = repositoryItems.ToList();
        if (context.Options.PageSize > 0)
        {
            (list, total) = ApplyPagination(list, context.Options.Page, context.Options.PageSize, total);
            context.Headers["Koan-InMemory-Paging"] = "true";
            context.Headers["X-Page"] = context.Options.Page.ToString();
            context.Headers["X-Page-Size"] = context.Options.PageSize.ToString();
            context.Headers["X-Total-Count"] = total.ToString();
        }

        if (!await runner.AfterCollectionAsync(hookContext, list))
        {
            return CollectionShortCircuit(context, hookContext);
        }

        ApplyViewHeader(context, request.Accept);

        var emit = await runner.EmitCollectionAsync(hookContext, list);
        var payload = emit.replaced ? emit.payload : list;
        CopyHookHeaders(context, hookContext);

        return new EntityCollectionResult<TEntity>(context, list, total, payload);
    }

    public async Task<EntityModelResult<TEntity>> GetNewAsync(EntityGetNewRequest request)
    {
        var context = request.Context;
        var repo = _dataService.GetRepository<TEntity, TKey>();
        context.Capabilities = Capabilities(repo);

        var runner = CreateRunner(context);
        var hookContext = new HookContext<TEntity>(context);

        var model = Activator.CreateInstance<TEntity>();
        await runner.AfterModelFetchAsync(hookContext, model);

        ApplyViewHeader(context, request.Accept);

        var emit = await runner.EmitModelAsync(hookContext, model!);
        var payload = emit.replaced ? emit.payload : model;
        CopyHookHeaders(context, hookContext);

        return new EntityModelResult<TEntity>(context, model, payload);
    }

    public async Task<EntityModelResult<TEntity>> GetByIdAsync(EntityGetByIdRequest<TKey> request)
    {
        var context = request.Context;
        var repo = _dataService.GetRepository<TEntity, TKey>();
        context.Capabilities = Capabilities(repo);

        var runner = CreateRunner(context);
        var hookContext = new HookContext<TEntity>(context);

        if (!await runner.BeforeModelFetchAsync(hookContext, request.Id?.ToString() ?? string.Empty))
        {
            return ModelShortCircuit(context, hookContext);
        }

        using var _ = DataSetContext.With(string.IsNullOrWhiteSpace(request.Set) ? null : request.Set);
        var model = await Data<TEntity, TKey>.GetAsync(request.Id!, context.CancellationToken);
        await runner.AfterModelFetchAsync(hookContext, model);
        if (model is null)
        {
            CopyHookHeaders(context, hookContext);
            return new EntityModelResult<TEntity>(context, null, null, new NotFoundResult());
        }

        if (!string.IsNullOrWhiteSpace(request.With) && request.With.Contains("all", StringComparison.OrdinalIgnoreCase) && model is Entity<TEntity, TKey> entity)
        {
            var enriched = await entity.GetRelatives(context.CancellationToken);
            ApplyViewHeader(context, request.Accept);
            CopyHookHeaders(context, hookContext);
            return new EntityModelResult<TEntity>(context, model, enriched);
        }

        ApplyViewHeader(context, request.Accept);
        var emit = await runner.EmitModelAsync(hookContext, model);
        var payload = emit.replaced ? emit.payload : model;
        CopyHookHeaders(context, hookContext);
        return new EntityModelResult<TEntity>(context, model, payload);
    }

    public async Task<EntityModelResult<TEntity>> UpsertAsync(EntityUpsertRequest<TEntity> request)
    {
        var context = request.Context;
        var repo = _dataService.GetRepository<TEntity, TKey>();
        context.Capabilities = Capabilities(repo);

        var runner = CreateRunner(context);
        var hookContext = new HookContext<TEntity>(context);

        await runner.BeforeSaveAsync(hookContext, request.Model);

        TEntity saved;
        if (!string.IsNullOrWhiteSpace(request.Set))
        {
            using var _ = DataSetContext.With(request.Set);
            saved = await request.Model.Upsert<TEntity, TKey>(context.CancellationToken);
        }
        else
        {
            saved = await request.Model.Upsert<TEntity, TKey>(context.CancellationToken);
        }

        await runner.AfterSaveAsync(hookContext, saved);

        ApplyViewHeader(context, request.Accept);
        var emit = await runner.EmitModelAsync(hookContext, saved);
        var payload = emit.replaced ? emit.payload : saved;
        CopyHookHeaders(context, hookContext);
        return new EntityModelResult<TEntity>(context, saved, payload);
    }

    public async Task<EntityEndpointResult> UpsertManyAsync(EntityUpsertManyRequest<TEntity> request)
    {
        var context = request.Context;
        var repo = _dataService.GetRepository<TEntity, TKey>();
        context.Capabilities = Capabilities(repo);

        var runner = CreateRunner(context);
        var hookContext = new HookContext<TEntity>(context);

        var list = request.Models.ToList();
        if (list.Count == 0)
        {
            return new EntityEndpointResult(context, null, new BadRequestObjectResult(new { error = "At least one item is required" }));
        }
        if (list.Any(m => m is null))
        {
            return new EntityEndpointResult(context, null, new BadRequestObjectResult(new { error = "Null items are not allowed" }));
        }

        foreach (var model in list)
        {
            await runner.BeforeSaveAsync(hookContext, model);
        }

        using var _ = DataSetContext.With(string.IsNullOrWhiteSpace(request.Set) ? null : request.Set);
        var upserted = await Data<TEntity, TKey>.UpsertManyAsync(list, context.CancellationToken);

        foreach (var model in list)
        {
            await runner.AfterSaveAsync(hookContext, model);
        }

        var writes = WriteCaps(repo);
        context.Headers["Koan-Write-Capabilities"] = writes.Writes.ToString();
        CopyHookHeaders(context, hookContext);
        return new EntityEndpointResult(context, new { upserted });
    }

    public async Task<EntityModelResult<TEntity>> DeleteAsync(EntityDeleteRequest<TKey> request)
    {
        var context = request.Context;
        var repo = _dataService.GetRepository<TEntity, TKey>();
        context.Capabilities = Capabilities(repo);

        var runner = CreateRunner(context);
        var hookContext = new HookContext<TEntity>(context);

        using var _ = DataSetContext.With(string.IsNullOrWhiteSpace(request.Set) ? null : request.Set);
        var model = await Data<TEntity, TKey>.GetAsync(request.Id, context.CancellationToken);
        if (model is null)
        {
            return new EntityModelResult<TEntity>(context, null, null, new NotFoundResult());
        }

        await runner.BeforeDeleteAsync(hookContext, model);
        var ok = await Data<TEntity, TKey>.DeleteAsync(request.Id, context.CancellationToken);
        if (!ok)
        {
            return new EntityModelResult<TEntity>(context, null, null, new NotFoundResult());
        }
        await runner.AfterDeleteAsync(hookContext, model);

        ApplyViewHeader(context, request.Accept);
        var emit = await runner.EmitModelAsync(hookContext, model);
        var payload = emit.replaced ? emit.payload : model;
        CopyHookHeaders(context, hookContext);
        return new EntityModelResult<TEntity>(context, model, payload);
    }

    public async Task<EntityEndpointResult> DeleteManyAsync(EntityDeleteManyRequest<TKey> request)
    {
        var context = request.Context;
        var repo = _dataService.GetRepository<TEntity, TKey>();
        var writes = WriteCaps(repo);
        context.Headers["Koan-Write-Capabilities"] = writes.Writes.ToString();
        using var _ = DataSetContext.With(string.IsNullOrWhiteSpace(request.Set) ? null : request.Set);
        var deleted = await Data<TEntity, TKey>.DeleteManyAsync(request.Ids ?? Array.Empty<TKey>(), context.CancellationToken);
        return new EntityEndpointResult(context, new { deleted });
    }

    public async Task<EntityEndpointResult> DeleteByQueryAsync(EntityDeleteByQueryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return new EntityEndpointResult(request.Context, null, new BadRequestResult());
        }

        using var _ = DataSetContext.With(string.IsNullOrWhiteSpace(request.Set) ? null : request.Set);
        var removed = await Entity<TEntity, TKey>.Remove(request.Query!, request.Context.CancellationToken);
        return new EntityEndpointResult(request.Context, new { deleted = removed });
    }

    public async Task<EntityEndpointResult> DeleteAllAsync(EntityDeleteAllRequest request)
    {
        using var _ = DataSetContext.With(string.IsNullOrWhiteSpace(request.Set) ? null : request.Set);
        var deleted = await Entity<TEntity, TKey>.RemoveAll(request.Context.CancellationToken);
        return new EntityEndpointResult(request.Context, new { deleted });
    }

    public async Task<EntityModelResult<TEntity>> PatchAsync(EntityPatchRequest<TEntity, TKey> request)
    {
        var context = request.Context;
        var repo = _dataService.GetRepository<TEntity, TKey>();
        context.Capabilities = Capabilities(repo);

        var runner = CreateRunner(context);
        var hookContext = new HookContext<TEntity>(context);

        await runner.BeforePatchAsync(hookContext, request.Id?.ToString() ?? string.Empty, request.Patch);

        using var _ = DataSetContext.With(string.IsNullOrWhiteSpace(request.Set) ? null : request.Set);
        var original = await Data<TEntity, TKey>.GetAsync(request.Id!, context.CancellationToken);
        if (original is null)
        {
            return new EntityModelResult<TEntity>(context, null, null, new NotFoundResult());
        }

        var working = await Data<TEntity, TKey>.GetAsync(request.Id!, context.CancellationToken);
        request.Patch.ApplyTo(working!);
        var idProp = typeof(TEntity).GetProperty("Id");
        if (idProp is not null)
        {
            var newId = idProp.GetValue(working);
            if (newId is not null && !Equals(newId, request.Id))
            {
                return new EntityModelResult<TEntity>(context, null, null, new ConflictResult());
            }
        }

        await runner.BeforeSaveAsync(hookContext, working!);
        var saved = await working!.Upsert<TEntity, TKey>(context.CancellationToken);
        await runner.AfterPatchAsync(hookContext, saved);

        ApplyViewHeader(context, request.Accept);
        var emit = await runner.EmitModelAsync(hookContext, saved);
        var payload = emit.replaced ? emit.payload : saved;
        CopyHookHeaders(context, hookContext);
        return new EntityModelResult<TEntity>(context, saved, payload);
    }

    private static HookRunner<TEntity> CreateRunner(EntityRequestContext context)
    {
        var services = context.Services;
        return new HookRunner<TEntity>(
            services.GetServices<IAuthorizeHook<TEntity>>(),
            services.GetServices<IRequestOptionsHook<TEntity>>(),
            services.GetServices<ICollectionHook<TEntity>>(),
            services.GetServices<IModelHook<TEntity>>(),
            services.GetServices<IEmitHook<TEntity>>());
    }

    private static IQueryCapabilities Capabilities(IDataRepository<TEntity, TKey> repo)
        => repo as IQueryCapabilities ?? new RepositoryCapabilities(QueryCapabilities.None);

    private static IWriteCapabilities WriteCaps(IDataRepository<TEntity, TKey> repo)
        => repo as IWriteCapabilities ?? new RepoWriteCaps(WriteCapabilities.None);

    private static void CopyHookHeaders(EntityRequestContext context, HookContext<TEntity> hookContext)
    {
        foreach (var kv in hookContext.ResponseHeaders)
        {
            context.Headers[kv.Key] = kv.Value;
        }
    }

    private static bool ShouldPaginate(EntityRequestContext context, EntityCollectionRequest request)
    {
        if (request.ForcePagination)
        {
            return true;
        }

        if (context.Options.PageSize > 0 || context.Options.Page > 1)
        {
            return true;
        }

        var http = context.HttpContext;
        if (http is null)
        {
            return false;
        }

        var query = http.Request.Query;
        return query.ContainsKey("page") || query.ContainsKey("size");
    }

    private static (List<TEntity> Items, int Total) ApplyPagination(List<TEntity> source, int page, int pageSize, int total)
    {
        if (pageSize <= 0)
        {
            return (source, total);
        }

        var skip = Math.Max(page - 1, 0) * pageSize;
        var items = source.Skip(skip).Take(pageSize).ToList();
        return (items, total);
    }

    private async Task<(IReadOnlyList<TEntity> Items, int Total)> QueryCollectionAsync(
        IDataRepository<TEntity, TKey> repo,
        EntityCollectionRequest request,
        QueryOptions options,
        CancellationToken cancellationToken)
    {
        using var _ = DataSetContext.With(string.IsNullOrWhiteSpace(request.Set) ? null : request.Set);
        if (!string.IsNullOrWhiteSpace(request.FilterJson) && repo is ILinqQueryRepository<TEntity, TKey> lrepo)
        {
            if (!JsonFilterBuilder.TryBuild<TEntity>(request.FilterJson!, out var predicate, out var error, new JsonFilterBuilder.BuildOptions { IgnoreCase = request.IgnoreCase }))
            {
                throw new InvalidOperationException(error ?? "Invalid filter");
            }

            IReadOnlyList<TEntity> items;
            if (repo is ILinqQueryRepositoryWithOptions<TEntity, TKey> lrepoOpts)
            {
                var dq = new Koan.Data.Abstractions.DataQueryOptions(options.Page, options.PageSize);
                items = await lrepoOpts.QueryAsync(predicate!, dq, cancellationToken);
            }
            else
            {
                items = await lrepo.QueryAsync(predicate!, cancellationToken);
            }

            int total;
            try { total = await lrepo.CountAsync(predicate!, cancellationToken); } catch { total = items.Count; }
            return (items, total);
        }

        if (!string.IsNullOrWhiteSpace(options.Q) && repo is IStringQueryRepository<TEntity, TKey> srepo)
        {
            IReadOnlyList<TEntity> items;
            if (repo is IStringQueryRepositoryWithOptions<TEntity, TKey> srepoOpts)
            {
                var dq = new Koan.Data.Abstractions.DataQueryOptions(options.Page, options.PageSize);
                items = await srepoOpts.QueryAsync(options.Q!, dq, cancellationToken);
            }
            else
            {
                items = await srepo.QueryAsync(options.Q!, cancellationToken);
            }

            int total;
            try { total = await srepo.CountAsync(options.Q!, cancellationToken); } catch { total = items.Count; }
            return (items, total);
        }

        if (repo is IDataRepositoryWithOptions<TEntity, TKey> repoOpts)
        {
            var dq = new Koan.Data.Abstractions.DataQueryOptions(options.Page, options.PageSize);
            var items = await repoOpts.QueryAsync(null, dq, cancellationToken);
            int total;
            try { total = await repoOpts.CountAsync(null, cancellationToken); } catch { total = items.Count; }
            return (items, total);
        }

        var fallback = await repo.QueryAsync(null, cancellationToken);
        int fallbackTotal;
        try { fallbackTotal = await repo.CountAsync(null, cancellationToken); } catch { fallbackTotal = fallback.Count; }
        return (fallback, fallbackTotal);
    }

    private async Task<(IReadOnlyList<TEntity> Items, int Total)> QueryCollectionFromBodyAsync(
        IDataRepository<TEntity, TKey> repo,
        EntityQueryRequest request,
        QueryOptions options,
        CancellationToken cancellationToken)
    {
        using var _ = DataSetContext.With(string.IsNullOrWhiteSpace(request.Set) ? null : request.Set);
        if (!string.IsNullOrWhiteSpace(request.FilterJson) && repo is ILinqQueryRepository<TEntity, TKey> lrepo)
        {
            if (!JsonFilterBuilder.TryBuild<TEntity>(request.FilterJson!, out var predicate, out var error, new JsonFilterBuilder.BuildOptions { IgnoreCase = request.IgnoreCase }))
            {
                throw new InvalidOperationException(error ?? "Invalid filter");
            }
            var items = await lrepo.QueryAsync(predicate!, cancellationToken);
            int total;
            try { total = await lrepo.CountAsync(predicate!, cancellationToken); } catch { total = items.Count; }
            return (items.ToList(), total);
        }

        if (!string.IsNullOrWhiteSpace(options.Q) && repo is IStringQueryRepository<TEntity, TKey> srepo)
        {
            var items = await srepo.QueryAsync(options.Q!, cancellationToken);
            int total;
            try { total = await srepo.CountAsync(options.Q!, cancellationToken); } catch { total = items.Count; }
            return (items.ToList(), total);
        }

        var all = await repo.QueryAsync(null, cancellationToken);
        int allTotal;
        try { allTotal = await repo.CountAsync(null, cancellationToken); } catch { allTotal = all.Count; }
        return (all.ToList(), allTotal);
    }

    private static async Task<IReadOnlyList<object>> EnrichRelationshipsAsync(IReadOnlyList<TEntity> list, CancellationToken cancellationToken)
    {
        var enrichedResults = new List<object>();
        foreach (var item in list)
        {
            if (item is Entity<TEntity, TKey> entity)
            {
                var enriched = await entity.GetRelatives(cancellationToken);
                enrichedResults.Add(enriched);
            }
            else
            {
                enrichedResults.Add(item);
            }
        }
        return enrichedResults;
    }

    private static object ApplyShape(string? shape, IReadOnlyList<TEntity> list)
    {
        if (string.Equals(shape, "map", StringComparison.OrdinalIgnoreCase))
        {
            return list.Select(i => new { key = GetEntityId(i), display = GetDisplay(i) }).ToList();
        }
        if (string.Equals(shape, "dict", StringComparison.OrdinalIgnoreCase))
        {
            return list.ToDictionary(i => GetEntityId(i)!, GetDisplay);
        }
        return list;
    }

    private static object? GetEntityId(TEntity entity)
    {
        var t = entity.GetType();
        return t.GetProperty("Id")?.GetValue(entity);
    }

    private static string GetDisplay(TEntity entity)
    {
        var t = entity.GetType();
        return t.GetProperty("Name")?.GetValue(entity) as string
               ?? t.GetProperty("Title")?.GetValue(entity) as string
               ?? t.GetProperty("Label")?.GetValue(entity) as string
               ?? entity.ToString()
               ?? string.Empty;
    }

    private static string[] BuildLinkHeaders(string basePath, IReadOnlyDictionary<string, string?> query, int page, int size, int totalPages)
    {
        if (string.IsNullOrWhiteSpace(basePath) || size <= 0 || totalPages <= 0)
        {
            return Array.Empty<string>();
        }

        var links = new List<string>();
        string Build(int targetPage, string rel)
        {
            var dict = new Dictionary<string, string?>(query, StringComparer.OrdinalIgnoreCase)
            {
                ["page"] = targetPage.ToString(),
                ["size"] = size.ToString()
            };
            var uri = QueryHelpers.AddQueryString(basePath, dict);
            return $"<{uri}>; rel=\"{rel}\"";
        }

        links.Add(Build(1, "first"));
        links.Add(Build(totalPages, "last"));
        if (page > 1)
        {
            links.Add(Build(page - 1, "prev"));
        }
        if (page < totalPages)
        {
            links.Add(Build(page + 1, "next"));
        }
        return links.ToArray();
    }

    private static void ApplyViewHeader(EntityRequestContext context, string? accept)
    {
        string? view = context.Options.View;
        if (!string.IsNullOrWhiteSpace(view))
        {
            context.Headers["Koan-View"] = view;
        }
        else if (!string.IsNullOrWhiteSpace(accept))
        {
            context.Headers["Koan-View"] = ParseViewFromAccept(accept) ?? "full";
        }
        else
        {
            context.Headers["Koan-View"] = "full";
        }
    }

    private static string? ParseViewFromAccept(string accept)
    {
        try
        {
            var parts = accept.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var part in parts)
            {
                var kv = part.Split('=', 2, StringSplitOptions.TrimEntries);
                if (kv.Length == 2 && kv[0].Equals("view", StringComparison.OrdinalIgnoreCase))
                {
                    return kv[1].Trim('"');
                }
            }
        }
        catch
        {
        }
        return null;
    }

    private static EntityCollectionResult<TEntity> CollectionShortCircuit(EntityRequestContext context, HookContext<TEntity> hookContext)
    {
        CopyHookHeaders(context, hookContext);
        if (hookContext.ShortCircuitResult is IActionResult action)
        {
            return new EntityCollectionResult<TEntity>(context, Array.Empty<TEntity>(), 0, null, action);
        }
        return new EntityCollectionResult<TEntity>(context, Array.Empty<TEntity>(), 0, Array.Empty<TEntity>());
    }

    private static EntityModelResult<TEntity> ModelShortCircuit(EntityRequestContext context, HookContext<TEntity> hookContext)
    {
        CopyHookHeaders(context, hookContext);
        if (hookContext.ShortCircuitResult is IActionResult action)
        {
            return new EntityModelResult<TEntity>(context, default, null, action);
        }
        return new EntityModelResult<TEntity>(context, default, null);
    }

    private sealed record RepositoryCapabilities(QueryCapabilities Value) : IQueryCapabilities
    {
        public QueryCapabilities Capabilities => Value;
    }

    private sealed record RepoWriteCaps(WriteCapabilities Value) : IWriteCapabilities
    {
        public WriteCapabilities Writes => Value;
    }
}


