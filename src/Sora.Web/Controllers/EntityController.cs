using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sora.Data.Abstractions;
using Sora.Data.Core;
using Sora.Data.Core.Model;
using Sora.Web.Attributes;
using Sora.Web.Filtering;
using Sora.Web.Hooks;
using Sora.Web.Infrastructure;
using System.Net.Mime;

namespace Sora.Web.Controllers;

/// <summary>
/// Generic controller providing CRUD endpoints for any aggregate.
/// Integrates pluggable hooks, pagination, capability headers, and content shaping.
/// </summary>
[ApiController]
public abstract class EntityController<TEntity, TKey> : ControllerBase
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    protected virtual bool CanRead => true;
    protected virtual bool CanWrite => true;
    protected virtual bool CanRemove => true;


    protected virtual string GetDisplay(TEntity e)
    {
        var t = e!.GetType();
        var name = t.GetProperty("Name")?.GetValue(e) as string
                   ?? t.GetProperty("Title")?.GetValue(e) as string
                   ?? t.GetProperty("Label")?.GetValue(e) as string
                   ?? e.ToString();
        return name ?? string.Empty;
    }

    private IQueryCapabilities Capabilities(IDataRepository<TEntity, TKey> repo)
        => repo as IQueryCapabilities ?? new RepositoryCapabilities(QueryCapabilities.None);

    private sealed record RepositoryCapabilities(QueryCapabilities Value) : IQueryCapabilities { public QueryCapabilities Capabilities => Value; }

    private IWriteCapabilities WriteCaps(IDataRepository<TEntity, TKey> repo)
        => repo as IWriteCapabilities ?? new RepoWriteCaps(WriteCapabilities.None);
    private sealed record RepoWriteCaps(WriteCapabilities Value) : IWriteCapabilities { public WriteCapabilities Writes => Value; }

    protected virtual QueryOptions BuildOptions()
    {
        var q = HttpContext.Request.Query;
        var opts = new QueryOptions();
        // Apply defaults from attribute
        var beh = GetType().GetCustomAttributes(typeof(SoraDataBehaviorAttribute), true).FirstOrDefault() as SoraDataBehaviorAttribute;
        if (q.TryGetValue("q", out var vq)) opts.Q = vq.FirstOrDefault();
        if (q.TryGetValue("page", out var vp) && int.TryParse(vp, out var p) && p > 0) opts.Page = p; else opts.Page = 1;
        var maxSize = beh?.MaxPageSize ?? SoraWebConstants.Defaults.MaxPageSize;
        var defSize = beh?.DefaultPageSize ?? SoraWebConstants.Defaults.DefaultPageSize;
        if (q.TryGetValue("size", out var vs) && int.TryParse(vs, out var s) && s > 0) opts.PageSize = Math.Min(s, maxSize); else opts.PageSize = defSize;
        if (q.TryGetValue("sort", out var vsort))
        {
            foreach (var spec in vsort.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var desc = spec.StartsWith('-');
                var field = desc ? spec[1..] : spec;
                if (!string.IsNullOrWhiteSpace(field)) opts.Sort.Add(new SortSpec(field, desc));
            }
        }
        if (q.TryGetValue("dir", out var vdir) && opts.Sort.Count == 1)
        {
            var d = vdir.ToString();
            if (string.Equals(d, "desc", StringComparison.OrdinalIgnoreCase))
                opts.Sort[0] = new SortSpec(opts.Sort[0].Field, true);
        }
        if (q.TryGetValue("output", out var vout)) opts.Shape = vout.ToString();
        if (q.TryGetValue("view", out var vview)) opts.View = vview.ToString();
        if (beh?.MustPaginate == true)
        {
            // Ensure page/size are set; already ensured above.
        }
        return opts;
    }

    protected virtual IQueryable<TEntity> ApplySort(IQueryable<TEntity> query, QueryOptions opts)
    {
        // v1: best-effort; for LINQ-capable providers with in-memory IQueryable
        // If we cannot apply, return as-is.
        return query;
    }

    protected virtual ObjectResult PrepareResponse(object content)
    {
        // Mark Accept as a content-variant
        Response.Headers["Vary"] = "Accept";
        Response.Headers["Sora-Access-Read"] = CanRead.ToString().ToLowerInvariant();
        Response.Headers["Sora-Access-Write"] = CanWrite.ToString().ToLowerInvariant();
        Response.Headers["Sora-Access-Remove"] = CanRemove.ToString().ToLowerInvariant();
        return new ObjectResult(content);
    }

    // DI hook aggregator
    private HookRunner<TEntity> GetRunner()
    {
        var sp = HttpContext.RequestServices;
        return new HookRunner<TEntity>(
            sp.GetServices<IAuthorizeHook<TEntity>>(),
            sp.GetServices<IRequestOptionsHook<TEntity>>(),
            sp.GetServices<ICollectionHook<TEntity>>(),
            sp.GetServices<IModelHook<TEntity>>(),
            sp.GetServices<IEmitHook<TEntity>>()
        );
    }

    [HttpGet("")]
    [Produces(MediaTypeNames.Application.Json)]
    public virtual async Task<IActionResult> GetCollection(CancellationToken ct)
    {
        if (!CanRead) return Forbid();
        // Validate explicit negative page requests
        if (HttpContext.Request.Query.TryGetValue("page", out var _vp)
            && int.TryParse(_vp, out var _p)
            && _p < 0)
        {
            return BadRequest(new { error = "page must be >= 0" });
        }

        // Repository + capabilities + options
        var repo = HttpContext.RequestServices.GetRequiredService<IDataService>().GetRepository<TEntity, TKey>();
        var caps = Capabilities(repo);
        var opts = BuildOptions();
        var withParam = HttpContext.Request.Query.TryGetValue("with", out var _w) ? _w.ToString() : null;
        ILogger? log = null; // optional diagnostic (kept minimal, no spam)
        try { log = HttpContext.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("Sora.Web.EntityController"); } catch { }

        var ctx = new HookContext<TEntity> { Http = HttpContext, Services = HttpContext.RequestServices, Options = opts, Capabilities = caps, Ct = ct };
        var runner = GetRunner();

        if (!await runner.BuildOptionsAsync(ctx, opts)) return ctx.ShortCircuitResult!;
        if (!await runner.BeforeCollectionAsync(ctx, opts)) return ctx.ShortCircuitResult!;

        // Accept either string q or JSON filter via querystring: filter={} (URL or single-quoted)
        IReadOnlyList<TEntity> items;
        int total = 0;
        System.Linq.Expressions.Expression<Func<TEntity, bool>>? builtPredicate = null;
        var filterQs = HttpContext.Request.Query.TryGetValue("filter", out var f) ? f.ToString() : null;
        var set = HttpContext.Request.Query.TryGetValue("set", out var sVal) ? sVal.ToString() : null;
        // Optional: ignoreCase query parameter (true/1/yes) to enable case-insensitive string matching
        bool ignoreCase = false;
        if (HttpContext.Request.Query.TryGetValue("ignoreCase", out var icVal))
        {
            var v = icVal.ToString();
            ignoreCase = v.Equals("true", StringComparison.OrdinalIgnoreCase)
                      || v.Equals("1")
                      || v.Equals("yes", StringComparison.OrdinalIgnoreCase)
                      || v.Equals("on", StringComparison.OrdinalIgnoreCase);
        }
        using var _set = Sora.Data.Core.DataSetContext.With(string.IsNullOrWhiteSpace(set) ? null : set);
        if (!string.IsNullOrWhiteSpace(filterQs) && repo is ILinqQueryRepository<TEntity, TKey> lrepo)
        {
            if (!JsonFilterBuilder.TryBuild<TEntity>(filterQs, out var predicate, out var error, new JsonFilterBuilder.BuildOptions { IgnoreCase = ignoreCase }))
                return BadRequest(new { error = error ?? "Invalid filter" });
            builtPredicate = predicate!;
            if (repo is ILinqQueryRepositoryWithOptions<TEntity, TKey> lrepoOpts)
            {
                var dq = new Sora.Data.Abstractions.DataQueryOptions(opts.Page, opts.PageSize);
                items = await lrepoOpts.QueryAsync(builtPredicate, dq, ct);
            }
            else
            {
                items = await lrepo.QueryAsync(builtPredicate, ct);
            }
            try { total = await lrepo.CountAsync(builtPredicate, ct); } catch { total = items.Count; }
        }
        else if (!string.IsNullOrWhiteSpace(opts.Q) && repo is IStringQueryRepository<TEntity, TKey> srepo)
        {
            if (repo is IStringQueryRepositoryWithOptions<TEntity, TKey> srepoOpts)
            {
                var dq = new Sora.Data.Abstractions.DataQueryOptions(opts.Page, opts.PageSize);
                items = await srepoOpts.QueryAsync(opts.Q!, dq, ct);
            }
            else
            {
                items = await srepo.QueryAsync(opts.Q!, ct);
            }
            try { total = await srepo.CountAsync(opts.Q!, ct); } catch { total = items.Count; }
        }
        else
        {
            if (repo is IDataRepositoryWithOptions<TEntity, TKey> repoOpts)
            {
                var dq = new Sora.Data.Abstractions.DataQueryOptions(opts.Page, opts.PageSize);
                items = await repoOpts.QueryAsync(null, dq, ct);
            }
            else
            {
                items = await repo.QueryAsync(null, ct);
            }
            try { total = await repo.CountAsync(null, ct); } catch { total = items.Count; }
        }

        var list = items.ToList();
        // Server-side pagination (currently in-memory; providers should implement native paging)
        var beh = GetType().GetCustomAttributes(typeof(SoraDataBehaviorAttribute), true).FirstOrDefault() as SoraDataBehaviorAttribute;
        if ((beh?.MustPaginate ?? false) || HttpContext.Request.Query.ContainsKey("page") || HttpContext.Request.Query.ContainsKey("size"))
        {
            var skip = (opts.Page - 1) * opts.PageSize;
            list = list.Skip(skip).Take(opts.PageSize).ToList();
            Response.Headers["Sora-InMemory-Paging"] = "true";
            // Pagination metadata
            var page = opts.Page;
            var size = opts.PageSize;
            var totalPages = size > 0 ? (int)Math.Ceiling((double)total / size) : 0;

            Response.Headers["X-Total-Count"] = total.ToString();
            Response.Headers["X-Page"] = page.ToString();
            Response.Headers["X-Page-Size"] = size.ToString();
            Response.Headers["X-Total-Pages"] = totalPages.ToString();

            // RFC 5988 Link header: first, prev, next, last (when applicable)
            var links = new List<string>();
            string BuildLink(int targetPage, string rel)
            {
                var req = HttpContext.Request;
                var basePath = req.Path.HasValue ? req.Path.Value! : "/";
                var dict = req.Query.ToDictionary(kv => kv.Key, kv => (string?)kv.Value.ToString());
                dict["page"] = targetPage.ToString();
                dict["size"] = size.ToString();
                var uri = QueryHelpers.AddQueryString(basePath, dict);
                return $"<${uri}>; rel=\"{rel}\"";
            }
            if (totalPages > 0)
            {
                links.Add(BuildLink(1, "first"));
                links.Add(BuildLink(totalPages, "last"));
                if (page > 1) links.Add(BuildLink(page - 1, "prev"));
                if (page < totalPages) links.Add(BuildLink(page + 1, "next"));
            }
            if (links.Count > 0) Response.Headers["Link"] = string.Join(", ", links);
        }
        if (!await runner.AfterCollectionAsync(ctx, list)) return ctx.ShortCircuitResult!;

        // Parent aggregation (executed after hooks/pagination). If 'with' absent proceed with normal shaping.
        if (!string.IsNullOrWhiteSpace(withParam))
        {
            var parentKeys = withParam.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                      .Select(k => k.Trim())
                                      .ToList();
            var relMeta = HttpContext.RequestServices.GetRequiredService<Sora.Data.Core.Relationships.IRelationshipMetadata>();
            var parentRels = relMeta.GetParentRelationships(typeof(TEntity));
            var resultList = new List<Dictionary<string, object?>>();
            foreach (var model in list)
            {
                var parentDict = new Dictionary<string, object?>();
                foreach (var (prop, parentType) in parentRels)
                {
                    bool include = parentKeys.Any(k =>
                        string.Equals(k, prop, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(k, parentType.Name, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(k, parentType.FullName, StringComparison.OrdinalIgnoreCase)
                    ) || parentKeys.Any(k => string.Equals(k, "all", StringComparison.OrdinalIgnoreCase));
                    if (!include) continue;
                    var parentId = typeof(TEntity).GetProperty(prop)?.GetValue(model);
                    if (parentId is null) continue;
                    var dataType = typeof(Sora.Data.Core.Data<,>).MakeGenericType(parentType, typeof(string));
                    var method = dataType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                        .FirstOrDefault(m => m.Name == "GetAsync"
                            && m.GetParameters().Length == 2
                            && m.GetParameters()[0].ParameterType == typeof(string)
                            && m.GetParameters()[1].ParameterType == typeof(System.Threading.CancellationToken));
                    if (method == null) continue;
                    var parentTask = (Task)method.Invoke(null, new object[] { parentId.ToString(), ct });
                    await parentTask.ConfigureAwait(false);
                    var parentResult = parentTask.GetType().GetProperty("Result")?.GetValue(parentTask);
                    parentDict[prop] = parentResult;
                }
                var response = new Dictionary<string, object?>();
                foreach (var p in typeof(TEntity).GetProperties())
                    response[p.Name] = p.GetValue(model);
                response["_parent"] = parentDict;
                resultList.Add(response);
            }
            foreach (var kv in ctx.ResponseHeaders) Response.Headers[kv.Key] = kv.Value;
            return PrepareResponse(resultList);
        }

        // No parent aggregation: optional shape mapping then emit hook
        object payload = list;
        if (string.Equals(opts.Shape, "map", StringComparison.OrdinalIgnoreCase))
            payload = list.Select(i => new { key = (object?)((dynamic)i).Id, display = GetDisplay(i) }).ToList();
        else if (string.Equals(opts.Shape, "dict", StringComparison.OrdinalIgnoreCase))
            payload = list.ToDictionary(i => (object?)((dynamic)i).Id!, i => (object)GetDisplay(i));

        var accept = HttpContext.Request.Headers["Accept"].ToString();
        if (!string.IsNullOrWhiteSpace(opts.View)) Response.Headers["Sora-View"] = opts.View!;
        else if (!string.IsNullOrWhiteSpace(accept)) Response.Headers["Sora-View"] = ParseViewFromAccept(accept) ?? "full";

        var (replaced2, transformed2) = await runner.EmitCollectionAsync(ctx, payload);
        payload = transformed2;
        foreach (var kv in ctx.ResponseHeaders) Response.Headers[kv.Key] = kv.Value;
        return PrepareResponse(payload);
    }

    // POST /query accepts a JSON body with shape: { filter, page, size, sort, set, $options }
    [HttpPost("query")]
    [Produces(MediaTypeNames.Application.Json)]
    public virtual async Task<IActionResult> Query([FromBody] object body, CancellationToken ct)
    {
        if (!CanRead) return Forbid();
        var repo = HttpContext.RequestServices.GetRequiredService<IDataService>().GetRepository<TEntity, TKey>();
        var caps = Capabilities(repo);
        var opts = BuildOptions();
        var ctx = new HookContext<TEntity> { Http = HttpContext, Services = HttpContext.RequestServices, Options = opts, Capabilities = caps, Ct = ct };
        var runner = GetRunner();

        if (!await runner.BuildOptionsAsync(ctx, opts)) return ctx.ShortCircuitResult!;
        if (!await runner.BeforeCollectionAsync(ctx, opts)) return ctx.ShortCircuitResult!;

        // Minimal parse: expect { filter?: {}, page?: n, size?: n }
        string? filterJson = null;
        bool ignoreCase = false;
        string? set = null;
        try
        {
            var jobj = Newtonsoft.Json.Linq.JObject.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(body));
            if (jobj.TryGetValue("filter", out var f)) filterJson = f.ToString(Newtonsoft.Json.Formatting.None);
            if (jobj.TryGetValue("page", out var p) && (p.Type == Newtonsoft.Json.Linq.JTokenType.Integer || p.Type == Newtonsoft.Json.Linq.JTokenType.String)) opts.Page = (int)p;
            if (jobj.TryGetValue("size", out var s) && (s.Type == Newtonsoft.Json.Linq.JTokenType.Integer || s.Type == Newtonsoft.Json.Linq.JTokenType.String)) opts.PageSize = (int)s;
            if (jobj.TryGetValue("set", out var st) && st.Type == Newtonsoft.Json.Linq.JTokenType.String) set = (string?)st;
            if (jobj.TryGetValue("$options", out var opt) && opt.Type == Newtonsoft.Json.Linq.JTokenType.Object)
            {
                var o = (Newtonsoft.Json.Linq.JObject)opt;
                if (o.TryGetValue("ignoreCase", out var ic) && ic.Type == Newtonsoft.Json.Linq.JTokenType.Boolean && (bool)ic) ignoreCase = true;
            }
        }
        catch { }

        IReadOnlyList<TEntity> items;
        int total = 0;
        System.Linq.Expressions.Expression<Func<TEntity, bool>>? builtPredicate = null;
        using var _set = Sora.Data.Core.DataSetContext.With(string.IsNullOrWhiteSpace(set) ? null : set);
        if (!string.IsNullOrWhiteSpace(filterJson) && repo is ILinqQueryRepository<TEntity, TKey> lrepo)
        {
            if (!JsonFilterBuilder.TryBuild<TEntity>(filterJson, out var predicate, out var error, new JsonFilterBuilder.BuildOptions { IgnoreCase = ignoreCase }))
                return BadRequest(new { error = error ?? "Invalid filter" });
            builtPredicate = predicate!;
            items = await lrepo.QueryAsync(builtPredicate, ct);
            try { total = await lrepo.CountAsync(builtPredicate, ct); } catch { total = items.Count; }
        }
        else if (!string.IsNullOrWhiteSpace(opts.Q) && repo is IStringQueryRepository<TEntity, TKey> srepo)
        {
            items = await srepo.QueryAsync(opts.Q!, ct);
            try { total = await srepo.CountAsync(opts.Q!, ct); } catch { total = items.Count; }
        }
        else
        {
            items = await repo.QueryAsync(null, ct);
            try { total = await repo.CountAsync(null, ct); } catch { total = items.Count; }
        }

        var list = items.ToList();
        var beh = GetType().GetCustomAttributes(typeof(SoraDataBehaviorAttribute), true).FirstOrDefault() as SoraDataBehaviorAttribute;
        if ((beh?.MustPaginate ?? false) || opts.PageSize > 0)
        {
            var skip = (opts.Page - 1) * opts.PageSize;
            list = list.Skip(skip).Take(opts.PageSize).ToList();
            Response.Headers["Sora-InMemory-Paging"] = "true";
            var page = opts.Page;
            var size = opts.PageSize;
            var totalPages = size > 0 ? (int)Math.Ceiling((double)total / size) : 0;
            Response.Headers["X-Total-Count"] = total.ToString();
            Response.Headers["X-Page"] = page.ToString();
            Response.Headers["X-Page-Size"] = size.ToString();
            Response.Headers["X-Total-Pages"] = totalPages.ToString();
        }

        if (!await runner.AfterCollectionAsync(ctx, list)) return ctx.ShortCircuitResult!;
        // Parent relationship aggregation for collections
        var with = HttpContext.Request.Query.TryGetValue("with", out var wVal) ? wVal.ToString() : null;
        ILogger? log = null;
        try { log = HttpContext.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("Sora.Web.EntityController.ParentDebug"); } catch { }
        log?.LogInformation("PARENT_DEBUG: [A] with param raw value: {With}", with);
        log?.LogInformation("PARENT_DEBUG: [B] IsNullOrWhiteSpace(with): {Result}", string.IsNullOrWhiteSpace(with));
        if (!string.IsNullOrWhiteSpace(with))
        {
            log?.LogInformation("PARENT_DEBUG: [C] Entered parent aggregation block for entity={Entity} with={With}", typeof(TEntity).Name, with);
            var parentKeys = with.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            log?.LogInformation("PARENT_DEBUG: [D] parentKeys={ParentKeys}", string.Join(',', parentKeys));
            var relMeta = HttpContext.RequestServices.GetRequiredService<Sora.Data.Core.Relationships.IRelationshipMetadata>();
            log?.LogInformation("PARENT_DEBUG: [E] Got relMeta instance: {Type}", relMeta?.GetType().FullName);
            var parentRels = relMeta.GetParentRelationships(typeof(TEntity));
            log?.LogInformation("PARENT_DEBUG: [F] parentRels={ParentRels}", string.Join(',', parentRels.Select(r => r.Item1)));
            if (parentRels.Count == 0)
            {
                log?.LogWarning("PARENT_DEBUG: [G] No parent relationships discovered for entity={Entity}. Check model annotations and DI registration.", typeof(TEntity).Name);
            }
            try { Response.Headers["X-Debug-With"] = with!; } catch { }
            try { Response.Headers["X-Debug-ParentRels"] = parentRels.Count.ToString(); } catch { }
            log?.LogInformation("PARENT_DEBUG: [H] Collection parent rel count={Count} entity={Entity}", parentRels.Count, typeof(TEntity).Name);
            var resultList = new List<Dictionary<string, object?>>();
            foreach (var model in list)
            {
                log?.LogInformation("PARENT_DEBUG: [I] Aggregating model: {Model}", model);
                var parentDict = new Dictionary<string, object?>();
                foreach (var (prop, parentType) in parentRels)
                {
                    log?.LogInformation("PARENT_DEBUG: [J] Checking prop={Prop} for parent aggregation", prop);
                    if (parentKeys.Contains(prop, StringComparer.OrdinalIgnoreCase) || parentKeys.Contains("all", StringComparer.OrdinalIgnoreCase))
                    {
                        var parentId = typeof(TEntity).GetProperty(prop)?.GetValue(model);
                        log?.LogInformation("PARENT_DEBUG: [K] Found parentId={ParentId} for prop={Prop}", parentId, prop);
                        if (parentId != null)
                        {
                            var method = typeof(Sora.Data.Core.Data<,>)
                                .MakeGenericType(parentType, typeof(string))
                                .GetMethod("GetAsync", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                            log?.LogInformation("PARENT_DEBUG: [L] Invoking GetAsync for parentType={ParentType} parentId={ParentId}", parentType.Name, parentId);
                            var parentTask = (Task)method.Invoke(null, new object[] { parentId.ToString(), ct });
                            await parentTask.ConfigureAwait(false);
                            var parentResult = parentTask.GetType().GetProperty("Result")?.GetValue(parentTask);
                            parentDict[prop] = parentResult;
                            log?.LogInformation("PARENT_DEBUG: [M] Aggregated parent for prop={Prop} id={ParentId}", prop, parentId);
                        }
                        else
                        {
                            log?.LogInformation("PARENT_DEBUG: [N] Missing parentId for prop={Prop} entity={Entity}", prop, typeof(TEntity).Name);
                        }
                    }
                }
                var response = new Dictionary<string, object?>();
                foreach (var p in typeof(TEntity).GetProperties())
                    response[p.Name] = p.GetValue(model);
                response["_parent"] = parentDict;
                var idVal = typeof(TEntity).GetProperty("Id")?.GetValue(model);
                log?.LogInformation("PARENT_DEBUG: [O] Aggregated collection model id={Id} parentKeys={ParentKeys}", idVal, string.Join(',', parentDict.Keys));
                resultList.Add(response);
            }
            foreach (var kv in ctx.ResponseHeaders) Response.Headers[kv.Key] = kv.Value;
            log?.LogInformation("PARENT_DEBUG: [P] Exiting collection aggregation entity={Entity} count={Count}", typeof(TEntity).Name, resultList.Count);
            return PrepareResponse(resultList);
        }
        var (replaced, transformed) = await runner.EmitCollectionAsync(ctx, list);
        foreach (var kv in ctx.ResponseHeaders) Response.Headers[kv.Key] = kv.Value;
        return PrepareResponse(transformed);
    }

    [HttpGet("new")]
    public virtual async Task<IActionResult> GetNew(CancellationToken ct)
    {
        if (!CanRead) return Forbid();
        var repo = HttpContext.RequestServices.GetRequiredService<IDataService>().GetRepository<TEntity, TKey>();
        var caps = Capabilities(repo);
        var opts = BuildOptions();
        var ctx = new HookContext<TEntity> { Http = HttpContext, Services = HttpContext.RequestServices, Options = opts, Capabilities = caps, Ct = ct };
        var runner = GetRunner();

        var model = Activator.CreateInstance<TEntity>();
        await runner.AfterModelFetchAsync(ctx, model);

        // Accept/view header
        var accept = HttpContext.Request.Headers["Accept"].ToString();
        if (!string.IsNullOrWhiteSpace(opts.View)) Response.Headers["Sora-View"] = opts.View!;
        else if (!string.IsNullOrWhiteSpace(accept)) Response.Headers["Sora-View"] = ParseViewFromAccept(accept) ?? "full";

        var (replaced, transformed) = await runner.EmitModelAsync(ctx, model!);
        var payload = transformed;
        foreach (var kv in ctx.ResponseHeaders) Response.Headers[kv.Key] = kv.Value;
        return PrepareResponse(payload);
    }

    [HttpGet("{id}")]
    public virtual async Task<IActionResult> GetById([FromRoute] TKey id, CancellationToken ct)
    {
        if (!CanRead) return Forbid();
        var repo = HttpContext.RequestServices.GetRequiredService<IDataService>().GetRepository<TEntity, TKey>();
        var caps = Capabilities(repo);
        var opts = BuildOptions();
        var ctx = new HookContext<TEntity> { Http = HttpContext, Services = HttpContext.RequestServices, Options = opts, Capabilities = caps, Ct = ct };
        var runner = GetRunner();

        // Optional set routing via querystring (?set=)
        var set = HttpContext.Request.Query.TryGetValue("set", out var sVal) ? sVal.ToString() : null;
        var idStr = id?.ToString() ?? string.Empty;
        if (!await runner.BeforeModelFetchAsync(ctx, idStr)) return ctx.ShortCircuitResult!;
        using var _set = Sora.Data.Core.DataSetContext.With(string.IsNullOrWhiteSpace(set) ? null : set);
        var model = await Data<TEntity, TKey>.GetAsync(id!, ct);
        await runner.AfterModelFetchAsync(ctx, model);
        if (model == null) return NotFound();

        // Accept/view header
        var accept = HttpContext.Request.Headers["Accept"].ToString();
        if (!string.IsNullOrWhiteSpace(opts.View)) Response.Headers["Sora-View"] = opts.View!;
        else if (!string.IsNullOrWhiteSpace(accept)) Response.Headers["Sora-View"] = ParseViewFromAccept(accept) ?? "full";

        // Parent relationship aggregation
        var with = HttpContext.Request.Query.TryGetValue("with", out var wVal) ? wVal.ToString() : null;
        if (!string.IsNullOrWhiteSpace(with))
        {
            try { Response.Headers["X-Debug-With"] = with!; } catch { }
            ILogger? log = null; try { log = HttpContext.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("Sora.Web.EntityController.ParentDebug"); } catch { }
            log?.LogInformation("PARENT_DEBUG: Enter single aggregation entity={Entity} id={Id} with={With}", typeof(TEntity).Name, idStr, with);
            var parentKeys = with.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var relMeta = HttpContext.RequestServices.GetRequiredService<Sora.Data.Core.Relationships.IRelationshipMetadata>();
            var parentRels = relMeta.GetParentRelationships(typeof(TEntity));
            try { Response.Headers["X-Debug-ParentRels"] = parentRels.Count.ToString(); } catch { }
            log?.LogInformation("PARENT_DEBUG: Single parent rel count={Count} entity={Entity}", parentRels.Count, typeof(TEntity).Name);
            var parentDict = new Dictionary<string, object?>();
            foreach (var (prop, parentType) in parentRels)
            {
                if (parentKeys.Contains(prop, StringComparer.OrdinalIgnoreCase) || parentKeys.Contains("all", StringComparer.OrdinalIgnoreCase))
                {
                    var parentId = typeof(TEntity).GetProperty(prop)?.GetValue(model);
                    if (parentId != null)
                    {
                        // Use reflection to invoke Data<TParent, string>.GetAsync for parent lookup
                        var method = typeof(Sora.Data.Core.Data<,>)
                            .MakeGenericType(parentType, typeof(string))
                            .GetMethod("GetAsync", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        var parentTask = (Task)method.Invoke(null, new object[] { parentId.ToString(), ct });
                        await parentTask.ConfigureAwait(false);
                        var parentResult = parentTask.GetType().GetProperty("Result")?.GetValue(parentTask);
                        parentDict[prop] = parentResult;
                        log?.LogInformation("PARENT_DEBUG: Single aggregated parent prop={Prop} entity={Entity}", prop, typeof(TEntity).Name);
                    }
                    else
                    {
                        log?.LogInformation("PARENT_DEBUG: Single missing parentId for prop={Prop} entity={Entity}", prop, typeof(TEntity).Name);
                    }
                }
            }
            var response = new Dictionary<string, object?>();
            foreach (var p in typeof(TEntity).GetProperties())
                response[p.Name] = p.GetValue(model);
            response["_parent"] = parentDict;
            log?.LogInformation("PARENT_DEBUG: Exiting single aggregation entity={Entity} id={Id} parentKeys={ParentKeys}", typeof(TEntity).Name, idStr, string.Join(',', parentDict.Keys));
            return PrepareResponse(response);
        }

        var (replaced, transformed) = await runner.EmitModelAsync(ctx, model);
        foreach (var kv in ctx.ResponseHeaders) Response.Headers[kv.Key] = kv.Value;
        return PrepareResponse(transformed);
    }

    [HttpPost("")]
    public virtual async Task<IActionResult> Upsert([FromBody][ValidateNever] TEntity model, CancellationToken ct)
    {
        if (model is null) return BadRequest(new { error = "Request body is required" });
        if (!CanWrite) return Forbid();
        var repo = HttpContext.RequestServices.GetRequiredService<IDataService>().GetRepository<TEntity, TKey>();
        var caps = Capabilities(repo);
        var opts = BuildOptions();
        var ctx = new HookContext<TEntity> { Http = HttpContext, Services = HttpContext.RequestServices, Options = opts, Capabilities = caps, Ct = ct };
        var runner = GetRunner();

        // Optional set routing via querystring (?set=)
        var set = HttpContext.Request.Query.TryGetValue("set", out var sVal) ? sVal.ToString() : null;

        await runner.BeforeSaveAsync(ctx, model);
        TEntity saved;
        if (!string.IsNullOrWhiteSpace(set))
        {
            saved = await model.Upsert<TEntity, TKey>(set!, ct);
        }
        else
        {
            saved = await model.Upsert<TEntity, TKey>(ct);
        }
        await runner.AfterSaveAsync(ctx, saved);

        // Accept/view header
        var accept = HttpContext.Request.Headers["Accept"].ToString();
        if (!string.IsNullOrWhiteSpace(opts.View)) Response.Headers["Sora-View"] = opts.View!;
        else if (!string.IsNullOrWhiteSpace(accept)) Response.Headers["Sora-View"] = ParseViewFromAccept(accept) ?? "full";

        var (replaced, transformed) = await runner.EmitModelAsync(ctx, saved);
        foreach (var kv in ctx.ResponseHeaders) Response.Headers[kv.Key] = kv.Value;
        return PrepareResponse(transformed);
    }

    // Bulk upsert
    [HttpPost("bulk")]
    public virtual async Task<IActionResult> UpsertMany([FromBody][ValidateNever] IEnumerable<TEntity> models, CancellationToken ct)
    {
        if (models is null) return BadRequest(new { error = "Request body is required" });
        if (!CanWrite) return Forbid();
        var repo = HttpContext.RequestServices.GetRequiredService<IDataService>().GetRepository<TEntity, TKey>();
        var caps = Capabilities(repo);
        var writes = WriteCaps(repo);
        var opts = BuildOptions();
        var ctx = new HookContext<TEntity> { Http = HttpContext, Services = HttpContext.RequestServices, Options = opts, Capabilities = caps, Ct = ct };
        var runner = GetRunner();

        // Optional set routing via querystring (?set=)
        var set = HttpContext.Request.Query.TryGetValue("set", out var sVal) ? sVal.ToString() : null;

        // Run per-model BeforeSave hooks and ensure IDs via facade
        var list = models.ToList();
        if (list.Count == 0) return BadRequest(new { error = "At least one item is required" });
        if (list.Any(m => m is null)) return BadRequest(new { error = "Null items are not allowed" });
        foreach (var m in list) await runner.BeforeSaveAsync(ctx, m);

        int count;
        if (!string.IsNullOrWhiteSpace(set))
        {
            using var _set = Sora.Data.Core.DataSetContext.With(set);
            count = await Data<TEntity, TKey>.UpsertManyAsync(list, ct);
        }
        else
        {
            count = await Data<TEntity, TKey>.UpsertManyAsync(list, ct);
        }
        foreach (var m in list) await runner.AfterSaveAsync(ctx, m);

        Response.Headers["Sora-Write-Capabilities"] = writes.Writes.ToString();
        return Ok(new { upserted = count });
    }

    [HttpDelete("{id}")]
    public virtual async Task<IActionResult> Delete([FromRoute] TKey id, CancellationToken ct)
    {
        if (!CanRemove) return Forbid();
        var repo = HttpContext.RequestServices.GetRequiredService<IDataService>().GetRepository<TEntity, TKey>();
        var caps = Capabilities(repo);
        var opts = BuildOptions();
        var ctx = new HookContext<TEntity> { Http = HttpContext, Services = HttpContext.RequestServices, Options = opts, Capabilities = caps, Ct = ct };
        var runner = GetRunner();

    // Optional set routing via querystring (?set=)
    var set = HttpContext.Request.Query.TryGetValue("set", out var sVal) ? sVal.ToString() : null;
    using var _set = Sora.Data.Core.DataSetContext.With(string.IsNullOrWhiteSpace(set) ? null : set);

    var model = await Data<TEntity, TKey>.GetAsync(id, ct);
        if (model == null) return NotFound();
        await runner.BeforeDeleteAsync(ctx, model);
        var ok = await Data<TEntity, TKey>.DeleteAsync(id, ct);
        if (!ok) return NotFound();
        await runner.AfterDeleteAsync(ctx, model);

        var (replaced, transformed) = await runner.EmitModelAsync(ctx, model);
        foreach (var kv in ctx.ResponseHeaders) Response.Headers[kv.Key] = kv.Value;
        return PrepareResponse(transformed);
    }

    // Bulk delete by IDs
    [HttpDelete("bulk")]
    public virtual async Task<IActionResult> DeleteMany([FromBody] IEnumerable<TKey> ids, CancellationToken ct)
    {
        if (!CanRemove) return Forbid();
        var repo = HttpContext.RequestServices.GetRequiredService<IDataService>().GetRepository<TEntity, TKey>();
        var writes = WriteCaps(repo);
        // Optional set routing via querystring (?set=)
        var set = HttpContext.Request.Query.TryGetValue("set", out var sVal) ? sVal.ToString() : null;
        int deleted;
        if (!string.IsNullOrWhiteSpace(set))
        {
            using var _set = Sora.Data.Core.DataSetContext.With(set);
            deleted = await Data<TEntity, TKey>.DeleteManyAsync(ids ?? Array.Empty<TKey>(), ct);
        }
        else
        {
            deleted = await Data<TEntity, TKey>.DeleteManyAsync(ids ?? Array.Empty<TKey>(), ct);
        }
        Response.Headers["Sora-Write-Capabilities"] = writes.Writes.ToString();
        return Ok(new { deleted });
    }

    // DELETE ?q= (remove by string query when supported)
    [HttpDelete("")]
    public virtual async Task<IActionResult> DeleteByQuery([FromQuery] string? q, CancellationToken ct)
    {
        if (!CanRemove) return Forbid();
        if (string.IsNullOrWhiteSpace(q)) return BadRequest();
        // Optional set routing via querystring (?set=)
        var set = HttpContext.Request.Query.TryGetValue("set", out var sVal) ? sVal.ToString() : null;
        // Use static helper; will throw if string queries unsupported
        try
        {
            // Placeholder kept for future batch-based delete-by-query optimization
            // var _ = await Sora.Data.Core.Data<TEntity, TKey>.Batch().SaveAsync(ct: ct);
        }
        catch
        {
            // Fallback implementation using repository directly
        }

        // Implement via domain static helper (set-aware via ambient context)
        int removed;
        if (!string.IsNullOrWhiteSpace(set))
        {
            using var _set = Sora.Data.Core.DataSetContext.With(set);
            removed = await Entity<TEntity, TKey>.Remove(q!, ct);
        }
        else
        {
            removed = await Entity<TEntity, TKey>.Remove(q!, ct);
        }
        return Ok(new { deleted = removed });
    }

    // DELETE /all (remove entire collection)
    [HttpDelete("all")]
    public virtual async Task<IActionResult> DeleteAll(CancellationToken ct)
    {
        if (!CanRemove) return Forbid();
        // Optional set routing via querystring (?set=)
        var set = HttpContext.Request.Query.TryGetValue("set", out var sVal) ? sVal.ToString() : null;
        int deleted;
        if (!string.IsNullOrWhiteSpace(set))
        {
            using var _set = Sora.Data.Core.DataSetContext.With(set);
            deleted = await Entity<TEntity, TKey>.RemoveAll(ct);
        }
        else
        {
            deleted = await Entity<TEntity, TKey>.RemoveAll(ct);
        }
        return Ok(new { deleted });
    }

    [HttpPatch("{id}")]
    public virtual async Task<IActionResult> Patch([FromRoute] TKey id, [FromBody] JsonPatchDocument<TEntity> patch, CancellationToken ct)
    {
        if (!CanWrite) return Forbid();
        var repo = HttpContext.RequestServices.GetRequiredService<IDataService>().GetRepository<TEntity, TKey>();
        var caps = Capabilities(repo);
        var opts = BuildOptions();
        var ctx = new HookContext<TEntity> { Http = HttpContext, Services = HttpContext.RequestServices, Options = opts, Capabilities = caps, Ct = ct };
        var runner = GetRunner();

    // Optional set routing via querystring (?set=)
    var set = HttpContext.Request.Query.TryGetValue("set", out var sVal) ? sVal.ToString() : null;

        await runner.BeforePatchAsync(ctx, id?.ToString() ?? string.Empty, patch);

    using var _set = Sora.Data.Core.DataSetContext.With(string.IsNullOrWhiteSpace(set) ? null : set);
    var original = await Data<TEntity, TKey>.GetAsync(id!, ct);
        if (original == null) return NotFound();
        var working = await Data<TEntity, TKey>.GetAsync(id!, ct);
        patch.ApplyTo(working!);
        // Ensure id consistency
        var idProp = typeof(TEntity).GetProperty("Id");
    if (idProp != null)
        {
            var newId = idProp.GetValue(working);
            if (newId is not null && !Equals(newId, id)) return Conflict();
        }
        await runner.BeforeSaveAsync(ctx, working!);
    var saved = await working!.Upsert<TEntity, TKey>(ct);
        await runner.AfterPatchAsync(ctx, saved);

        // Accept/view header
        var accept = HttpContext.Request.Headers["Accept"].ToString();
        if (!string.IsNullOrWhiteSpace(opts.View)) Response.Headers["Sora-View"] = opts.View!;
        else if (!string.IsNullOrWhiteSpace(accept)) Response.Headers["Sora-View"] = ParseViewFromAccept(accept) ?? "full";

        var (replaced, transformed) = await runner.EmitModelAsync(ctx, saved);
        foreach (var kv in ctx.ResponseHeaders) Response.Headers[kv.Key] = kv.Value;
        return PrepareResponse(transformed);
    }

    protected virtual string? ParseViewFromAccept(string accept)
    {
        // Minimal v1: parse view= param if present
        // e.g., application/json;view=compact
        try
        {
            var parts = accept.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var p in parts)
            {
                var kv = p.Split('=', 2, StringSplitOptions.TrimEntries);
                if (kv.Length == 2 && kv[0].Equals("view", StringComparison.OrdinalIgnoreCase)) return kv[1].Trim('"');
            }
        }
        catch { }
        return null;
    }
}

public abstract class EntityController<TEntity> : EntityController<TEntity, string>
    where TEntity : class, IEntity<string>
{ }
