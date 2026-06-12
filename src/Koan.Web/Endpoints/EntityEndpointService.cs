using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

using Microsoft.Extensions.Logging;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Instructions;
using Koan.Web.Filtering;
using Koan.Web.Hooks;
using Koan.Web.Infrastructure;

namespace Koan.Web.Endpoints;

internal sealed class EntityEndpointService<TEntity, TKey> : IEntityEndpointService<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly IDataService _dataService;
    private readonly IEntityHookPipeline<TEntity> _hookPipeline;
    private readonly ILogger<EntityEndpointService<TEntity, TKey>>? _logger;

    public EntityEndpointService(
        IDataService dataService,
        IEntityHookPipeline<TEntity> hookPipeline,
        ILogger<EntityEndpointService<TEntity, TKey>>? logger = null)
    {
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _hookPipeline = hookPipeline ?? throw new ArgumentNullException(nameof(hookPipeline));
        _logger = logger;
    }

    public async Task<EntityCollectionResult<TEntity>> GetCollection(EntityCollectionRequest request)
    {
        var context = request.Context;
        var repo = _dataService.GetRepository<TEntity, TKey>();
        context.Capabilities = Capabilities(repo);

        var hookContext = _hookPipeline.CreateContext(context);

        if (!await _hookPipeline.BuildOptions(hookContext, context.Options))
        {
            return CollectionShortCircuit(context, hookContext);
        }

        if (!await _hookPipeline.BeforeCollection(hookContext, context.Options))
        {
            return CollectionShortCircuit(context, hookContext);
        }

        RepositoryQueryResult queryResult;
        long total;
        try
        {
            queryResult = await QueryCollection(request, context.Options, context.CancellationToken);
            total = queryResult.Total;
        }
        catch (Exception ex) when (ex is InvalidOperationException or FilterParseException or InvalidFilterFieldException or NotSupportedException)
        {
            var bad = new BadRequestObjectResult(new { error = ex.Message });
            return new EntityCollectionResult<TEntity>(context, [], 0, null, bad);
        }

        if (queryResult.ExceededSafetyLimit)
        {
            _logger?.LogWarning(
                "EntityEndpointService<{Entity}> blocked unpaged response exceeding safety cap {Cap}. Path: {Path}. ReportedTotal: {Total}.",
                typeof(TEntity).Name,
                request.Policy.AbsoluteMaxRecords,
                request.BasePath ?? context.HttpContext?.Request.Path.ToString() ?? "unknown",
                queryResult.Total);

            var errorPayload = new
            {
                error = "Result too large",
                message = $"This endpoint allows at most {request.Policy.AbsoluteMaxRecords} records without pagination."
            };
            var tooLarge = new ObjectResult(errorPayload) { StatusCode = StatusCodes.Status413PayloadTooLarge };
            return new EntityCollectionResult<TEntity>(context, [], queryResult.Total, null, tooLarge);
        }

        // Sort is now applied by Data<T,K>.QueryWithCount (orchestrator) before the result reaches here.
        // The orchestrator inspects RepositoryQueryResult.SortHandled and falls back to in-memory sort
        // when the adapter cannot push it down — see DATA-0092.
        var list = queryResult.Items.ToList();

        var shouldPaginate = request.ApplyPagination;

        if (shouldPaginate && !queryResult.RepositoryHandledPagination)
        {
            (list, total) = ApplyPagination(list, context.Options.Page, context.Options.PageSize, total);
            context.Headers["Koan-InMemory-Paging"] = "true";
        }

        if (shouldPaginate)
        {
            context.Headers["X-Page"] = context.Options.Page.ToString();
            context.Headers["X-Page-Size"] = context.Options.PageSize.ToString();
            var totalPages = context.Options.PageSize > 0 ? (int)Math.Ceiling((double)total / context.Options.PageSize) : 0;
            context.Headers["X-Total-Pages"] = totalPages.ToString();
            if (request.IncludeTotalCount)
            {
                context.Headers["X-Total-Count"] = total.ToString();
            }

            if (!string.IsNullOrWhiteSpace(request.BasePath) && request.QueryParameters.Count > 0 && totalPages > 0)
            {
                var links = BuildLinkHeaders(request.BasePath!, request.QueryParameters, context.Options.Page, context.Options.PageSize, totalPages);
                if (links.Length > 0)
                {
                    context.Headers["Link"] = string.Join(", ", links);
                }
            }
        }
        else if (request.IncludeTotalCount)
        {
            context.Headers["X-Total-Count"] = total.ToString();
        }

        if (!await _hookPipeline.AfterCollection(hookContext, list))
        {
            return CollectionShortCircuit(context, hookContext);
        }

        object payload = list;

        if (!string.IsNullOrWhiteSpace(request.With) && request.With.Contains("all", StringComparison.OrdinalIgnoreCase))
        {
            payload = await EnrichRelationships(list, context.CancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(request.Shape))
        {
            payload = ApplyShape(request.Shape, list);
        }

        ApplyViewHeader(context, request.Accept);

        var emit = await _hookPipeline.EmitCollection(hookContext, payload);
        payload = emit.replaced ? emit.payload : payload;
        CopyHookHeaders(context, hookContext);

        return new EntityCollectionResult<TEntity>(context, list, total, payload);
    }

    public async Task<EntityCollectionResult<TEntity>> Query(EntityQueryRequest request)
    {
        var context = request.Context;
        var repo = _dataService.GetRepository<TEntity, TKey>();
        context.Capabilities = Capabilities(repo);

        var hookContext = _hookPipeline.CreateContext(context);

        if (!await _hookPipeline.BuildOptions(hookContext, context.Options))
        {
            return CollectionShortCircuit(context, hookContext);
        }

        if (!await _hookPipeline.BeforeCollection(hookContext, context.Options))
        {
            return CollectionShortCircuit(context, hookContext);
        }

        IReadOnlyList<TEntity> repositoryItems;
        long total;
        try
        {
            (repositoryItems, total) = await QueryCollectionFromBody(repo, request, context.Options, context.CancellationToken);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FilterParseException or InvalidFilterFieldException or NotSupportedException)
        {
            var bad = new BadRequestObjectResult(new { error = ex.Message });
            return new EntityCollectionResult<TEntity>(context, [], 0, null, bad);
        }

        // Sort + pagination handled by orchestrator inside QueryCollectionFromBody (DATA-0092).
        // The caller only sets headers — never paginates again, or we'd page-of-page (regression).
        var list = repositoryItems.ToList();
        if (context.Options.PageSize > 0)
        {
            context.Headers["X-Page"] = context.Options.Page.ToString();
            context.Headers["X-Page-Size"] = context.Options.PageSize.ToString();
            context.Headers["X-Total-Count"] = total.ToString();
        }

        if (!await _hookPipeline.AfterCollection(hookContext, list))
        {
            return CollectionShortCircuit(context, hookContext);
        }

        ApplyViewHeader(context, request.Accept);

        var emit = await _hookPipeline.EmitCollection(hookContext, list);
        var payload = emit.replaced ? emit.payload : list;
        CopyHookHeaders(context, hookContext);

        return new EntityCollectionResult<TEntity>(context, list, total, payload);
    }

    public async Task<EntityModelResult<TEntity>> GetNew(EntityGetNewRequest request)
    {
        var context = request.Context;
        var repo = _dataService.GetRepository<TEntity, TKey>();
        context.Capabilities = Capabilities(repo);

        var hookContext = _hookPipeline.CreateContext(context);

        var model = Activator.CreateInstance<TEntity>();
        await _hookPipeline.AfterModelFetch(hookContext, model);

        ApplyViewHeader(context, request.Accept);

        var emit = await _hookPipeline.EmitModel(hookContext, model!);
        var payload = emit.replaced ? emit.payload : model;
        CopyHookHeaders(context, hookContext);

        return new EntityModelResult<TEntity>(context, model, payload);
    }

    public async Task<EntityModelResult<TEntity>> GetById(EntityGetByIdRequest<TKey> request)
    {
        var context = request.Context;
        var repo = _dataService.GetRepository<TEntity, TKey>();
        context.Capabilities = Capabilities(repo);

        var hookContext = _hookPipeline.CreateContext(context);

        if (!await _hookPipeline.BeforeModelFetch(hookContext, request.Id?.ToString() ?? ""))
        {
            return ModelShortCircuit(context, hookContext);
        }

        using var _ = EntityContext.With(partition: string.IsNullOrWhiteSpace(request.Set) ? null : request.Set);
        var model = await Data<TEntity, TKey>.Get(request.Id!, context.CancellationToken);
        await _hookPipeline.AfterModelFetch(hookContext, model);
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
        var emit = await _hookPipeline.EmitModel(hookContext, model);
        var payload = emit.replaced ? emit.payload : model;
        CopyHookHeaders(context, hookContext);
        return new EntityModelResult<TEntity>(context, model, payload);
    }

    public async Task<EntityModelResult<TEntity>> Upsert(EntityUpsertRequest<TEntity> request)
    {
        var context = request.Context;
        var repo = _dataService.GetRepository<TEntity, TKey>();
        context.Capabilities = Capabilities(repo);

        var hookContext = _hookPipeline.CreateContext(context);

        await _hookPipeline.BeforeSave(hookContext, request.Model);

        TEntity saved;
        if (!string.IsNullOrWhiteSpace(request.Set))
        {
            using var _ = EntityContext.With(partition: request.Set);
            saved = await request.Model.Upsert<TEntity, TKey>(context.CancellationToken);
        }
        else
        {
            saved = await request.Model.Upsert<TEntity, TKey>(context.CancellationToken);
        }

        await _hookPipeline.AfterSave(hookContext, saved);

        ApplyViewHeader(context, request.Accept);
        var emit = await _hookPipeline.EmitModel(hookContext, saved);
        var payload = emit.replaced ? emit.payload : saved;
        CopyHookHeaders(context, hookContext);
        return new EntityModelResult<TEntity>(context, saved, payload);
    }

    public async Task<EntityEndpointResult> UpsertMany(EntityUpsertManyRequest<TEntity> request)
    {
        var context = request.Context;
        var repo = _dataService.GetRepository<TEntity, TKey>();
        context.Capabilities = Capabilities(repo);

        var hookContext = _hookPipeline.CreateContext(context);

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
            await _hookPipeline.BeforeSave(hookContext, model);
        }

        using var _ = EntityContext.With(partition: string.IsNullOrWhiteSpace(request.Set) ? null : request.Set);
        var upserted = await Data<TEntity, TKey>.UpsertMany(list, context.CancellationToken);

        foreach (var model in list)
        {
            await _hookPipeline.AfterSave(hookContext, model);
        }

        context.Headers["Koan-Write-Capabilities"] = WriteCapabilitiesHeader(repo);
        CopyHookHeaders(context, hookContext);
        return new EntityEndpointResult(context, new { upserted });
    }

    public async Task<EntityModelResult<TEntity>> Delete(EntityDeleteRequest<TKey> request)
    {
        var context = request.Context;
        var repo = _dataService.GetRepository<TEntity, TKey>();
        context.Capabilities = Capabilities(repo);

        var hookContext = _hookPipeline.CreateContext(context);

        using var _ = EntityContext.With(partition: string.IsNullOrWhiteSpace(request.Set) ? null : request.Set);
        var model = await Data<TEntity, TKey>.Get(request.Id, context.CancellationToken);
        if (model is null)
        {
            return new EntityModelResult<TEntity>(context, null, null, new NotFoundResult());
        }

        await _hookPipeline.BeforeDelete(hookContext, model);
        var ok = await Data<TEntity, TKey>.Delete(request.Id, context.CancellationToken);
        if (!ok)
        {
            return new EntityModelResult<TEntity>(context, null, null, new NotFoundResult());
        }
        await _hookPipeline.AfterDelete(hookContext, model);

        ApplyViewHeader(context, request.Accept);
        var emit = await _hookPipeline.EmitModel(hookContext, model);
        var payload = emit.replaced ? emit.payload : model;
        CopyHookHeaders(context, hookContext);
        return new EntityModelResult<TEntity>(context, model, payload);
    }

    public async Task<EntityEndpointResult> DeleteMany(EntityDeleteManyRequest<TKey> request)
    {
        var context = request.Context;
        var repo = _dataService.GetRepository<TEntity, TKey>();
        context.Headers["Koan-Write-Capabilities"] = WriteCapabilitiesHeader(repo);
        using var _ = EntityContext.With(partition: string.IsNullOrWhiteSpace(request.Set) ? null : request.Set);
        var deleted = await Data<TEntity, TKey>.DeleteMany(request.Ids ?? [], context.CancellationToken);
        return new EntityEndpointResult(context, new { deleted });
    }

    public async Task<EntityEndpointResult> DeleteByQuery(EntityDeleteByQueryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return new EntityEndpointResult(request.Context, null, new BadRequestResult());
        }

        using var _ = EntityContext.With(partition: string.IsNullOrWhiteSpace(request.Set) ? null : request.Set);

        // Parse the JSON filter DSL into the unified Filter AST. Works on every adapter (the
        // coordinator pushes what it can and evaluates the rest in-memory) — never a silent match-all.
        Filter filter;
        try
        {
            filter = JsonFilterParser.Parse<TEntity>(request.Query);
        }
        catch (Exception ex) when (ex is FilterParseException or InvalidFilterFieldException)
        {
            return new EntityEndpointResult(request.Context, null, new BadRequestObjectResult(new { error = ex.Message }));
        }

        var items = await Data<TEntity, TKey>.All(QueryDefinition.All.Where(filter), request.Context.CancellationToken);
        var ids = items.Select(e => e.Id).ToList();
        if (ids.Count == 0) return new EntityEndpointResult(request.Context, new { deleted = 0 });
        var removedByPredicate = await Data<TEntity, TKey>.DeleteMany(ids, request.Context.CancellationToken);
        return new EntityEndpointResult(request.Context, new { deleted = removedByPredicate });
    }

    public async Task<EntityEndpointResult> DeleteAll(EntityDeleteAllRequest request)
    {
        using var _ = EntityContext.With(partition: string.IsNullOrWhiteSpace(request.Set) ? null : request.Set);
        var deleted = await Entity<TEntity, TKey>.RemoveAll(request.Context.CancellationToken);
        return new EntityEndpointResult(request.Context, new { deleted });
    }

    public async Task<EntityModelResult<TEntity>> Patch(EntityPatchRequest<TEntity, TKey> request)
    {
        var context = request.Context;
        var repo = _dataService.GetRepository<TEntity, TKey>();
        context.Capabilities = Capabilities(repo);

        var hookContext = _hookPipeline.CreateContext(context);

        await _hookPipeline.BeforePatch(hookContext, request.Id?.ToString() ?? "", request.Patch!);

        using var _ = EntityContext.With(partition: string.IsNullOrWhiteSpace(request.Set) ? null : request.Set);
        var original = await Data<TEntity, TKey>.Get(request.Id!, context.CancellationToken);
        if (original is null)
        {
            return new EntityModelResult<TEntity>(context, null, null, new NotFoundResult());
        }

        var working = await Data<TEntity, TKey>.Get(request.Id!, context.CancellationToken);
        if (working is null)
        {
            return new EntityModelResult<TEntity>(context, null, null, new NotFoundResult());
        }

        // Apply generalized patch
        if (request.Patch is Microsoft.AspNetCore.JsonPatch.JsonPatchDocument<TEntity> jp)
        {
            jp.ApplyTo(working);
        }
        else if (request.Patch is Newtonsoft.Json.Linq.JToken jt)
        {
            var opts = context.HttpContext?.RequestServices.GetService(typeof(Microsoft.Extensions.Options.IOptions<Koan.Web.Options.KoanWebOptions>)) as Microsoft.Extensions.Options.IOptions<Koan.Web.Options.KoanWebOptions>;
            var mergePolicy = opts?.Value.MergePatchNullsForNonNullable ?? MergePatchNullPolicy.SetDefault;
            var partialPolicy = opts?.Value.PartialJsonNulls ?? PartialJsonNullPolicy.SetNull;
            Koan.Data.Abstractions.Instructions.IPatchApplicator<TEntity> applicator = request.Kind == PatchKind.MergePatch7386
                ? new Koan.Data.Core.Patch.MergePatchApplicator<TEntity>(jt, mergePolicy)
                : new Koan.Data.Core.Patch.PartialJsonApplicator<TEntity>(jt, partialPolicy);
            applicator.Apply(working);
        }
        else
        {
            return new EntityModelResult<TEntity>(context, null, null, new BadRequestObjectResult(new { error = "Unsupported patch payload" }));
        }
        var idProp = typeof(TEntity).GetProperty("Id");
        if (idProp is not null)
        {
            var newId = idProp.GetValue(working);
            if (newId is not null && !Equals(newId, request.Id))
            {
                return new EntityModelResult<TEntity>(context, null, null, new ConflictResult());
            }
        }

        await _hookPipeline.BeforeSave(hookContext, working!);
        var saved = await working!.Upsert<TEntity, TKey>(context.CancellationToken);
        await _hookPipeline.AfterPatch(hookContext, saved);

        ApplyViewHeader(context, request.Accept);
        var emit = await _hookPipeline.EmitModel(hookContext, saved);
        var payload = emit.replaced ? emit.payload : saved;
        CopyHookHeaders(context, hookContext);
        return new EntityModelResult<TEntity>(context, saved, payload);
    }

    // ARCH-0084: negotiate via the unified CapabilitySet (the adapter's IDescribesCapabilities declaration).
    private static CapabilitySet Capabilities(IDataRepository<TEntity, TKey> repo)
        => DataCaps.Describe(repo, repo.GetType().Name);

    // Renders the Koan-Write-Capabilities header as the declared write capability tokens (ARCH-0084).
    private static string WriteCapabilitiesHeader(IDataRepository<TEntity, TKey> repo)
        => string.Join(", ", DataCaps.Describe(repo, repo.GetType().Name).All
            .Where(c => c.Id.StartsWith("write.", StringComparison.Ordinal)).Select(c => c.Id));

    private static void CopyHookHeaders(EntityRequestContext context, HookContext<TEntity> hookContext)
    {
        foreach (var kv in hookContext.ResponseHeaders)
        {
            context.Headers[kv.Key] = kv.Value;
        }
    }

    private static (List<TEntity> Items, long Total) ApplyPagination(List<TEntity> source, int page, int pageSize, long total)
    {
        if (pageSize <= 0)
        {
            return (source, total);
        }

        var skip = Math.Max(page - 1, 0) * pageSize;
        var items = source.Skip(skip).Take(pageSize).ToList();
        return (items, total);
    }
    // In-memory sort moved to Koan.Data.Core.Sorting.InMemorySorter — orchestrator handles fallback.
    // CreateKeySelector replaced by structured MemberPath walking. See DATA-0092.


    private sealed class RepositoryQueryResult
    {
        public RepositoryQueryResult(IReadOnlyList<TEntity> items, long total, bool handled, bool exceededLimit)
        {
            Items = items;
            Total = total;
            RepositoryHandledPagination = handled;
            ExceededSafetyLimit = exceededLimit;
        }

        public IReadOnlyList<TEntity> Items { get; }
        public long Total { get; }
        public bool RepositoryHandledPagination { get; }
        public bool ExceededSafetyLimit { get; }
    }

    private async Task<RepositoryQueryResult> QueryCollection(
        EntityCollectionRequest request,
        QueryOptions options,
        CancellationToken cancellationToken)
    {
        using var _ = EntityContext.With(partition: string.IsNullOrWhiteSpace(request.Set) ? null : request.Set);

        Filter? userFilter = null;
        if (!string.IsNullOrWhiteSpace(request.FilterJson))
        {
            userFilter = JsonFilterParser.Parse<TEntity>(request.FilterJson!, new FilterParseOptions { IgnoreCase = request.IgnoreCase });
        }

        // WEB-0068: hook-contributed predicates AND-compose with the user's filter (all lowered to one
        // Filter AST) so the adapter counts and pages against the already-filtered set. When any filter
        // exists, free-text Q is dropped — Q reaches the raw provider surface only.
        var composed = QueryFilterComposer.AndAll<TEntity>(userFilter, options.Predicates);
        var queryDef = BuildQueryDefinition(request, options);
        var absoluteMax = request.AbsoluteMaxRecords > 0 ? request.AbsoluteMaxRecords : (int?)null;

        if (composed is null && !string.IsNullOrWhiteSpace(options.Q))
        {
            var raw = await Data<TEntity, TKey>.QueryRaw(options.Q!, null, queryDef, cancellationToken);
            return new RepositoryQueryResult(raw, raw.Count, false, false);
        }
        if (composed is not null && !string.IsNullOrWhiteSpace(options.Q))
        {
            _logger?.LogInformation(
                "EntityEndpointService<{Entity}> dropped free-text Q because IRequestOptionsHook(s) contributed {Count} predicate(s). See WEB-0068.",
                typeof(TEntity).Name,
                options.Predicates.Count);
        }

        var result = await Data<TEntity, TKey>.QueryWithCount(queryDef.Where(composed), cancellationToken, absoluteMax);

        return new RepositoryQueryResult(result.Items, result.TotalCount, result.RepositoryHandledPagination, result.ExceededSafetyLimit);
    }

    private static QueryDefinition BuildQueryDefinition(EntityCollectionRequest request, QueryOptions options)
    {
        var def = QueryDefinition.All;
        if (request.ApplyPagination && options.Page > 0 && options.PageSize > 0)
            def = def.WithPagination(options.Page, options.PageSize);
        if (!string.IsNullOrWhiteSpace(request.Set))
            def = def.ForPartition(request.Set);
        if (options.Sort.Count > 0)
            def = def.WithSort(options.Sort);
        return def;
    }

    private async Task<(IReadOnlyList<TEntity> Items, long Total)> QueryCollectionFromBody(
        IDataRepository<TEntity, TKey> repo,
        EntityQueryRequest request,
        QueryOptions options,
        CancellationToken cancellationToken)
    {
        using var _ = EntityContext.With(partition: string.IsNullOrWhiteSpace(request.Set) ? null : request.Set);

        // Route body queries through the orchestrator (Data<T,K>.QueryWithCount) so the unified
        // QueryDefinition contract handles split/residual/sort/paginate-after centrally.
        var queryDef = QueryDefinition.All;
        if (options.PageSize > 0 && options.Page > 0)
            queryDef = queryDef.WithPagination(options.Page, options.PageSize);
        if (!string.IsNullOrWhiteSpace(request.Set))
            queryDef = queryDef.ForPartition(request.Set);
        if (options.Sort.Count > 0)
            queryDef = queryDef.WithSort(options.Sort);

        Filter? userFilter = null;
        if (!string.IsNullOrWhiteSpace(request.FilterJson))
        {
            userFilter = JsonFilterParser.Parse<TEntity>(request.FilterJson!, new FilterParseOptions { IgnoreCase = request.IgnoreCase });
        }

        // WEB-0068: same composition rule as the GET path — hook predicates AND with the user's
        // filter (one Filter AST), free-text Q is dropped when any filter contributes.
        var composed = QueryFilterComposer.AndAll<TEntity>(userFilter, options.Predicates);

        if (composed is null && !string.IsNullOrWhiteSpace(options.Q))
        {
            var raw = await Data<TEntity, TKey>.QueryRaw(options.Q!, null, queryDef, cancellationToken);
            return (raw, raw.Count);
        }
        if (composed is not null && !string.IsNullOrWhiteSpace(options.Q))
        {
            _logger?.LogInformation(
                "EntityEndpointService<{Entity}> dropped free-text Q (body-query) because IRequestOptionsHook(s) contributed {Count} predicate(s). See WEB-0068.",
                typeof(TEntity).Name,
                options.Predicates.Count);
        }

        var result = await Koan.Data.Core.Data<TEntity, TKey>.QueryWithCount(queryDef.Where(composed), cancellationToken);
        return (result.Items, result.TotalCount);
    }

    private static async Task<IReadOnlyList<object>> EnrichRelationships(IReadOnlyList<TEntity> list, CancellationToken cancellationToken)
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
               ?? "";
    }

    private static string[] BuildLinkHeaders(string basePath, IReadOnlyDictionary<string, string?> query, int page, int size, int totalPages)
    {
        if (string.IsNullOrWhiteSpace(basePath) || size <= 0 || totalPages <= 0)
        {
            return [];
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
        var shortCircuit = hookContext.ShortCircuitPayload;
        if (shortCircuit is IActionResult action)
        {
            return new EntityCollectionResult<TEntity>(context, [], 0, null, action);
        }
        return new EntityCollectionResult<TEntity>(context, [], 0, shortCircuit, shortCircuit);
    }

    private static EntityModelResult<TEntity> ModelShortCircuit(EntityRequestContext context, HookContext<TEntity> hookContext)
    {
        CopyHookHeaders(context, hookContext);
        var shortCircuit = hookContext.ShortCircuitPayload;
        if (shortCircuit is IActionResult action)
        {
            return new EntityModelResult<TEntity>(context, default, null, action);
        }
        return new EntityModelResult<TEntity>(context, default, shortCircuit, shortCircuit);
    }

}











