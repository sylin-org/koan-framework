using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Koan.Data.Core.Lifecycle;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Instructions;
using Koan.Web.Attributes;
using Koan.Web.Authorization;
using Koan.Web.Controllers;
using Koan.Web.Hooks;
using Koan.Web.Infrastructure;

namespace Koan.Web.Endpoints;

internal sealed class EntityEndpointService<TEntity, TKey> : IEntityEndpointService<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly IDataService _dataService;
    private readonly IEntityHookPipeline<TEntity> _hookPipeline;
    private readonly IAuthorize? _authorize;
    private readonly IAccessGateCache? _gateCache;
    private readonly ILogger<EntityEndpointService<TEntity, TKey>>? _logger;

    // SEC-0005: an [Audit] entity writes one AgentAction per successful MUTATION (write/remove) through the normal
    // entity path; reads are never audited. Computed once per closed generic. A bulk op records one row (EntityId="").
    private static readonly bool IsAudited = typeof(TEntity).GetCustomAttribute<AuditAttribute>(inherit: true) is not null;

    private static async System.Threading.Tasks.Task AuditMutation(EntityRequestContext context, string action, string entityId)
    {
        if (!IsAudited) return;
        await new AgentAction
        {
            Subject = AuthSubject.Id(context.User) ?? "anonymous",
            Resource = typeof(TEntity).Name,
            Action = action,
            EntityId = entityId,
            At = DateTimeOffset.UtcNow,
        }.Save(context.CancellationToken).ConfigureAwait(false);
    }

    public EntityEndpointService(
        IDataService dataService,
        IEntityHookPipeline<TEntity> hookPipeline,
        IAuthorize? authorize = null,
        IAccessGateCache? gateCache = null,
        ILogger<EntityEndpointService<TEntity, TKey>>? logger = null)
    {
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _hookPipeline = hookPipeline ?? throw new ArgumentNullException(nameof(hookPipeline));
        _authorize = authorize;
        _gateCache = gateCache;
        _logger = logger;
    }

    // ARCH-0092 (§D): the single cross-surface authorization gate. Maps the operation onto a read/write/remove
    // action and asks the unified IAuthorize seam (resource = the entity type). Returns null to PROCEED (the seam
    // allowed, or no seam is registered = allow-by-default); otherwise the denying decision (Forbid/Challenge),
    // which each surface translates. Both REST and the MCP edge run through this one service, so this is the one
    // place base CRUD is authorized.
    private async Task<AuthorizeDecision?> Gate(EntityRequestContext context, string action)
    {
        // A selected transport handler explicitly rejected the caller's credential. This is not the anonymous
        // case: fail before hooks or persistence even when the declared action gate is open/Anyone.
        if (context.AuthenticationRejected) return AuthorizeDecision.Challenged();
        if (_authorize is null) return null;
        // Memoize per (action) for the lifetime of this request: the operation's guard and AnnotateAccess both
        // ask for the same verbs, so a verb is evaluated through the seam at most once — which matters once an
        // external PDP/ReBAC rung joins the ladder — and the gate decision stays consistent within the request.
        var key = "Koan.Access.Gate." + action;
        if (context.Items.TryGetValue(key, out var cached)) return (AuthorizeDecision?)cached;
        // SEC-0011: the same decision every MCP listing asks before advertising this verb.
        var result = await EntityAuthorization.Decide(_authorize, context.User, action, typeof(TEntity))
            .ConfigureAwait(false);
        context.Items[key] = result;
        return result;
    }

    // SEC-0004 (§C) — honest capability advertisement: the single-item form of the per-row projection. One
    // open-vocabulary list header naming the verbs THIS principal may perform (a verb is permitted when its gate
    // returns null = allow). Replaces the three Koan-Access-Read/Write/Remove booleans. Reached only on a request
    // whose own action gate passed, so the list always includes at least that verb. Gate() is memoized per
    // request, so the guard + these three calls evaluate each verb at most once. Slice B/C extend the same list
    // with custom verbs and the per-row can:[] sidecar, sharing this per-verb gate-allow computation.
    private async Task AnnotateAccess(EntityRequestContext context)
    {
        if (_authorize is null) return;
        var verbs = new List<string>(3);
        if (await Gate(context, EntityAuthorizeActions.Read).ConfigureAwait(false) is null) verbs.Add(EntityAuthorizeActions.Read);
        if (await Gate(context, EntityAuthorizeActions.Write).ConfigureAwait(false) is null) verbs.Add(EntityAuthorizeActions.Write);
        if (await Gate(context, EntityAuthorizeActions.Remove).ConfigureAwait(false) is null) verbs.Add(EntityAuthorizeActions.Remove);
        context.Headers["Koan-Access"] = string.Join(", ", verbs);
    }

    private static EntityCollectionResult<TEntity> CollectionDenied(EntityRequestContext context, AuthorizeDecision decision)
        => new(context, Array.Empty<TEntity>(), 0, payload: null, shortCircuit: decision);

    private static EntityModelResult<TEntity> ModelDenied(EntityRequestContext context, AuthorizeDecision decision)
        => new(context, default, payload: null, shortCircuit: decision);

    private static EntityEndpointResult Denied(EntityRequestContext context, AuthorizeDecision decision)
        => new(context, payload: null, shortCircuit: decision);

    private static async Task<IActionResult?> AdmitFields(EntityRequestContext context, Action<FieldAccess> inspect)
    {
        try
        {
            var fields = context.FieldAccess;
            if (fields is null)
            {
                fields = await FieldAccess.Prepare(typeof(TEntity), context.Services, context.User,
                    context.CancellationToken).ConfigureAwait(false);
                context.BindFieldAccess(fields);
            }
            if (!fields.IsCurrent(typeof(TEntity), context.User))
                throw new InvalidOperationException("Field access no longer matches the entity or request principal.");
            inspect(fields);
            return null;
        }
        catch (Exception error) when (error is UnauthorizedAccessException or NotSupportedException
            or InvalidOperationException or FilterParseException or InvalidFilterFieldException)
        {
            context.Headers.Clear();
            context.Items.Remove(AccessProjection.ManifestKey);
            return new ObjectResult(new
            {
                code = error is UnauthorizedAccessException ? KoanWebConstants.Codes.FieldAccess.Denied
                    : KoanWebConstants.Codes.FieldAccess.Unsupported,
                error = error.Message
            }) { StatusCode = error is UnauthorizedAccessException ? StatusCodes.Status403Forbidden : StatusCodes.Status400BadRequest };
        }
    }

    private static Task<IActionResult?> AdmitFieldOutput(EntityRequestContext context, object? payload)
        => AdmitFields(context, fields =>
        {
            if (!fields.HasRestrictions) return;
            var value = payload switch
            {
                ObjectResult result => result.Value,
                JsonResult result => result.Value,
                _ => payload
            };
            if (value is string or Newtonsoft.Json.Linq.JToken or byte[]
                || value is IActionResult and not StatusCodeResult)
                throw new NotSupportedException("Conditional entity fields require a typed output. Return the entity or a typed view instead of a raw or pre-serialized replacement.");
        });

    private static void AdmitCallerQuery(FieldAccess fields, string? filterJson, QueryOptions options)
    {
        if (!string.IsNullOrWhiteSpace(filterJson)) fields.DemandFilter(JsonFilterParser.Parse<TEntity>(filterJson));
        foreach (var sort in options.Sort) fields.DemandReadPath(sort.Path.DotPath);
        fields.DemandShape(options.Shape);
    }

    // SEC-0004 (§B): the per-request EntityAccess<TEntity> realization (null = no Constrain → byte-identical to
    // today). Resolved + principal-bound once, memoized on the context. Reads ride the open-generic
    // IRequestOptionsHook; these write/delete paths (which never call BuildOptions) resolve it directly.
    private static EntityAccess<TEntity>? ResolveAccessor(EntityRequestContext context)
    {
        const string key = "Koan.Access.Accessor";
        if (context.Items.TryGetValue(key, out var cached)) return (EntityAccess<TEntity>?)cached;
        var accessor = context.Services.GetService<EntityAccess<TEntity>>();
        accessor?.Bind(context);
        context.Items[key] = accessor;
        return accessor;
    }

    // Run the realization's Constrain for one action and return the populated accumulator (Predicates + pending
    // owner Stamps). The author composes Owner via q.Where(Owner) / q.Stamp(ownerSelector, CurrentUserId).
    private static AccessFilter<TEntity> ConstrainFor(EntityAccess<TEntity> accessor, AccessAction action)
    {
        var filter = new AccessFilter<TEntity>();
        accessor.Constrain(filter, action);
        if (action != AccessAction.Read)
            Filter.RequireRowOnly(filter.Filter, $"{action} access constraint");
        return filter;
    }

    // WEB-0077: a delete must raise the model delete hooks per row when any may exist. A custom pipeline may hold hooks
    // the default runner cannot see, so it always does.
    private bool RaisesDeleteHooks => _hookPipeline is not DefaultEntityHookPipeline<TEntity> pipeline || pipeline.HasModelHooks;

    // WEB-0077: BeforeDelete for every row of a batch, before any is deleted. The first short circuit refuses the batch.
    private async Task<bool> BeforeDeleteAll(HookContext<TEntity> hookContext, IEnumerable<TEntity> rows)
    {
        foreach (var row in rows)
            if (!await _hookPipeline.BeforeDelete(hookContext, row)) return false;
        return true;
    }

    private async Task<bool> AfterDeleteAll(HookContext<TEntity> hookContext, IEnumerable<TEntity> rows)
    {
        foreach (var row in rows)
            if (!await _hookPipeline.AfterDelete(hookContext, row)) return false;
        return true;
    }

    // SEC-0013: the rows a principal may delete. The Delete constraint, else the Read constraint ("delete only rows I
    // can see"), so a realization that scopes reads but forgets Delete bounds every delete verb, not only DeleteAll.
    // SEC-0019: a Read fallback may be a counterpart predicate; the verbs evaluate it through the Data facade.
    private static Filter? DeleteBound(EntityAccess<TEntity> accessor)
        => ConstrainFor(accessor, AccessAction.Delete).Filter ?? ConstrainFor(accessor, AccessAction.Read).Filter;

    // SEC-0019: the rows among ids that a record-access bound (a delete's, or an insert conflict's Read) admits, in request
    // order. A row-only bound is checked on the loaded row; a counterpart bound only the store can answer, so it runs as one
    // scoped query through the Data facade, the path a governed read takes. A connector without counterpart reads refuses
    // there, as reads do.
    private static async Task<List<TEntity>> RowsInBound(IReadOnlyCollection<TKey> ids, Filter? bound, CancellationToken ct)
    {
        var rows = new List<TEntity>(ids.Count);
        if (Filter.HasCounterpart(bound))
        {
            var keys = ids.Distinct().Select(static id => (object?)id).ToArray();
            var found = (await Data<TEntity, TKey>.All(QueryDefinition.All.Where(
                Filter.And(Filter.In(nameof(IEntity<TKey>.Id), keys), bound)), ct)).ToDictionary(row => row.Id);
            foreach (var id in ids)
                if (found.TryGetValue(id, out var row)) rows.Add(row);
            return rows;
        }
        foreach (var id in ids)
        {
            var row = await Data<TEntity, TKey>.Get(id, ct);
            if (row is not null && PassesRequestPredicates(row, bound)) rows.Add(row);
        }
        return rows;
    }

    // NK-293 / DATA-0156: the delete bound was checked on the loaded rows, and the delete then runs by identity. Where
    // the adapter deletes under a stored-row guard and the bound is a row predicate it pushes completely, the bound
    // travels with the delete, so a row that left the principal's scope after the check is not removed and reads as
    // a 404. Otherwise the checked delete stands; it is not atomic.
    private static IConditionalDeleteRepository<TEntity, TKey>? GuardedDeletes(Filter? bound)
    {
        if (bound is null || Filter.HasCounterpart(bound) || EntityContext.Current?.TransactionCoordinator is not null)
            return null;
        if (Data<TEntity, TKey>.As<IConditionalDeleteRepository<TEntity, TKey>>() is not { } conditional) return null;
        var support = DataCaps.Describe(conditional, conditional.GetType().Name).Detail<FilterSupport>(DataCaps.Query.Filter)
            ?? FilterSupport.None;
        return FilterSplitter.Split(bound, support, typeof(TEntity)).Residual is null ? conditional : null;
    }

    private static async Task<bool> DeleteWithin(TKey id, Filter? bound, CancellationToken ct)
    {
        if (GuardedDeletes(bound) is { } guarded)
        {
            // Data refuses a guard it cannot honour (an identity or field shape) before it writes anything.
            try { return await guarded.ConditionalDeleteAsync(id, bound!, ct); }
            catch (NotSupportedException) { }
        }
        return await Data<TEntity, TKey>.Delete(id, ct);
    }

    private static async Task<int> DeleteManyWithin(IReadOnlyCollection<TKey> ids, Filter? bound, CancellationToken ct)
    {
        if (GuardedDeletes(bound) is { } guarded)
        {
            try { return await guarded.ConditionalDeleteMany(ids as IReadOnlyList<TKey> ?? ids.ToList(), bound!, ct); }
            catch (NotSupportedException) { }
        }
        return await Data<TEntity, TKey>.DeleteMany(ids, ct);
    }

    // Read visibility is independent of mutation authority. Inspect actual mutation declarations,
    // not the presence of an access realization, and reuse the request-bound accumulators.
    private (AccessFilter<TEntity>? Create, AccessFilter<TEntity>? Update) MutationConstraints(EntityRequestContext context)
    {
        var accessor = ResolveAccessor(context);
        if (accessor is null) return (null, null);
        var create = ConstrainFor(accessor, AccessAction.Create);
        var update = ConstrainFor(accessor, AccessAction.Update);
        return create.HasStamps || create.Filter is not null || update.HasStamps || update.Filter is not null
            ? (create, update)
            : (null, null);
    }

    // SEC-0004 (§C): does the COARSE seam allow this verb at all (respecting every IAuthorize provider)? The outer
    // guard of the per-row projection — the row-bound gate + Constrain then refine it. Gate() is memoized per verb.
    private async Task<bool> CoarseAllows(EntityRequestContext context, string action)
        => _authorize is null || await Gate(context, action).ConfigureAwait(false) is null;

    // SEC-0004 (§C): assemble the per-row projector ONCE per request — coarse seam decisions, the entity's compiled
    // gate (the SAME gate the floor provider enforces, so the projection never disagrees with enforcement), the
    // principal, the realization's single Owner predicate, and the per-verb Constrain predicates (Update is the
    // row-bound write; Delete the row-bound remove).
    private async Task<RowProjection<TEntity>> CreateProjector(EntityRequestContext context,
        Filter? frozenReadFilter, EndpointReadProof<TEntity, TKey>? proof = null)
    {
        var coarseRead = await CoarseAllows(context, EntityAuthorizeActions.Read).ConfigureAwait(false);
        var coarseWrite = await CoarseAllows(context, EntityAuthorizeActions.Write).ConfigureAwait(false);
        var coarseRemove = await CoarseAllows(context, EntityAuthorizeActions.Remove).ConfigureAwait(false);

        var gate = _gateCache?.GetOrCompile(typeof(TEntity)) ?? AccessGate.Open;
        var accessor = ResolveAccessor(context);
        var authed = context.User.Identity?.IsAuthenticated == true;
        var owner = accessor?.OwnerExpression?.Compile();

        var writeFilter = accessor is null ? null : ConstrainFor(accessor, AccessAction.Update).Filter;
        // SEC-0019: with no Delete constraint the remove bound is the Read constraint this request already executed, so
        // `remove` follows the row's read (its query proof for a counterpart) instead of re-running the Read declaration.
        var removeFilter = accessor is null ? null : ConstrainFor(accessor, AccessAction.Delete).Filter;
        var removeFollowsRead = accessor is not null && removeFilter is null;

        return new RowProjection<TEntity>(gate, context.User, coarseRead, coarseWrite, coarseRemove,
            owner, authed, frozenReadFilter, writeFilter, removeFilter, proof is null ? null : proof.Contains, removeFollowsRead);
    }

    // SEC-0004 (§C): the per-row can:[] manifest for a set of rows (id → { can }). Computed once per request and
    // stored on the context for any surface to render — REST wraps it as the `access` sidecar, the MCP edge
    // attaches it to the tool-result metadata.
    private async Task<Dictionary<string, object>> BuildAccessManifest(EntityRequestContext context,
        IReadOnlyList<TEntity> rows, Filter? frozenReadFilter, EndpointReadProof<TEntity, TKey>? proof)
    {
        var projector = await CreateProjector(context, frozenReadFilter, proof).ConfigureAwait(false);
        var manifest = new Dictionary<string, object>(rows.Count, StringComparer.Ordinal);
        foreach (var row in rows)
        {
            // The key is the row's id rendered as a string — it must match the `id` field the client reads off the
            // serialized item to correlate. This holds for the canonical IEntity key types (string / Guid /
            // numeric: ToString() is the same text JSON emits). The verb list is lowercase-by-design so no
            // serializer naming policy reshapes the manifest's `can` / `items` / `access` keys.
            var id = GetEntityId(row)?.ToString();
            if (id is null) continue;
            manifest[id] = new { can = projector.Can(row) };
        }
        context.Items[AccessProjection.ManifestKey] = manifest;
        return manifest;
    }

    // SEC-0004 (§C): a request opts into the projection when a surface asks — the MCP edge sets RequestKey by
    // default; REST sets includeAccess from ?access=true. wrapRest distinguishes the REST sidecar (wrap the payload
    // into { items, access }) from the MCP path (manifest read off the context; the bare payload is unchanged).
    private static bool ShouldProject(EntityRequestContext context, bool includeAccess, out bool wrapRest)
    {
        wrapRest = includeAccess;
        return includeAccess || context.Items.ContainsKey(AccessProjection.RequestKey);
    }

    public async Task<EntityCollectionResult<TEntity>> GetCollection(EntityCollectionRequest request)
    {
        var context = request.Context;
        context.Options.Shape = request.Shape ?? context.Options.Shape;
        if (request.With is not null)
            context.Options.IncludeRelationships = request.With.Contains("all", StringComparison.OrdinalIgnoreCase);
        if (await Gate(context, EntityAuthorizeActions.Read).ConfigureAwait(false) is { } denied) return CollectionDenied(context, denied);
        if (await AdmitFields(context, fields => AdmitCallerQuery(fields, request.FilterJson, context.Options)) is { } fieldDenied)
            return new EntityCollectionResult<TEntity>(context, [], 0, null, fieldDenied);
        await AnnotateAccess(context).ConfigureAwait(false);
        var repo = _dataService.GetRepository<TEntity, TKey>();
        context.Capabilities = Capabilities(repo);

        var hookContext = _hookPipeline.CreateContext(context);

        if (!await _hookPipeline.BuildOptions(hookContext, context.Options))
        {
            return await CollectionShortCircuit(context, hookContext);
        }

        // WEB-0076: a paged read serves at most the policy's page size, whichever surface asked.
        if (request.ApplyPagination)
            (context.Options.Page, context.Options.PageSize) = request.Policy.Clamp(context.Options.Page, context.Options.PageSize);
        var safetyLimit = request.AbsoluteMaxRecords > 0 ? request.AbsoluteMaxRecords : request.Policy.AbsoluteMaxRecords;

        QueryDefinition query;
        try
        {
            query = FreezeReadQuery(BuildQueryDefinition(request, context.Options),
                request.FilterJson, request.IgnoreCase, context.Options);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FilterParseException or InvalidFilterFieldException or NotSupportedException)
        {
            return new EntityCollectionResult<TEntity>(context, [], 0, null, new BadRequestObjectResult(new { error = ex.Message }));
        }
        var frozenReadFilter = context.Options.Filter;
        var shape = context.Options.Shape;
        var includeRelationships = context.Options.IncludeRelationships;
        var proof = EndpointReadProof<TEntity, TKey>.Prepare(context, query);

        if (!await _hookPipeline.BeforeCollection(hookContext, context.Options))
        {
            return await CollectionShortCircuit(context, hookContext, proof is not null);
        }
        if (proof is not null && !proof.IsRequestUnchanged(context))
            return new EntityCollectionResult<TEntity>(context, [], 0, null, ReadEvidenceChanged(context));

        RepositoryQueryResult queryResult;
        long total;
        try
        {
            queryResult = await QueryCollection(query, context.Options.Q, request.AllowRawQuery,
                safetyLimit, context.CancellationToken);
            total = queryResult.Total;
        }
        catch (FreeTextRefusedException refused)
        {
            return new EntityCollectionResult<TEntity>(context, [], 0, null, FreeTextRefused(refused));
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
                safetyLimit,
                request.BasePath ?? context.HttpContext?.Request.Path.ToString() ?? "unknown",
                queryResult.Total);

            return new EntityCollectionResult<TEntity>(context, [], queryResult.Total, null, TooLarge(safetyLimit));
        }

        // Sort is applied by the shared Data<T,K> materialized-query orchestrator before the result reaches here.
        // The orchestrator inspects RepositoryQueryResult.SortHandled and falls back to in-memory sort
        // when the adapter cannot push it down — see DATA-0092.
        var list = queryResult.Items.ToList();
        if (proof is not null && !proof.Bind(queryResult.ReadEvidence, list))
            return new EntityCollectionResult<TEntity>(context, [], 0, null, ReadEvidenceChanged(context));

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
            if (request.IncludeTotalCount)
            {
                var totalPages = context.Options.PageSize > 0 ? (int)Math.Ceiling((double)total / context.Options.PageSize) : 0;
                context.Headers["X-Total-Pages"] = totalPages.ToString();
                context.Headers["X-Total-Count"] = total.ToString();

                if (!string.IsNullOrWhiteSpace(request.BasePath) && request.QueryParameters.Count > 0 && totalPages > 0)
                {
                    var links = BuildLinkHeaders(request.BasePath!, request.QueryParameters, context.Options.Page, context.Options.PageSize, totalPages);
                    if (links.Length > 0)
                    {
                        context.Headers["Link"] = string.Join(", ", links);
                    }
                }
            }
        }
        else if (request.IncludeTotalCount)
        {
            context.Headers["X-Total-Count"] = total.ToString();
        }

        if (!await _hookPipeline.AfterCollection(hookContext, list))
        {
            return await CollectionShortCircuit(context, hookContext, proof is not null);
        }
        if (proof is not null && !proof.IsValid(context, list))
            return new EntityCollectionResult<TEntity>(context, [], 0, null, ReadEvidenceChanged(context));

        object payload = list;
        var relationshipEvidence = new List<Func<bool>>();
        var projectionSourcesUnchanged = CaptureProjectionSources(context, query, list, proof);
        var emit = await _hookPipeline.EmitCollection(hookContext, list);
        if (hookContext.IsShortCircuited) return await CollectionShortCircuit(context, hookContext, proof is not null);
        if (emit.payload is EmitDecision.DeferredProjection projection)
        {
            if (!ProjectionKeySupported())
                return new EntityCollectionResult<TEntity>(context, [], 0, null, ProjectionKeyRejected());
            if (!IsFlatProjection(context.Options))
                return new EntityCollectionResult<TEntity>(context, [], 0, null, ProjectionShapeRejected());
            if (!projection.Matches(list) || !projectionSourcesUnchanged())
                return new EntityCollectionResult<TEntity>(context, [], 0, null, ReadEvidenceChanged(context));
            try
            {
                var projected = projection.Map(projectionSourcesUnchanged);
                if (projected is null)
                    return new EntityCollectionResult<TEntity>(context, [], 0, null, ReadEvidenceChanged(context));
                payload = projected;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new EntityCollectionResult<TEntity>(context, [], 0, null, ProjectionFailed(context, ex));
            }
        }
        else if (emit.replaced)
        {
            if (proof is not null)
                return new EntityCollectionResult<TEntity>(context, [], 0, null, ReadEvidenceChanged(context));
            payload = emit.payload;
        }
        if (proof is not null && !proof.IsValid(context, list))
            return new EntityCollectionResult<TEntity>(context, [], 0, null, ReadEvidenceChanged(context));

        // Framework containers are built after custom hooks, so no hook can mutate an unproved
        // map entry or relationship wrapper. A terminal projection has already selected its view.
        if (!emit.replaced && includeRelationships)
        {
            try
            {
                payload = await EnrichRelationships(list, context, request.Set, relationshipEvidence);
            }
            catch (RelationshipQueryRejectedException ex)
            {
                return new EntityCollectionResult<TEntity>(context, list, total, null, RelationshipRejectedResult(ex));
            }
            catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException)
            {
                return new EntityCollectionResult<TEntity>(context, [], 0, null, new BadRequestObjectResult(new { error = ex.Message }));
            }
        }
        else if (!emit.replaced && !string.IsNullOrWhiteSpace(shape))
        {
            if (await AdmitFields(context, fields => fields.DemandShape(shape)) is { } shapeDenied)
                return new EntityCollectionResult<TEntity>(context, [], 0, null, shapeDenied);
            payload = ApplyShape(shape, list);
        }

        if (await AdmitFieldOutput(context, payload) is { } fieldOutputDenied)
            return new EntityCollectionResult<TEntity>(context, [], 0, null, fieldOutputDenied);

        ApplyViewHeader(context, request.Accept);

        CopyHookHeaders(context, hookContext);

        // SEC-0004 (§C): the per-row capability projection. Opt-in (REST ?access=true / MCP default) keeps the bare
        // array the default for existing consumers; REST wraps { items, access }, MCP reads the manifest off the
        // context. Computed after EmitCollection so `items` carries whatever the response would have been.
        if (ShouldProject(context, request.IncludeAccess, out var wrapAccess))
        {
            var manifest = await BuildAccessManifest(context, list, frozenReadFilter, proof).ConfigureAwait(false);
            if (wrapAccess) payload = AccessProjection.Wrap(payload, manifest);
        }

        if ((proof is not null && !proof.IsValid(context, list))
            || (emit.payload is EmitDecision.DeferredProjection && !projectionSourcesUnchanged())
            || relationshipEvidence.Any(check => !check()))
            return new EntityCollectionResult<TEntity>(context, [], 0, null, ReadEvidenceChanged(context));
        return new EntityCollectionResult<TEntity>(context, list, total, payload);
    }

    public async Task<EntityCollectionResult<TEntity>> Query(EntityQueryRequest request)
    {
        var context = request.Context;
        if (await Gate(context, EntityAuthorizeActions.Read).ConfigureAwait(false) is { } denied) return CollectionDenied(context, denied);
        if (await AdmitFields(context, fields => AdmitCallerQuery(fields, request.FilterJson, context.Options)) is { } fieldDenied)
            return new EntityCollectionResult<TEntity>(context, [], 0, null, fieldDenied);
        await AnnotateAccess(context).ConfigureAwait(false);
        var repo = _dataService.GetRepository<TEntity, TKey>();
        context.Capabilities = Capabilities(repo);

        var hookContext = _hookPipeline.CreateContext(context);

        if (!await _hookPipeline.BuildOptions(hookContext, context.Options))
        {
            return await CollectionShortCircuit(context, hookContext);
        }

        // WEB-0076: the body query is always paged, clamped to the endpoint's policy: a size of 0 or a page of 0 is
        // the first default page, never the whole table.
        var policy = request.Policy ?? PaginationPolicy.Resolve(context.Services, null);
        (context.Options.Page, context.Options.PageSize) = policy.Clamp(context.Options.Page, context.Options.PageSize);

        QueryDefinition query;
        try
        {
            // The body query reports X-Total-Count, so it asks for the count unless the policy opts out (IncludeCount).
            var definition = QueryDefinition.All.ForPartition(request.Set).WithSort(context.Options.Sort.ToArray())
                .WithCountStrategy(policy.IncludeCount ? CountStrategy.Optimized : null)
                .WithPagination(context.Options.Page, context.Options.PageSize);
            query = FreezeReadQuery(definition, request.FilterJson, request.IgnoreCase, context.Options);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FilterParseException or InvalidFilterFieldException or NotSupportedException)
        {
            return new EntityCollectionResult<TEntity>(context, [], 0, null, new BadRequestObjectResult(new { error = ex.Message }));
        }
        var frozenReadFilter = context.Options.Filter;
        var proof = EndpointReadProof<TEntity, TKey>.Prepare(context, query);

        if (!await _hookPipeline.BeforeCollection(hookContext, context.Options))
        {
            return await CollectionShortCircuit(context, hookContext, proof is not null);
        }
        if (proof is not null && !proof.IsRequestUnchanged(context))
            return new EntityCollectionResult<TEntity>(context, [], 0, null, ReadEvidenceChanged(context));

        RepositoryQueryResult queryResult;
        long total;
        try
        {
            queryResult = await QueryCollection(query, context.Options.Q, request.AllowRawQuery,
                policy.AbsoluteMaxRecords, context.CancellationToken);
            total = queryResult.Total;
        }
        catch (FreeTextRefusedException refused)
        {
            return new EntityCollectionResult<TEntity>(context, [], 0, null, FreeTextRefused(refused));
        }
        catch (Exception ex) when (ex is InvalidOperationException or FilterParseException or InvalidFilterFieldException or NotSupportedException)
        {
            var bad = new BadRequestObjectResult(new { error = ex.Message });
            return new EntityCollectionResult<TEntity>(context, [], 0, null, bad);
        }

        if (queryResult.ExceededSafetyLimit)
            return new EntityCollectionResult<TEntity>(context, [], queryResult.Total, null, TooLarge(policy.AbsoluteMaxRecords));

        // Sort + pagination handled by orchestrator inside QueryCollectionFromBody (DATA-0092).
        // The caller only sets headers — never paginates again, or we'd page-of-page (regression).
        var list = queryResult.Items.ToList();
        if (proof is not null && !proof.Bind(queryResult.ReadEvidence, list))
            return new EntityCollectionResult<TEntity>(context, [], 0, null, ReadEvidenceChanged(context));
        if (context.Options.PageSize > 0)
        {
            context.Headers["X-Page"] = context.Options.Page.ToString();
            context.Headers["X-Page-Size"] = context.Options.PageSize.ToString();
            if (policy.IncludeCount) context.Headers["X-Total-Count"] = total.ToString();
        }

        if (!await _hookPipeline.AfterCollection(hookContext, list))
        {
            return await CollectionShortCircuit(context, hookContext, proof is not null);
        }
        if (proof is not null && !proof.IsValid(context, list))
            return new EntityCollectionResult<TEntity>(context, [], 0, null, ReadEvidenceChanged(context));

        ApplyViewHeader(context, request.Accept);

        var projectionSourcesUnchanged = CaptureProjectionSources(context, query, list, proof);
        var emit = await _hookPipeline.EmitCollection(hookContext, list);
        if (hookContext.IsShortCircuited) return await CollectionShortCircuit(context, hookContext, proof is not null);
        object payload = emit.replaced ? emit.payload : list;
        if (emit.payload is EmitDecision.DeferredProjection projection)
        {
            if (!ProjectionKeySupported())
                return new EntityCollectionResult<TEntity>(context, [], 0, null, ProjectionKeyRejected());
            if (!IsFlatProjection(context.Options))
                return new EntityCollectionResult<TEntity>(context, [], 0, null, ProjectionShapeRejected());
            if (!projection.Matches(list) || !projectionSourcesUnchanged())
                return new EntityCollectionResult<TEntity>(context, [], 0, null, ReadEvidenceChanged(context));
            try
            {
                var projected = projection.Map(projectionSourcesUnchanged);
                if (projected is null)
                    return new EntityCollectionResult<TEntity>(context, [], 0, null, ReadEvidenceChanged(context));
                payload = projected;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new EntityCollectionResult<TEntity>(context, [], 0, null, ProjectionFailed(context, ex));
            }
        }
        else if (proof is not null && emit.replaced)
            return new EntityCollectionResult<TEntity>(context, [], 0, null, ReadEvidenceChanged(context));
        CopyHookHeaders(context, hookContext);

        // SEC-0004 (§C): the body-query path has no REST sidecar toggle (the bare body schema stays stable); the MCP
        // edge still opts in by default, so the manifest is computed + stashed for the tool-result metadata.
        if (ShouldProject(context, includeAccess: false, out _))
        {
            await BuildAccessManifest(context, list, frozenReadFilter, proof).ConfigureAwait(false);
        }

        if ((proof is not null && !proof.IsValid(context, list))
            || (emit.payload is EmitDecision.DeferredProjection && !projectionSourcesUnchanged()))
            return new EntityCollectionResult<TEntity>(context, [], 0, null, ReadEvidenceChanged(context));
        if (await AdmitFieldOutput(context, payload) is { } finalFieldOutputDenied)
            return new EntityCollectionResult<TEntity>(context, [], 0, null, finalFieldOutputDenied);
        return new EntityCollectionResult<TEntity>(context, list, total, payload);
    }

    public async Task<EntityModelResult<TEntity>> GetNew(EntityGetNewRequest request)
    {
        var context = request.Context;
        if (await Gate(context, EntityAuthorizeActions.Read).ConfigureAwait(false) is { } denied) return ModelDenied(context, denied);
        await AnnotateAccess(context).ConfigureAwait(false);
        var repo = _dataService.GetRepository<TEntity, TKey>();
        context.Capabilities = Capabilities(repo);

        var hookContext = _hookPipeline.CreateContext(context);
        if (!await _hookPipeline.BuildOptions(hookContext, context.Options))
            return await ModelShortCircuit(context, hookContext);
        if (Filter.HasCounterpart(context.Options.Filter))
            return new EntityModelResult<TEntity>(context, null, null, new BadRequestObjectResult(new
            {
                error = "A new-entity template has no persisted identity to authorize against a counterpart. Query an existing entity instead."
            }));

        var model = Activator.CreateInstance<TEntity>();
        if (!await _hookPipeline.AfterModelFetch(hookContext, model))
            return await ModelShortCircuit(context, hookContext);

        ApplyViewHeader(context, request.Accept);

        var emit = await _hookPipeline.EmitModel(hookContext, model!);
        if (hookContext.IsShortCircuited) return await ModelShortCircuit(context, hookContext);
        var payload = emit.replaced ? emit.payload : model;
        if (await AdmitFieldOutput(context, payload) is { } fieldOutputDenied)
            return new EntityModelResult<TEntity>(context, null, null, fieldOutputDenied);
        CopyHookHeaders(context, hookContext);

        return new EntityModelResult<TEntity>(context, model, payload);
    }

    public async Task<EntityModelResult<TEntity>> GetById(EntityGetByIdRequest<TKey> request)
    {
        var context = request.Context;
        if (request.With is not null)
            context.Options.IncludeRelationships = request.With.Contains("all", StringComparison.OrdinalIgnoreCase);
        if (await Gate(context, EntityAuthorizeActions.Read).ConfigureAwait(false) is { } denied) return ModelDenied(context, denied);
        await AnnotateAccess(context).ConfigureAwait(false);
        var repo = _dataService.GetRepository<TEntity, TKey>();
        context.Capabilities = Capabilities(repo);

        var hookContext = _hookPipeline.CreateContext(context);

        // The same normalized read constraint reaches keyed and collection reads.
        if (!await _hookPipeline.BuildOptions(hookContext, context.Options))
        {
            return await ModelShortCircuit(context, hookContext);
        }

        context.Options.Filter = Filter.Snapshot(context.Options.Filter);
        var frozenReadFilter = context.Options.Filter;
        using var partition = EntityContext.With(partition: request.Set);
        var query = QueryDefinition.All.ForPartition(request.Set).WithPagination(1, 1)
            .Where(Filter.And(Filter.Eq(nameof(IEntity<TKey>.Id), request.Id), frozenReadFilter));
        var proof = EndpointReadProof<TEntity, TKey>.Prepare(context, query);

        if (!await _hookPipeline.BeforeModelFetch(hookContext, request.Id?.ToString() ?? ""))
        {
            return await ModelShortCircuit(context, hookContext, proof is not null);
        }
        if (proof is not null && !proof.IsRequestUnchanged(context))
            return new EntityModelResult<TEntity>(context, null, null, ReadEvidenceChanged(context));

        TEntity? model;
        if (proof is null)
        {
            model = await Data<TEntity, TKey>.Get(request.Id!, context.CancellationToken);
        }
        else
        {
            try
            {
                var queryRepository = _dataService.GetRepository<TEntity, TKey>() as IQueryRepository<TEntity, TKey>
                    ?? throw new NotSupportedException("The selected connector does not support governed structured reads.");
                var result = await queryRepository.Query(proof.Query.WithCountStrategy(null), context.CancellationToken);
                if (!proof.Bind(result.ReadEvidence, result.Items))
                    return new EntityModelResult<TEntity>(context, null, null, ReadEvidenceChanged(context));
                model = result.Items.SingleOrDefault();
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
            {
                return new EntityModelResult<TEntity>(context, null, null, new BadRequestObjectResult(new { error = ex.Message }));
            }
        }
        if (!await _hookPipeline.AfterModelFetch(hookContext, model))
            return await ModelShortCircuit(context, hookContext, proof is not null);
        if (proof is not null && !proof.IsValid(context, model is null ? [] : [model]))
            return new EntityModelResult<TEntity>(context, null, null, ReadEvidenceChanged(context));
        if (model is null || (proof is null && !PassesRequestPredicates(model, frozenReadFilter)))
        {
            // A predicate-filtered row returns the same NotFound as a missing row so existence is not
            // revealed to a caller the visibility hook excludes.
            CopyHookHeaders(context, hookContext);
            return new EntityModelResult<TEntity>(context, null, null, new NotFoundResult());
        }

        // SEC-0004 (§C): refine the single-item Koan-Access header against THIS row — but only when a realization
        // exists, since without one the coarse header from AnnotateAccess is already exact (no Owner/Constrain to
        // bind). This is the single-item form of the per-row projection: a public-read/owner-write row fetched by a
        // non-owner advertises `read`, not `read, write` — honest about what the principal may actually do.
        if (_authorize is not null && ResolveAccessor(context) is not null)
        {
            var projector = await CreateProjector(context, frozenReadFilter, proof).ConfigureAwait(false);
            context.Headers["Koan-Access"] = string.Join(", ", projector.Can(model));
        }

        if (context.Options.IncludeRelationships && model is Entity<TEntity, TKey>)
        {
            // WEB-0068 / AN-leak: relationship expansion must govern every related entity by ITS OWN
            // type's visibility predicates — domain Relatives() is app-authority and would tunnel
            // hidden rows out through a visible parent. Already inside the partition scope above.
            RelationshipGraph<TEntity> enriched;
            try
            {
                enriched = await GovernedRelationshipExpander.ExpandAsync<TEntity, TKey>(model, request.Id!, context);
            }
            catch (RelationshipQueryRejectedException ex)
            {
                return new EntityModelResult<TEntity>(context, model, null, RelationshipRejectedResult(ex));
            }
            catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException)
            {
                return new EntityModelResult<TEntity>(context, null, null, new BadRequestObjectResult(new { error = ex.Message }));
            }
            ApplyViewHeader(context, request.Accept);
            CopyHookHeaders(context, hookContext);
            if (proof is not null && !proof.IsValid(context, [model]))
                return new EntityModelResult<TEntity>(context, null, null, ReadEvidenceChanged(context));
            return new EntityModelResult<TEntity>(context, model, enriched);
        }

        ApplyViewHeader(context, request.Accept);
        var emit = await _hookPipeline.EmitModel(hookContext, model);
        if (hookContext.IsShortCircuited) return await ModelShortCircuit(context, hookContext, proof is not null);
        if (proof is not null && emit.replaced)
            return new EntityModelResult<TEntity>(context, null, null, ReadEvidenceChanged(context));
        var payload = emit.replaced ? emit.payload : model;
        if (await AdmitFieldOutput(context, payload) is { } fieldOutputDenied)
            return new EntityModelResult<TEntity>(context, null, null, fieldOutputDenied);
        CopyHookHeaders(context, hookContext);
        if (proof is not null && !proof.IsValid(context, [model]))
            return new EntityModelResult<TEntity>(context, null, null, ReadEvidenceChanged(context));
        return new EntityModelResult<TEntity>(context, model, payload);
    }

    public async Task<EntityModelResult<TEntity>> Upsert(EntityUpsertRequest<TEntity, TKey> request)
    {
        var context = request.Context;
        if (await Gate(context, EntityAuthorizeActions.Write).ConfigureAwait(false) is { } denied) return ModelDenied(context, denied);
        if (await AdmitFields(context, fields => fields.DemandReplacement()) is { } fieldDenied)
            return new EntityModelResult<TEntity>(context, null, null, fieldDenied);
        await AnnotateAccess(context).ConfigureAwait(false);
        using var _ = EntityContext.With(partition: string.IsNullOrWhiteSpace(request.Set) ? null : request.Set);
        var repo = _dataService.GetRepository<TEntity, TKey>();
        context.Capabilities = Capabilities(repo);

        var hookContext = _hookPipeline.CreateContext(context);

        // WEB-0073: route-id authority. A verb that pins the id to the route (PUT) delivers it via
        // RouteId; the model carries it before the create-vs-update split so replace-by-id cannot
        // drift into create. Applied only onto a default id - a disagreeing body id is rejected by
        // the controller before the request is built.
        if (request.RouteId is not null && !EqualityComparer<TKey>.Default.Equals(request.RouteId, default!)
            && EqualityComparer<TKey>.Default.Equals(request.Model.Id, default!))
        {
            if (typeof(TEntity).GetProperty(nameof(IEntity<TKey>.Id), System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.SetProperty) is not { } idProperty || !idProperty.CanWrite)
            {
                throw new InvalidOperationException($"Entity type '{typeof(TEntity).Name}' does not expose a writable id; a route-pinned write (PUT) is not supported for it.");
            }
            idProperty.SetValue(request.Model, request.RouteId);
        }

        var (saved, answer) = await Write(context, "upsert", async () =>
        {
            // The prior row, read where the hold opens: the hooks read it as Prior, the delta projects from it, and a
            // constrained entity splits create from update on it.
            var mutation = MutationConstraints(context);
            var id = request.Model.Id;
            var before = id is not null && !EqualityComparer<TKey>.Default.Equals(id, default!)
                ? await Data<TEntity, TKey>.Get(id, context.CancellationToken)
                : null;
            Prior(context, before, before is null ? "create" : "update");
            if (mutation.Create is not null)
            {
                // create (no existing row) → STAMP the owner onto the payload — server-truth that overwrites a forged
                // owner (a Where on create is a silent no-op that lets it through). update → the existing row must be
                // in scope (404 if not, existence-hiding, matching the read rail) then apply the update stamp
                // (freeze ownership by default).
                if (before is null)
                {
                    mutation.Create.ApplyStamps(request.Model);
                }
                else
                {
                    var constrain = mutation.Update!;
                    if (!PassesRequestPredicates(before, constrain.Filter))
                        return Ended(new EntityModelResult<TEntity>(context, null, null, new NotFoundResult()));
                    constrain.ApplyStamps(request.Model);
                }
            }

            var requiresInsert = mutation.Create is not null && before is null;
            if (requiresInsert && (repo is not IInsertOnlyRepository<TEntity, TKey> ||
                !DataCaps.Describe(repo, repo.GetType().Name).Has(DataCaps.Write.InsertOnly)))
                return Ended(new EntityModelResult<TEntity>(context, null, null, InsertUnsupported()));

            if (!await _hookPipeline.BeforeSave(hookContext, request.Model))
                return await Stopped(context, hookContext);

            if (request.DryRun)
            {
                // Rehearsal: the full hook/validation pipeline ran; the adapter write does NOT, and AfterSave is
                // not raised (no save happened). The "after" face is the would-be model.
                context.Items[EntityMutationProbe.DryRunKey] = true;
                ApplyViewHeader(context, request.Accept);
                CopyHookHeaders(context, hookContext);
                return Ended(new EntityModelResult<TEntity>(context, request.Model, request.Model));
            }

            TEntity written;
            if (requiresInsert)
            {
                var inserted = await ((IInsertOnlyRepository<TEntity, TKey>)repo).Insert(request.Model, context.CancellationToken);
                if (inserted.Outcome == MutationOutcome.Conflict)
                    return Ended(new EntityModelResult<TEntity>(context, null, null, await InsertConflict(context, request.Model.Id)));
                written = inserted.Entity!;
            }
            else written = await request.Model.Upsert<TEntity, TKey>(context.CancellationToken);
            await AuditMutation(context, EntityAuthorizeActions.Write, written.Id?.ToString() ?? "").ConfigureAwait(false);

            if (!await _hookPipeline.AfterSave(hookContext, written))
                return await Stopped(context, hookContext);
            return Saved(written);
        });
        return answer ?? await Emitted(context, hookContext, saved!, request.Accept);
    }

    public async Task<EntityEndpointResult> UpsertMany(EntityUpsertManyRequest<TEntity> request)
    {
        var context = request.Context;
        if (await Gate(context, EntityAuthorizeActions.Write).ConfigureAwait(false) is { } denied) return Denied(context, denied);
        if (await AdmitFields(context, fields => fields.DemandReplacement()) is { } fieldDenied)
            return new EntityEndpointResult(context, null, fieldDenied);
        await AnnotateAccess(context).ConfigureAwait(false);
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

        // One partition scope spans the constraint probe, BeforeSave hooks, and the write (mirrors single-item Upsert).
        using var _ = EntityContext.With(partition: string.IsNullOrWhiteSpace(request.Set) ? null : request.Set);

        return await WriteBatch(context, "upsertMany", async () =>
        {
            var mutation = MutationConstraints(context);
            if (mutation.Create is not null)
            {
                // Preflight the entire request. A null visible read does not prove absence; mixed
                // atomic insertion/update is not yet a provider contract, so such batches fail closed.
                var containsCreate = false;
                foreach (var model in list)
                {
                    var id = model.Id;
                    var before = id is not null && !EqualityComparer<TKey>.Default.Equals(id, default!)
                        ? await Data<TEntity, TKey>.Get(id, context.CancellationToken)
                        : null;
                    if (before is null)
                    {
                        containsCreate = true;
                        mutation.Create.ApplyStamps(model);
                    }
                    else
                    {
                        var constrain = mutation.Update!;
                        if (!PassesRequestPredicates(before, constrain.Filter))
                            return Ended(new EntityEndpointResult(context, null, new NotFoundResult()));
                        constrain.ApplyStamps(model);
                    }
                }
                if (containsCreate)
                    return Ended(new EntityEndpointResult(context, null, new ObjectResult(new
                    {
                        code = KoanWebConstants.Codes.Mutation.BulkCreateUnsupported,
                        message = "Constrained bulk requests containing new or unavailable identities require atomic mixed writes. Submit creates individually."
                    }) { StatusCode = StatusCodes.Status501NotImplemented }));
            }

            foreach (var model in list)
            {
                if (!await _hookPipeline.BeforeSave(hookContext, model))
                    return await StoppedBatch(context, hookContext);
            }

            if (request.DryRun)
            {
                // AN11: batch rehearsal — validation ran, no write. Batch mutations carry a count-level delta
                // (the affected count), not a per-field diff.
                context.Items[EntityMutationProbe.DryRunKey] = true;
                context.Items[EntityMutationProbe.OperationKey] = "upsertMany";
                context.Items[EntityMutationProbe.AffectedCountKey] = list.Count;
                CopyHookHeaders(context, hookContext);
                return Ended(new EntityEndpointResult(context, new { wouldUpsert = list.Count }));
            }

            var upserted = await Data<TEntity, TKey>.UpsertMany(list, context.CancellationToken);
            await AuditMutation(context, EntityAuthorizeActions.Write, "").ConfigureAwait(false);

            foreach (var model in list)
            {
                if (!await _hookPipeline.AfterSave(hookContext, model))
                    return await StoppedBatch(context, hookContext);
            }

            context.Headers["Koan-Write-Capabilities"] = WriteCapabilitiesHeader(repo);
            CopyHookHeaders(context, hookContext);
            return Stands(new EntityEndpointResult(context, new { upserted }));
        });
    }

    public async Task<EntityModelResult<TEntity>> Delete(EntityDeleteRequest<TKey> request)
    {
        var context = request.Context;
        if (await Gate(context, EntityAuthorizeActions.Remove).ConfigureAwait(false) is { } denied) return ModelDenied(context, denied);
        await AnnotateAccess(context).ConfigureAwait(false);
        var repo = _dataService.GetRepository<TEntity, TKey>();
        context.Capabilities = Capabilities(repo);

        var hookContext = _hookPipeline.CreateContext(context);
        var accessor = ResolveAccessor(context);
        var bound = accessor is null ? null : DeleteBound(accessor);

        using var _ = EntityContext.With(partition: string.IsNullOrWhiteSpace(request.Set) ? null : request.Set);
        var (deleted, answer) = await Write(context, "delete", async () =>
        {
            // SEC-0004: a row outside the principal's scope is a 404, never deleted (existence-hiding, matches reads).
            var model = (await RowsInBound([request.Id], bound, context.CancellationToken)).SingleOrDefault();
            if (model is null)
                return Ended(new EntityModelResult<TEntity>(context, null, null, new NotFoundResult()));
            Prior(context, model, "delete");

            if (!await _hookPipeline.BeforeDelete(hookContext, model))
                return await Stopped(context, hookContext);

            if (request.DryRun)
            {
                // Rehearsal: BeforeDelete ran; the row is NOT removed and AfterDelete is not raised.
                context.Items[EntityMutationProbe.DryRunKey] = true;
                ApplyViewHeader(context, request.Accept);
                CopyHookHeaders(context, hookContext);
                return Ended(new EntityModelResult<TEntity>(context, model, model));
            }

            if (!await DeleteWithin(request.Id, bound, context.CancellationToken))
                return Ended(new EntityModelResult<TEntity>(context, null, null, new NotFoundResult()));
            await AuditMutation(context, EntityAuthorizeActions.Remove, request.Id?.ToString() ?? "").ConfigureAwait(false);
            if (!await _hookPipeline.AfterDelete(hookContext, model))
                return await Stopped(context, hookContext);
            return Saved(model);
        });
        return answer ?? await Emitted(context, hookContext, deleted!, request.Accept);
    }

    public async Task<EntityEndpointResult> DeleteMany(EntityDeleteManyRequest<TKey> request)
    {
        var context = request.Context;
        if (await Gate(context, EntityAuthorizeActions.Remove).ConfigureAwait(false) is { } denied) return Denied(context, denied);
        await AnnotateAccess(context).ConfigureAwait(false);
        var repo = _dataService.GetRepository<TEntity, TKey>();
        context.Headers["Koan-Write-Capabilities"] = WriteCapabilitiesHeader(repo);

        using var _ = EntityContext.With(partition: string.IsNullOrWhiteSpace(request.Set) ? null : request.Set);
        var accessor = ResolveAccessor(context);
        var bound = accessor is null ? null : DeleteBound(accessor);
        var hooked = RaisesDeleteHooks;
        var hookContext = _hookPipeline.CreateContext(context);
        return await WriteBatch(context, "deleteMany", async () =>
        {
            IReadOnlyCollection<TKey> targets = request.Ids ?? [];
            var rows = new List<TEntity>();
            if ((accessor is not null || hooked) && targets.Count > 0)
            {
                // SEC-0004: trust no id — a row must be in scope to be deleted (out-of-scope ids are silently skipped,
                // the same hidden-row semantics as a single delete). This bounding runs BEFORE the dry-run report so a
                // rehearsal cannot leak the existence of out-of-scope ids. The loaded rows feed the delete hooks.
                rows = await RowsInBound(targets, bound, context.CancellationToken);
                targets = rows.Select(row => row.Id).ToList();
            }

            if (hooked && !await BeforeDeleteAll(hookContext, rows))
                return await StoppedBatch(context, hookContext);

            if (request.DryRun)
            {
                context.Items[EntityMutationProbe.DryRunKey] = true;
                context.Items[EntityMutationProbe.OperationKey] = "deleteMany";
                context.Items[EntityMutationProbe.AffectedCountKey] = targets.Count;
                return Ended(new EntityEndpointResult(context, new { wouldDelete = targets.Count }));
            }

            var deleted = await DeleteManyWithin(targets, bound, context.CancellationToken);
            await AuditMutation(context, EntityAuthorizeActions.Remove, "").ConfigureAwait(false);
            if (hooked && !await AfterDeleteAll(hookContext, rows))
                return await StoppedBatch(context, hookContext);
            CopyHookHeaders(context, hookContext);
            return Stands(new EntityEndpointResult(context, new { deleted }));
        });
    }

    public async Task<EntityEndpointResult> DeleteByQuery(EntityDeleteByQueryRequest request)
    {
        if (await Gate(request.Context, EntityAuthorizeActions.Remove).ConfigureAwait(false) is { } denied) return Denied(request.Context, denied);
        await AnnotateAccess(request.Context).ConfigureAwait(false);
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

        if (await AdmitFields(request.Context, fields => fields.DemandFilter(filter)) is { } fieldDenied)
            return new EntityEndpointResult(request.Context, null, fieldDenied);

        var accessor = ResolveAccessor(request.Context);
        var bound = accessor is null ? null : DeleteBound(accessor);
        if (bound is not null)
        {
            // SEC-0004: a mass delete cannot exceed the principal's rows — AND the Constrain predicate into the
            // user's filter before it ever reaches the adapter (agent-safety).
            filter = Filter.And(filter, bound) ?? filter;
        }

        var hooked = RaisesDeleteHooks;
        var hookContext = _hookPipeline.CreateContext(request.Context);
        return await WriteBatch(request.Context, "deleteByQuery", async () =>
        {
            var items = await Data<TEntity, TKey>.All(QueryDefinition.All.Where(filter), request.Context.CancellationToken);
            var ids = items.Select(e => e.Id).ToList();
            if (hooked && !await BeforeDeleteAll(hookContext, items))
                return await StoppedBatch(request.Context, hookContext);

            if (request.DryRun)
            {
                // AN11: the query already ran, so the rehearsal reports an EXACT affected count for free.
                request.Context.Items[EntityMutationProbe.DryRunKey] = true;
                request.Context.Items[EntityMutationProbe.OperationKey] = "deleteByQuery";
                request.Context.Items[EntityMutationProbe.AffectedCountKey] = ids.Count;
                return Ended(new EntityEndpointResult(request.Context, new { wouldDelete = ids.Count }));
            }

            if (ids.Count == 0) return Ended(new EntityEndpointResult(request.Context, new { deleted = 0 }));
            var removedByPredicate = await DeleteManyWithin(ids, bound, request.Context.CancellationToken);
            await AuditMutation(request.Context, EntityAuthorizeActions.Remove, "").ConfigureAwait(false);
            if (hooked && !await AfterDeleteAll(hookContext, items))
                return await StoppedBatch(request.Context, hookContext);
            CopyHookHeaders(request.Context, hookContext);
            return Stands(new EntityEndpointResult(request.Context, new { deleted = removedByPredicate }));
        });
    }

    public async Task<EntityEndpointResult> DeleteAll(EntityDeleteAllRequest request)
    {
        if (await Gate(request.Context, EntityAuthorizeActions.Remove).ConfigureAwait(false) is { } denied) return Denied(request.Context, denied);
        await AnnotateAccess(request.Context).ConfigureAwait(false);
        using var _ = EntityContext.With(partition: string.IsNullOrWhiteSpace(request.Set) ? null : request.Set);

        var accessor = ResolveAccessor(request.Context);
        // SEC-0004: a row-scoped entity must NOT truncate the table — bound like every delete (SEC-0013). WEB-0077: an
        // entity whose delete hooks may run loads its rows too, so each one passes BeforeDelete and AfterDelete. Only
        // an entity with neither keeps RemoveAll.
        var bound = accessor is null ? null : DeleteBound(accessor);
        var hooked = RaisesDeleteHooks;
        if (bound is null && !hooked && request.DryRun)
        {
            // Unconstrained: name the effect rather than scan the whole set for a count — honest A10 posture.
            request.Context.Items[EntityMutationProbe.DryRunKey] = true;
            request.Context.Items[EntityMutationProbe.OperationKey] = "deleteAll";
            return new EntityEndpointResult(request.Context, new { wouldDeleteAll = true });
        }

        var hookContext = _hookPipeline.CreateContext(request.Context);
        return await WriteBatch(request.Context, "deleteAll", async () =>
        {
            if (bound is null && !hooked)
            {
                var deleted = await Entity<TEntity, TKey>.RemoveAll(request.Context.CancellationToken);
                await AuditMutation(request.Context, EntityAuthorizeActions.Remove, "").ConfigureAwait(false);
                return Stands(new EntityEndpointResult(request.Context, new { deleted }));
            }

            var rows = await Data<TEntity, TKey>.All(QueryDefinition.All.Where(bound), request.Context.CancellationToken);
            var ids = rows.Select(e => e.Id).ToList();
            if (hooked && !await BeforeDeleteAll(hookContext, rows))
                return await StoppedBatch(request.Context, hookContext);
            if (request.DryRun)
            {
                request.Context.Items[EntityMutationProbe.DryRunKey] = true;
                request.Context.Items[EntityMutationProbe.OperationKey] = "deleteAll";
                request.Context.Items[EntityMutationProbe.AffectedCountKey] = ids.Count;
                return Ended(new EntityEndpointResult(request.Context, new { wouldDelete = ids.Count }));
            }
            var deletedBounded = ids.Count == 0 ? 0 : await DeleteManyWithin(ids, bound, request.Context.CancellationToken);
            await AuditMutation(request.Context, EntityAuthorizeActions.Remove, "").ConfigureAwait(false);
            if (hooked && !await AfterDeleteAll(hookContext, rows))
                return await StoppedBatch(request.Context, hookContext);
            CopyHookHeaders(request.Context, hookContext);
            return Stands(new EntityEndpointResult(request.Context, new { deleted = deletedBounded }));
        });
    }

    public async Task<EntityModelResult<TEntity>> Patch(EntityPatchRequest<TEntity, TKey> request)
    {
        var context = request.Context;
        if (await Gate(context, EntityAuthorizeActions.Write).ConfigureAwait(false) is { } denied) return ModelDenied(context, denied);
        if (await AdmitFields(context, fields =>
        {
            if (request.Patch is JsonPatchDocument<TEntity> document)
                fields.DemandPatch(document.Operations.Select(operation => new PatchOp(operation.op, operation.path, operation.from, null)));
            else if (request.Patch is Newtonsoft.Json.Linq.JToken token)
                fields.DemandReplacement();
            else throw new NotSupportedException("Field access requires a supported typed patch document.");
        }) is { } fieldDenied) return new EntityModelResult<TEntity>(context, null, null, fieldDenied);
        await AnnotateAccess(context).ConfigureAwait(false);
        var repo = _dataService.GetRepository<TEntity, TKey>();
        context.Capabilities = Capabilities(repo);

        var hookContext = _hookPipeline.CreateContext(context);
        var accessor = ResolveAccessor(context);
        var constrain = accessor is null ? null : ConstrainFor(accessor, AccessAction.Update);

        // The patch is judged before the hold opens, so an application weighs costly input with nothing held.
        try
        {
            if (!await _hookPipeline.BeforePatch(hookContext, request.Id?.ToString() ?? "", request.Patch!))
                return await ModelShortCircuit(context, hookContext);
        }
        catch (Exception refusal) when (refusal is Refusal or EntityLifecycleCancelledException)
        {
            return new EntityModelResult<TEntity>(context, null, null, Refusing(context, refusal));
        }

        using var _ = EntityContext.With(partition: string.IsNullOrWhiteSpace(request.Set) ? null : request.Set);
        var (saved, answer) = await Write(context, "patch", async () =>
        {
            var original = await Data<TEntity, TKey>.Get(request.Id!, context.CancellationToken);
            if (original is null)
                return Ended(new EntityModelResult<TEntity>(context, null, null, new NotFoundResult()));

            // SEC-0004: the existing row must be in scope (404 if not, existence-hiding); the same filter freezes
            // ownership on the patched copy below.
            if (constrain is not null && !PassesRequestPredicates(original, constrain.Filter))
                return Ended(new EntityModelResult<TEntity>(context, null, null, new NotFoundResult()));

            var working = await Data<TEntity, TKey>.Get(request.Id!, context.CancellationToken);
            if (working is null)
                return Ended(new EntityModelResult<TEntity>(context, null, null, new NotFoundResult()));

            // Apply generalized patch
            if (request.Patch is Microsoft.AspNetCore.JsonPatch.JsonPatchDocument<TEntity> jp)
            {
                jp.ApplyTo(working);
            }
            else if (request.Patch is Newtonsoft.Json.Linq.JToken jt)
            {
                var opts = context.HttpContext?.RequestServices.GetService(typeof(Microsoft.Extensions.Options.IOptions<Koan.Web.Options.KoanWebOptions>)) as Microsoft.Extensions.Options.IOptions<Koan.Web.Options.KoanWebOptions>;
                var mergePolicy = request.Options?.MergeNulls
                                  ?? opts?.Value.MergePatchNullsForNonNullable
                                  ?? MergePatchNullPolicy.SetDefault;
                var partialPolicy = request.Options?.PartialNulls
                                    ?? opts?.Value.PartialJsonNulls
                                    ?? PartialJsonNullPolicy.SetNull;
                // AE-16: the applicator restores a typed working copy (additive collections, private stored
                // state, siblings and family shape survive); refusal, malformed intent, or a non-convertible
                // value throws here, before stamps, BeforeSave and any save (BeforePatch has already run),
                // and becomes a corrective 422.
                try
                {
                    working = request.Kind == EntityPatchKind.MergePatch7386
                        ? new Koan.Data.Core.Patch.MergePatchApplicator<TEntity>(jt, mergePolicy).ApplyToCopy(working)
                        : new Koan.Data.Core.Patch.PartialJsonApplicator<TEntity>(jt, partialPolicy).ApplyToCopy(working);
                }
                catch (Exception ex) when (ex is Newtonsoft.Json.JsonException
                                               or System.IO.InvalidDataException
                                               or InvalidOperationException
                                               or ArgumentException)
                {
                    return Ended(new EntityModelResult<TEntity>(context, null, null,
                        new UnprocessableEntityObjectResult(new { error = ex.Message })));
                }
            }
            else
            {
                return Ended(new EntityModelResult<TEntity>(context, null, null, new BadRequestObjectResult(new { error = "Unsupported patch payload" })));
            }
            var idProp = typeof(TEntity).GetProperty("Id");
            if (idProp is not null)
            {
                var newId = idProp.GetValue(working);
                if (newId is not null && !Equals(newId, request.Id))
                    return Ended(new EntityModelResult<TEntity>(context, null, null, new ConflictResult()));
            }

            constrain?.ApplyStamps(working!); // freeze ownership (re-stamp owner to principal) before save

            // The pre-patch row (`original`) is already loaded, so the hooks' Prior and the delta cost nothing extra.
            Prior(context, original, "update");
            if (!await _hookPipeline.BeforeSave(hookContext, working!))
                return await Stopped(context, hookContext);

            if (request.DryRun)
            {
                // Rehearsal: the patch is applied + validated against the working copy; nothing is saved.
                context.Items[EntityMutationProbe.DryRunKey] = true;
                ApplyViewHeader(context, request.Accept);
                CopyHookHeaders(context, hookContext);
                return Ended(new EntityModelResult<TEntity>(context, working, working));
            }

            var written = await working!.Upsert<TEntity, TKey>(context.CancellationToken);
            await AuditMutation(context, EntityAuthorizeActions.Write, request.Id?.ToString() ?? "").ConfigureAwait(false);
            if (!await _hookPipeline.AfterPatch(hookContext, written))
                return await Stopped(context, hookContext);
            return Saved(written);
        });
        return answer ?? await Emitted(context, hookContext, saved!, request.Accept);
    }

    private static bool PassesRequestPredicates(TEntity model, Filter? filter)
    {
        Filter.RequireRowOnly(filter, "Row ownership or visibility evaluation");
        return filter is null || InMemoryFilterEvaluator.Compile<TEntity>(filter)(model);
    }

    private static Func<bool> CaptureProjectionSources(EntityRequestContext context, QueryDefinition query,
        IReadOnlyList<TEntity> rows, EndpointReadProof<TEntity, TKey>? proof)
    {
        if (proof is not null) return () => proof.IsValid(context, rows);
        var selected = rows.ToArray();
        var identities = rows.Select(row => row.Id).ToArray();
        var request = EndpointReadProof<TEntity, TKey>.CaptureRequest(context, query);
        return () => request.IsRequestUnchanged(context)
            && rows.Count == selected.Length
            && selected.Select((row, index) => ReferenceEquals(row, rows[index])
                && EqualityComparer<TKey>.Default.Equals(identities[index], row.Id)).All(same => same);
    }

    private static bool IsFlatProjection(QueryOptions options)
        => !options.IncludeRelationships && (string.IsNullOrWhiteSpace(options.Shape)
            || string.Equals(options.Shape, "full", StringComparison.OrdinalIgnoreCase));

    private static BadRequestObjectResult ProjectionShapeRejected() => new(new
    {
        error = "Source-bound projection supports flat collections. Remove map, dict, or relationship shaping from this request."
    });

    private static bool ProjectionKeySupported()
        => typeof(TKey) == typeof(string) || typeof(TKey) == typeof(Guid)
            || typeof(TKey) == typeof(byte) || typeof(TKey) == typeof(sbyte)
            || typeof(TKey) == typeof(short) || typeof(TKey) == typeof(ushort)
            || typeof(TKey) == typeof(int) || typeof(TKey) == typeof(uint)
            || typeof(TKey) == typeof(long) || typeof(TKey) == typeof(ulong);

    private static BadRequestObjectResult ProjectionKeyRejected() => new(new
    {
        error = "Source-bound projection requires an immutable string, Guid, or integral identity. This key type is not supported."
    });

    private ObjectResult ProjectionFailed(EntityRequestContext context, Exception exception)
    {
        context.Items.Remove(AccessProjection.ManifestKey);
        context.Headers.Clear();
        _logger?.LogError(exception, "The collection projection for {Entity} failed before response emission.", typeof(TEntity).Name);
        return new ObjectResult(new
        {
            code = KoanWebConstants.Codes.Read.ProjectionFailed,
            error = "Source-bound projection failed before response emission. No partial view was returned."
        }) { StatusCode = StatusCodes.Status500InternalServerError };
    }

    private static BadRequestObjectResult ReadEvidenceChanged(EntityRequestContext context)
    {
        context.Items.Remove(AccessProjection.ManifestKey);
        context.Headers.Clear();
        return new(new
        {
            code = KoanWebConstants.Codes.Read.EvidenceChanged,
            message = "The returned identities or their authorization scope changed after the query. Preserve queried objects and scope in read hooks, or issue a new governed read."
        });
    }

    private static ObjectResult InsertUnsupported() => new(new
    {
        code = KoanWebConstants.Codes.Mutation.InsertUnsupported,
        message = "This connector or identity shape cannot guarantee atomic insertion. Select a connector and identity shape that support insert-only writes."
    }) { StatusCode = StatusCodes.Status501NotImplemented };

    // WEB-0086: an insert that met a row it could not see first. A row the caller may read answers 409 id-taken, as a race with another
    // create does; only a row their Read constraint hides stays existence-hiding, as a read of it is. Readability is the record access
    // a read applies, judged as RowsInBound judges a bound, so a counterpart is answered by the store; field access and row projection
    // shape what a read shows, never whether the row exists.
    private static async Task<IActionResult> InsertConflict(EntityRequestContext context, TKey id)
    {
        var read = ResolveAccessor(context) is { } accessor ? ConstrainFor(accessor, AccessAction.Read).Filter : null;
        return (await RowsInBound([id], read, context.CancellationToken)).Count > 0
            ? Answers.Refused(context.User, Reason.Conflict(KoanWebConstants.Codes.Mutation.IdTaken, "Something already has this id, so nothing was written."))
            : new NotFoundResult();
    }

    /// <summary>The row a single write found where its hold opens, for its hooks (<see cref="HookContext{TEntity}.Prior"/>) and its delta.</summary>
    private static void Prior(EntityRequestContext context, TEntity? prior, string operation)
    {
        context.Items[EntityMutationProbe.BeforeKey] = prior;
        context.Items[EntityMutationProbe.OperationKey] = operation;
    }

    /// <summary>What part of a write ends with, and whether what it did stands: committed where it holds, kept where it cannot.</summary>
    private readonly record struct Step<TResult>(TResult Result, bool Stands);

    /// <summary>A single write's end: the row it wrote, which the emit composes, or its whole answer.</summary>
    private readonly record struct Written(TEntity? Row, EntityModelResult<TEntity>? Answer);

    private static Step<TResult> Stands<TResult>(TResult result) => new(result, true);

    private static Step<TResult> Ended<TResult>(TResult result) => new(result, false);

    private static Step<Written> Saved(TEntity row) => Stands(new Written(row, null));

    private static Step<Written> Ended(EntityModelResult<TEntity> answer) => Ended(new Written(default, answer));

    // A hook that stops a single write with a row answers it as written: the emit composes that row as it would a saved one, so a replay
    // answers as its first write did. Any other stop is its own answer, and one of 400 or more leaves nothing where the write holds.
    private static async Task<Step<Written>> Stopped(EntityRequestContext context, HookContext<TEntity> hookContext)
    {
        if (hookContext.ShortCircuitPayload is TEntity row)
        {
            hookContext.Resume();
            return Saved(row);
        }

        var answer = await ModelShortCircuit(context, hookContext);
        return new(new Written(default, answer), !IsDenial(answer.ShortCircuitPayload));
    }

    private static async Task<Step<EntityEndpointResult>> StoppedBatch(EntityRequestContext context, HookContext<TEntity> hookContext)
    {
        var answer = await ModelShortCircuit(context, hookContext);
        return new(answer, !IsDenial(answer.ShortCircuitPayload));
    }

    private static async Task<Written> Write(EntityRequestContext context, string operation, Func<Task<Step<Written>>> write)
        => await Hold(context, operation, write, refused => new Written(default, new EntityModelResult<TEntity>(context, null, null, refused)));

    private static Task<EntityEndpointResult> WriteBatch(EntityRequestContext context, string operation, Func<Task<Step<EntityEndpointResult>>> write)
        => Hold(context, operation, write, refused => new EntityEndpointResult(context, null, refused));

    // WEB-0086: a write runs from its prior read to its last after-hook as one. Inside a scope already open it joins, and that scope's owner
    // commits it, rolls it back and hears whatever refuses. Otherwise, where the entity's provider declares write.heldTransaction, it is a
    // hold of its own on the route the entity is stored on, which commits when what it did stands and rolls back on a stop of 400 or more,
    // a refusal or any throw; elsewhere it runs as it does without one. A thrown Refusal, and a lifecycle handler's cancellation, answer as a command's refusal does.
    private static async Task<TResult> Hold<TResult>(EntityRequestContext context, string operation, Func<Task<Step<TResult>>> write,
        Func<IActionResult, TResult> refused)
    {
        if (EntityContext.CurrentTransaction is not null) return (await write().ConfigureAwait(false)).Result;
        try
        {
            if (Data<TEntity, TKey>.Hold($"{typeof(TEntity).Name}.{operation}", context.CancellationToken) is not { } hold)
                return (await write().ConfigureAwait(false)).Result;
            using (await hold)
            {
                var step = await write().ConfigureAwait(false);
                if (step.Stands) await EntityContext.Commit(CancellationToken.None).ConfigureAwait(false);
                else await EntityContext.Rollback(CancellationToken.None).ConfigureAwait(false);
                return step.Result;
            }
        }
        catch (Exception refusal) when (refusal is Refusal or EntityLifecycleCancelledException)
        {
            return refused(Refusing(context, refusal));
        }
    }

    private static IActionResult Refusing(EntityRequestContext context, Exception refusal) => refusal switch
    {
        Refusal refused => Answers.Refused(context.User, refused.Reason),
        EntityLifecycleCancelledException cancelled => Answers.Refused(context.User, cancelled),
        _ => throw new ArgumentOutOfRangeException(nameof(refusal), refusal, "Only a refusal or a lifecycle cancellation is answered."),
    };

    // A written row's answer, composed after the write committed: the emit hooks read and take no lock.
    private async Task<EntityModelResult<TEntity>> Emitted(EntityRequestContext context, HookContext<TEntity> hookContext, TEntity row, string? accept)
    {
        ApplyViewHeader(context, accept);
        var emit = await _hookPipeline.EmitModel(hookContext, row);
        if (hookContext.IsShortCircuited) return await ModelShortCircuit(context, hookContext);
        var payload = emit.replaced ? emit.payload : row;
        if (await AdmitFieldOutput(context, payload) is { } fieldOutputDenied)
            return new EntityModelResult<TEntity>(context, null, null, fieldOutputDenied);
        CopyHookHeaders(context, hookContext);
        return new EntityModelResult<TEntity>(context, row, payload);
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
        public RepositoryQueryResult(IReadOnlyList<TEntity> items, long total, bool handled, bool exceededLimit,
            IQueryReadEvidence? readEvidence = null)
        {
            Items = items;
            Total = total;
            RepositoryHandledPagination = handled;
            ExceededSafetyLimit = exceededLimit;
            ReadEvidence = readEvidence;
        }

        public IReadOnlyList<TEntity> Items { get; }
        public long Total { get; }
        public bool RepositoryHandledPagination { get; }
        public bool ExceededSafetyLimit { get; }
        public IQueryReadEvidence? ReadEvidence { get; }
    }

    private static QueryDefinition FreezeReadQuery(QueryDefinition query, string? filterJson,
        bool ignoreCase, QueryOptions options)
    {
        options.Filter = Filter.Snapshot(options.Filter);
        var userFilter = string.IsNullOrWhiteSpace(filterJson) ? null
            : JsonFilterParser.Parse<TEntity>(filterJson, new FilterParseOptions { IgnoreCase = ignoreCase });
        return query with
        {
            Filter = Filter.Snapshot(Filter.And(userFilter, options.Filter)),
            Sort = query.Sort.ToArray()
        };
    }

    // WEB-0074: what a refused q tells the caller. Both name the structured way to say the same thing.
    private const string FreeTextUnsupportedMessage =
        "q is not accepted on this endpoint: request text is never run as a provider query. Narrow the collection with " +
        "filter=<URL-encoded JSON> (for example filter={\"title\":{\"$contains\":\"milk\"}}) or POST the filter to /query. " +
        "To give q a meaning, convert it into a filter in an IRequestOptionsHook<TEntity> and clear Options.Q.";

    private const string FreeTextNotComposableMessage =
        "q is a provider-native query on this endpoint and cannot be combined with filter or a server-side constraint. " +
        "Send the narrowing in filter and drop q.";

    private sealed class FreeTextRefusedException(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }

    private static ObjectResult TooLarge(int limit) => new(new
    {
        error = "Result too large",
        message = $"This endpoint allows at most {limit} records without pagination."
    }) { StatusCode = StatusCodes.Status413PayloadTooLarge };

    private static BadRequestObjectResult FreeTextRefused(FreeTextRefusedException refused)
        => new(new { code = refused.Code, message = refused.Message });

    private async Task<RepositoryQueryResult> QueryCollection(QueryDefinition query, string? q, bool allowRawQuery,
        int absoluteMaxRecords, CancellationToken cancellationToken)
    {
        using var partition = EntityContext.With(partition: query.Partition);
        if (!string.IsNullOrWhiteSpace(q))
        {
            // WEB-0074 / DATA-0096 section 7.5: request text becomes a provider query only on an endpoint that declared
            // it so, and then only alone - it cannot compose with the normalized filter (the caller's, or a server-side
            // constraint), so it is refused rather than silently dropped. A hook that gave q a meaning has already
            // turned it into a filter and cleared it by the time this runs.
            if (!allowRawQuery)
            {
                _logger?.LogInformation(
                    "EntityEndpointService<{Entity}> refused q: this endpoint does not run request text as a provider query.",
                    typeof(TEntity).Name);
                throw new FreeTextRefusedException(KoanWebConstants.Codes.Read.FreeTextUnsupported, FreeTextUnsupportedMessage);
            }
            if (query.Filter is not null)
                throw new FreeTextRefusedException(KoanWebConstants.Codes.Read.FreeTextNotComposable, FreeTextNotComposableMessage);

            // NK-296: the adapter applies the shaping's page itself and says so; the endpoint must not page that page
            // again. The total is the raw predicate's count, not the page's length.
            var repository = Data<TEntity, TKey>.As<IRawQueryRepository<TEntity, TKey>>()
                ?? throw new NotSupportedException(
                    $"The adapter backing {typeof(TEntity).Name} does not support raw provider queries. " +
                    "Use a LINQ predicate or the JSON filter DSL instead.");
            var raw = await repository.QueryRaw(q, null, query, cancellationToken);
            long total = raw.Items.Count;
            if (raw.PaginationHandled && query.CountStrategy is not null)
                total = (await repository.CountRaw(q, null, cancellationToken)).Value;
            return new RepositoryQueryResult(raw.Items, total, raw.PaginationHandled, false);
        }

        int? safetyLimit = absoluteMaxRecords > 0 ? absoluteMaxRecords : null;
        var result = query.CountStrategy is null
            ? await Data<TEntity, TKey>.QueryWithoutCount(query, cancellationToken, safetyLimit)
            : await Data<TEntity, TKey>.QueryWithCount(query, cancellationToken, safetyLimit);
        return new RepositoryQueryResult(result.Items, result.TotalCount, result.RepositoryHandledPagination,
            result.ExceededSafetyLimit, result.ReadEvidence);
    }

    private static QueryDefinition BuildQueryDefinition(EntityCollectionRequest request, QueryOptions options)
    {
        var query = QueryDefinition.All
            .ForPartition(request.Set)
            .WithSort(options.Sort.ToArray())
            .WithCountStrategy(request.IncludeTotalCount ? CountStrategy.Optimized : null);
        if (request.ApplyPagination && options.Page > 0 && options.PageSize > 0)
            query = query.WithPagination(options.Page, options.PageSize);
        return query;
    }
    // WEB-0068 / AN-leak: collection ?with=all expands each row's relationships through the SAME governed
    // path as the keyed read — every related entity is gated by its own type's visibility predicates.
    // Scope the partition explicitly: the QueryCollection partition scope has already disposed by here.
    private static async Task<object> EnrichRelationships(IReadOnlyList<TEntity> list, EntityRequestContext context, string? set,
        ICollection<Func<bool>> retainedEvidence)
    {
        using var _ = EntityContext.With(partition: set);
        if (list.Count == 0) return Array.Empty<RelationshipGraph<TEntity>>();
        if (list.Any(item => item is not Entity<TEntity, TKey>)) return list;
        var roots = list.Select(item => (item, item.Id)).ToArray();
        var checks = new List<Func<bool>>();
        var enriched = await GovernedRelationshipExpander.ExpandManyAsync<TEntity, TKey>(roots, context, checks);
        foreach (var check in checks)
            retainedEvidence.Add(() =>
            {
                using var partition = EntityContext.With(partition: set);
                return check();
            });
        return enriched;
    }

    private static ObjectResult RelationshipRejectedResult(RelationshipQueryRejectedException exception)
        => new(new
        {
            error = "Relationship expansion rejected",
            reason = exception.ReasonCode,
            relationship = $"{exception.ParentType}->{exception.ChildType}.{exception.ReferenceProperty}",
            provider = exception.Provider,
            correction = exception.Correction,
            limit = exception.Limit
        })
        {
            StatusCode = exception.IsLimitExceeded
                ? StatusCodes.Status413PayloadTooLarge
                : StatusCodes.Status422UnprocessableEntity
        };

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

    private static async Task<EntityCollectionResult<TEntity>> CollectionShortCircuit(EntityRequestContext context, HookContext<TEntity> hookContext, bool requiresProof = false)
    {
        CopyHookHeaders(context, hookContext);
        var shortCircuit = hookContext.ShortCircuitPayload;
        if ((requiresProof || Filter.HasCounterpart(context.Options.Filter)) && !IsDenial(shortCircuit))
            return new EntityCollectionResult<TEntity>(context, [], 0, null, ReadEvidenceChanged(context));
        if (requiresProof || Filter.HasCounterpart(context.Options.Filter))
        {
            context.Items.Remove(AccessProjection.ManifestKey);
            context.Headers.Clear();
        }
        if ((requiresProof || Filter.HasCounterpart(context.Options.Filter)) && shortCircuit is ObjectResult { StatusCode: >= 400 } denial)
            shortCircuit = new StatusCodeResult(denial.StatusCode.Value);
        if (await AdmitFieldOutput(context, shortCircuit) is { } fieldOutputDenied)
            shortCircuit = fieldOutputDenied;
        if (shortCircuit is IActionResult action)
        {
            return new EntityCollectionResult<TEntity>(context, [], 0, null, action);
        }
        return new EntityCollectionResult<TEntity>(context, [], 0, shortCircuit, shortCircuit);
    }

    private static async Task<EntityModelResult<TEntity>> ModelShortCircuit(EntityRequestContext context, HookContext<TEntity> hookContext, bool requiresProof = false)
    {
        CopyHookHeaders(context, hookContext);
        var shortCircuit = hookContext.ShortCircuitPayload;
        if ((requiresProof || Filter.HasCounterpart(context.Options.Filter)) && !IsDenial(shortCircuit))
            return new EntityModelResult<TEntity>(context, null, null, ReadEvidenceChanged(context));
        if (requiresProof || Filter.HasCounterpart(context.Options.Filter))
        {
            context.Items.Remove(AccessProjection.ManifestKey);
            context.Headers.Clear();
        }
        if ((requiresProof || Filter.HasCounterpart(context.Options.Filter)) && shortCircuit is ObjectResult { StatusCode: >= 400 } denial)
            shortCircuit = new StatusCodeResult(denial.StatusCode.Value);
        if (await AdmitFieldOutput(context, shortCircuit) is { } fieldOutputDenied)
            shortCircuit = fieldOutputDenied;
        if (shortCircuit is IActionResult action)
        {
            return new EntityModelResult<TEntity>(context, default, null, action);
        }
        return new EntityModelResult<TEntity>(context, default, shortCircuit, shortCircuit);
    }

    private static bool IsDenial(object? result)
        => result is StatusCodeResult { StatusCode: >= 400 }
            or ObjectResult { StatusCode: >= 400 } or ForbidResult or ChallengeResult;

}











