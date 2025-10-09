using System;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Core.Extensions;
using Koan.Web.Attributes;
using Koan.Web.Endpoints;
using Koan.Web.Hooks;
using Koan.Web.Infrastructure;
using System.Net.Mime;
// using Microsoft.Extensions.Options; // duplicate removed
using Koan.Web.Options;
using Koan.Data.Abstractions.Instructions;

namespace Koan.Web.Controllers;

/// <summary>
/// Generic controller providing CRUD endpoints for any aggregate.
/// Integrates pluggable hooks, pagination, capability headers, and content shaping.
/// </summary>
[ApiController]
public abstract class EntityController<TEntity, TKey> : ControllerBase
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private IEntityEndpointService<TEntity, TKey>? _endpointService;

    protected EntityController()
    {
    }

    protected EntityController(IEntityEndpointService<TEntity, TKey> endpointService)
    {
        _endpointService = endpointService;
    }

    protected virtual bool CanRead => true;
    protected virtual bool CanWrite => true;
    protected virtual bool CanRemove => true;

    protected IEntityEndpointService<TEntity, TKey> EndpointService =>
        _endpointService ?? HttpContext.RequestServices.GetRequiredService<IEntityEndpointService<TEntity, TKey>>();

    private EntityEndpointOptions EndpointOptions => HttpContext.RequestServices.GetRequiredService<IOptions<EntityEndpointOptions>>().Value;

    private EntityRequestContextBuilder ContextBuilder => HttpContext.RequestServices.GetRequiredService<EntityRequestContextBuilder>();

    protected virtual string GetDisplay(TEntity e)
        => e?.ToString() ?? string.Empty;

    private EntityRequestContext CreateRequestContext(QueryOptions options, CancellationToken ct)
        => ContextBuilder.Build(options, ct, HttpContext, HttpContext?.User);

    private IActionResult ResolveShortCircuit(EntityEndpointResult result)
        => result.ShortCircuitResult ?? PrepareResponse(result.ShortCircuitPayload ?? result.Payload);

    private static Dictionary<string, string?> ToQueryDictionary(IQueryCollection query)
    {
        var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in query)
        {
            dict[kv.Key] = kv.Value.ToString();
        }
        return dict;
    }

    private string? GetRelationshipParameter(IQueryCollection query)
    {
        if (!EndpointOptions.AllowRelationshipExpansion) return null;
        return query.TryGetValue("with", out var value) ? value.ToString() : null;
    }

    private string? GetShapeParameter(IQueryCollection query)
    {
        if (!query.TryGetValue("shape", out var value)) return null;
        var shape = value.ToString();
        return EndpointOptions.IsShapeAllowed(shape) ? shape : null;
    }

    private static bool GetBooleanQueryValue(IQueryCollection query, string key)
    {
        return query.TryGetValue(key, out var value) &&
               bool.TryParse(value.ToString(), out var result) && result;
    }

    protected virtual PaginationPolicy GetPaginationPolicy()
    {
        if (HttpContext?.RequestServices is null)
        {
            throw new InvalidOperationException("RequestServices is not available.");
        }

        var services = HttpContext.RequestServices;
        var methodInfo = ControllerContext?.ActionDescriptor?.MethodInfo;

        PaginationAttribute? LocateAttribute()
        {
            var methodAttr = methodInfo?.GetCustomAttribute<PaginationAttribute>();
            if (methodAttr is not null)
            {
                return methodAttr;
            }

            var controllerAttr = GetType().GetCustomAttribute<PaginationAttribute>();
            if (controllerAttr is not null)
            {
                return controllerAttr;
            }

            var legacy = GetType().GetCustomAttribute<KoanDataBehaviorAttribute>();
            if (legacy is not null)
            {
                return new PaginationAttribute
                {
                    Mode = legacy.MustPaginate ? PaginationMode.Required : PaginationMode.On,
                    DefaultSize = legacy.DefaultPageSize,
                    MaxSize = legacy.MaxPageSize,
                    IncludeCount = true
                };
            }

            return null;
        }

        var attr = LocateAttribute();
        return PaginationPolicy.Resolve(services, attr);
    }

    protected virtual QueryOptions BuildOptions()
    {
        var query = HttpContext.Request.Query;
        var defaults = EndpointOptions;

        var opts = new QueryOptions
        {
            Page = 1,
            PageSize = defaults.DefaultPageSize,
            View = defaults.DefaultView
        };

        if (query.TryGetValue("q", out var vq))
        {
            opts.Q = vq.FirstOrDefault();
        }

        if (query.TryGetValue("page", out var vp) && int.TryParse(vp, out var page) && page > 0)
        {
            opts.Page = page;
        }

        var maxSize = defaults.MaxPageSize;
        if (query.TryGetValue("pageSize", out var vps) && int.TryParse(vps, out var requested) && requested > 0)
        {
            opts.PageSize = Math.Min(requested, maxSize);
        }
        else if (query.TryGetValue("size", out var vs) && int.TryParse(vs, out var size) && size > 0)
        {
            opts.PageSize = Math.Min(size, maxSize);
        }

        if (query.TryGetValue("sort", out var vsort))
        {
            foreach (var spec in vsort.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var desc = spec.StartsWith('-');
                var field = desc ? spec[1..] : spec;
                if (!string.IsNullOrWhiteSpace(field))
                {
                    opts.Sort.Add(new SortSpec(field, desc));
                }
            }
        }

        if (query.TryGetValue("dir", out var vdir) && opts.Sort.Count == 1)
        {
            var direction = vdir.ToString();
            if (string.Equals(direction, "desc", StringComparison.OrdinalIgnoreCase))
            {
                opts.Sort[0] = new SortSpec(opts.Sort[0].Field, true);
            }
        }

        if (query.TryGetValue("output", out var vout))
        {
            var shape = vout.ToString();
            if (defaults.IsShapeAllowed(shape))
            {
                opts.Shape = shape;
            }
        }

        if (query.TryGetValue("view", out var vview))
        {
            opts.View = vview.ToString();
        }
        else if (string.IsNullOrWhiteSpace(opts.View))
        {
            opts.View = defaults.DefaultView;
        }

        return opts;
    }

    protected virtual IQueryable<TEntity> ApplySort(IQueryable<TEntity> query, QueryOptions opts)
    {
        return query;
    }

    protected virtual ObjectResult PrepareResponse(object? content)
    {
        Response.Headers["Vary"] = "Accept";
        Response.Headers["Koan-Access-Read"] = CanRead.ToString().ToLowerInvariant();
        Response.Headers["Koan-Access-Write"] = CanWrite.ToString().ToLowerInvariant();
        Response.Headers["Koan-Access-Remove"] = CanRemove.ToString().ToLowerInvariant();
        return new ObjectResult(content);
    }

    private void ApplyResponseMetadata(EntityEndpointResult result)
    {
        foreach (var header in result.Headers)
        {
            Response.Headers[header.Key] = header.Value;
        }
    }

    [HttpGet("")]
    [Produces(MediaTypeNames.Application.Json)]
    public virtual async Task<IActionResult> GetCollection(CancellationToken ct)
    {
        if (!CanRead) return Forbid();

        var query = HttpContext.Request.Query;
        var policy = GetPaginationPolicy();
        var paginationRequested = query.ContainsKey("page") || query.ContainsKey("pageSize") || query.ContainsKey("size");
        var clientRequestedAll = GetBooleanQueryValue(query, "all");

        int page = 1;
        if (query.TryGetValue("page", out var vp) && int.TryParse(vp, out var parsedPage))
        {
            if (parsedPage < 0)
            {
                return BadRequest(new { error = "page must be >= 0" });
            }

            page = parsedPage < 1 ? 1 : parsedPage;
        }

        int requestedSize = policy.DefaultSize;
        if (query.TryGetValue("pageSize", out var vps) && int.TryParse(vps, out var parsedSize))
        {
            requestedSize = parsedSize;
        }
        else if (query.TryGetValue("size", out var vs) && int.TryParse(vs, out var legacySize))
        {
            requestedSize = legacySize;
        }

        if (requestedSize < 1)
        {
            requestedSize = policy.DefaultSize;
        }

        var pageSize = Math.Min(requestedSize, policy.MaxSize);

        var applyPagination = policy.Mode switch
        {
            PaginationMode.On => true,
            PaginationMode.Required => true,
            PaginationMode.Optional => paginationRequested && !clientRequestedAll,
            PaginationMode.Off => false,
            _ => true
        };

        var options = BuildOptions();
        options.Page = applyPagination ? page : 0;
        options.PageSize = applyPagination ? pageSize : 0;

        if (!string.IsNullOrWhiteSpace(policy.DefaultSort) && options.Sort.Count == 0)
        {
            foreach (var spec in policy.DefaultSort.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var desc = spec.StartsWith('-');
                var field = desc ? spec[1..] : spec;
                if (!string.IsNullOrWhiteSpace(field))
                {
                    options.Sort.Add(new SortSpec(field, desc));
                }
            }
        }

        var context = CreateRequestContext(options, ct);
        var request = new EntityCollectionRequest
        {
            Context = context,
            FilterJson = query.TryGetValue("filter", out var f) ? f.ToString() : null,
            Set = query.TryGetValue("set", out var setVal) ? setVal.ToString() : null,
            IgnoreCase = GetBooleanQueryValue(query, "ignoreCase"),
            With = GetRelationshipParameter(query),
            Shape = GetShapeParameter(query) ?? options.Shape,
            ForcePagination = applyPagination,
            ApplyPagination = applyPagination,
            PaginationRequested = paginationRequested,
            ClientRequestedAll = clientRequestedAll,
            Policy = policy,
            IncludeTotalCount = applyPagination && policy.IncludeCount,
            AbsoluteMaxRecords = policy.AbsoluteMaxRecords,
            Accept = HttpContext.Request.Headers["Accept"].ToString(),
            BasePath = HttpContext.Request.Path.HasValue ? HttpContext.Request.Path.Value : "/",
            QueryParameters = ToQueryDictionary(query)
        };

        var result = await EndpointService.GetCollectionAsync(request);
        ApplyResponseMetadata(result);

        if (result.IsShortCircuited)
        {
            return ResolveShortCircuit(result);
        }

        return PrepareResponse(result.Payload ?? result.Items);
    }

    // POST /query
    [HttpPost("query")]
    [Produces(MediaTypeNames.Application.Json)]
    public virtual async Task<IActionResult> Query([FromBody] object body, CancellationToken ct)
    {
        if (!CanRead) return Forbid();

        var options = BuildOptions();
        var context = CreateRequestContext(options, ct);

        string? filterJson = null;
        string? set = null;
        bool ignoreCase = false;
        try
        {
            var jobj = JObject.Parse(JsonConvert.SerializeObject(body));
            if (jobj.TryGetValue("filter", out var f)) filterJson = f.ToString(Formatting.None);
            if (jobj.TryGetValue("page", out var p) && (p.Type == JTokenType.Integer || p.Type == JTokenType.String)) options.Page = (int)p;
            if (jobj.TryGetValue("size", out var s) && (s.Type == JTokenType.Integer || s.Type == JTokenType.String)) options.PageSize = (int)s;
            if (jobj.TryGetValue("set", out var st) && st.Type == JTokenType.String) set = st.ToString();
            if (jobj.TryGetValue("", out var opt) && opt.Type == JTokenType.Object)
            {
                var o = (JObject)opt;
                if (o.TryGetValue("ignoreCase", out var ic) && ic.Type == JTokenType.Boolean && (bool)ic) ignoreCase = true;
            }
        }
        catch
        {
        }

        var request = new EntityQueryRequest
        {
            Context = context,
            FilterJson = filterJson,
            Set = set,
            IgnoreCase = ignoreCase,
            Accept = HttpContext.Request.Headers["Accept"].ToString()
        };

        var result = await EndpointService.QueryAsync(request);
        ApplyResponseMetadata(result);
        if (result.IsShortCircuited)
        {
            return ResolveShortCircuit(result);
        }
        return PrepareResponse(result.Payload ?? result.Items);
    }

    [HttpGet("new")]
    public virtual async Task<IActionResult> GetNew(CancellationToken ct)
    {
        if (!CanRead) return Forbid();
        var options = BuildOptions();
        var context = CreateRequestContext(options, ct);
        var request = new EntityGetNewRequest
        {
            Context = context,
            Accept = HttpContext.Request.Headers["Accept"].ToString()
        };
        var result = await EndpointService.GetNewAsync(request);
        ApplyResponseMetadata(result);
        if (result.IsShortCircuited) return ResolveShortCircuit(result);
        return PrepareResponse(result.Payload ?? result.Model);
    }

    [HttpGet("{id}")]
    public virtual async Task<IActionResult> GetById([FromRoute] TKey id, CancellationToken ct)
    {
        if (!CanRead) return Forbid();
        var options = BuildOptions();
        var context = CreateRequestContext(options, ct);
        var query = HttpContext.Request.Query;
        var request = new EntityGetByIdRequest<TKey>
        {
            Context = context,
            Id = id,
            Set = query.TryGetValue("set", out var setVal) ? setVal.ToString() : null,
            With = GetRelationshipParameter(query),
            Accept = HttpContext.Request.Headers["Accept"].ToString()
        };
        var result = await EndpointService.GetByIdAsync(request);
        ApplyResponseMetadata(result);
        if (result.IsShortCircuited) return ResolveShortCircuit(result);
        return PrepareResponse(result.Payload ?? result.Model);
    }

    [HttpPost("")]
    public virtual async Task<IActionResult> Upsert([FromBody][ValidateNever] TEntity model, CancellationToken ct)
    {
        if (model is null) return BadRequest(new { error = "Request body is required" });
        if (!CanWrite) return Forbid();
        var options = BuildOptions();
        var context = CreateRequestContext(options, ct);
        var query = HttpContext.Request.Query;
        var request = new EntityUpsertRequest<TEntity>
        {
            Context = context,
            Model = model,
            Set = query.TryGetValue("set", out var setVal) ? setVal.ToString() : null,
            Accept = HttpContext.Request.Headers["Accept"].ToString()
        };
        var result = await EndpointService.UpsertAsync(request);
        ApplyResponseMetadata(result);
        if (result.IsShortCircuited) return ResolveShortCircuit(result);
        return PrepareResponse(result.Payload ?? result.Model);
    }

    [HttpPost("bulk")]
    public virtual async Task<IActionResult> UpsertMany([FromBody][ValidateNever] IEnumerable<TEntity> models, CancellationToken ct)
    {
        if (models is null) return BadRequest(new { error = "Request body is required" });
        if (!CanWrite) return Forbid();
        var list = models.ToList();
        var options = BuildOptions();
        var context = CreateRequestContext(options, ct);
        var query = HttpContext.Request.Query;
        var request = new EntityUpsertManyRequest<TEntity>
        {
            Context = context,
            Models = list,
            Set = query.TryGetValue("set", out var setVal) ? setVal.ToString() : null
        };
        var result = await EndpointService.UpsertManyAsync(request);
        ApplyResponseMetadata(result);
        if (result.IsShortCircuited) return ResolveShortCircuit(result);
        return PrepareResponse(result.Payload);
    }

    [HttpDelete("{id}")]
    public virtual async Task<IActionResult> Delete([FromRoute] TKey id, CancellationToken ct)
    {
        if (!CanRemove) return Forbid();
        var options = BuildOptions();
        var context = CreateRequestContext(options, ct);
        var query = HttpContext.Request.Query;
        var request = new EntityDeleteRequest<TKey>
        {
            Context = context,
            Id = id,
            Set = query.TryGetValue("set", out var setVal) ? setVal.ToString() : null,
            Accept = HttpContext.Request.Headers["Accept"].ToString()
        };
        var result = await EndpointService.DeleteAsync(request);
        ApplyResponseMetadata(result);
        if (result.IsShortCircuited) return ResolveShortCircuit(result);
        return PrepareResponse(result.Payload ?? result.Model);
    }

    [HttpDelete("bulk")]
    public virtual async Task<IActionResult> DeleteMany([FromBody] IEnumerable<TKey> ids, CancellationToken ct)
    {
        if (!CanRemove) return Forbid();
        var list = ids?.ToList() ?? new List<TKey>();
        var options = BuildOptions();
        var context = CreateRequestContext(options, ct);
        var query = HttpContext.Request.Query;
        var request = new EntityDeleteManyRequest<TKey>
        {
            Context = context,
            Ids = list,
            Set = query.TryGetValue("set", out var setVal) ? setVal.ToString() : null
        };
        var result = await EndpointService.DeleteManyAsync(request);
        ApplyResponseMetadata(result);
        if (result.IsShortCircuited) return ResolveShortCircuit(result);
        return PrepareResponse(result.Payload);
    }

    [HttpDelete("")]
    public virtual async Task<IActionResult> DeleteByQuery([FromQuery] string? q, CancellationToken ct)
    {
        if (!CanRemove) return Forbid();
        if (string.IsNullOrWhiteSpace(q)) return BadRequest();
        var options = BuildOptions();
        var context = CreateRequestContext(options, ct);
        var query = HttpContext.Request.Query;
        var request = new EntityDeleteByQueryRequest
        {
            Context = context,
            Query = q!,
            Set = query.TryGetValue("set", out var setVal) ? setVal.ToString() : null
        };
        var result = await EndpointService.DeleteByQueryAsync(request);
        ApplyResponseMetadata(result);
        if (result.IsShortCircuited) return ResolveShortCircuit(result);
        return PrepareResponse(result.Payload);
    }

    [HttpDelete("all")]
    public virtual async Task<IActionResult> DeleteAll(CancellationToken ct)
    {
        if (!CanRemove) return Forbid();
        var options = BuildOptions();
        var context = CreateRequestContext(options, ct);
        var query = HttpContext.Request.Query;
        var request = new EntityDeleteAllRequest
        {
            Context = context,
            Set = query.TryGetValue("set", out var setVal) ? setVal.ToString() : null
        };
        var result = await EndpointService.DeleteAllAsync(request);
        ApplyResponseMetadata(result);
        if (result.IsShortCircuited) return ResolveShortCircuit(result);
        return PrepareResponse(result.Payload);
    }

    [HttpPatch("{id}")]
    [Consumes(Infrastructure.KoanWebConstants.ContentTypes.ApplicationJsonPatch)]
    public virtual async Task<IActionResult> PatchJsonPatch([FromRoute] TKey id, [FromBody] JsonPatchDocument<TEntity> body, CancellationToken ct)
        => await PatchNormalized(id, NormalizeFromJsonPatch(id, body), ct);

    [HttpPatch("{id}")]
    [Consumes(Infrastructure.KoanWebConstants.ContentTypes.ApplicationMergePatch)]
    public virtual async Task<IActionResult> PatchMerge([FromRoute] TKey id, [FromBody] Newtonsoft.Json.Linq.JToken body, CancellationToken ct)
        => await PatchNormalized(id, NormalizeFromMergePatch(id, body), ct);

    [HttpPatch("{id}")]
    [Consumes(Infrastructure.KoanWebConstants.ContentTypes.ApplicationJson)]
    public virtual async Task<IActionResult> PatchPartial([FromRoute] TKey id, [FromBody] Newtonsoft.Json.Linq.JToken body, CancellationToken ct)
        => await PatchNormalized(id, NormalizeFromPartialJson(id, body), ct);

    private async Task<IActionResult> PatchNormalized(TKey id, Koan.Data.Abstractions.Instructions.PatchPayload<TKey> payload, CancellationToken ct)
    {
        if (!CanWrite) return Forbid();
        var options = BuildOptions();
        var context = CreateRequestContext(options, ct);
        var query = HttpContext.Request.Query;
        var set = query.TryGetValue(Koan.Web.Infrastructure.KoanWebConstants.Query.Set, out var setVal) ? setVal.ToString() : null;
        using var _ = EntityContext.With(partition: string.IsNullOrWhiteSpace(set) ? null : set);

        // Validate route id vs payload id (defense-in-depth if future clients send id in payload)
        if (payload.Id is not null && !Equals(payload.Id, id))
        {
            return BadRequest(new { code = Koan.Web.Infrastructure.KoanWebConstants.Codes.Patch.IdMismatch, message = "Route id does not match payload id." });
        }

        // Apply per-request null policy overrides via querystring (optional DX sugar)
        // ?nulls=default|null|ignore|reject or granular: ?mergeNulls=default|reject, ?partialNulls=null|ignore|reject
        var optsOverride = TryBuildPatchOptionsOverride(query);
        if (optsOverride is not null)
        {
            payload = payload with { Options = optsOverride };
        }

        var updated = await Koan.Data.Core.Data<TEntity, TKey>.PatchAsync(payload, ct);
        if (updated is null) return NotFound();

        var request = new EntityGetByIdRequest<TKey>
        {
            Context = context,
            Id = id,
            Set = set,
            Accept = HttpContext.Request.Headers["Accept"].ToString()
        };
        var result = await EndpointService.GetByIdAsync(request);
        ApplyResponseMetadata(result);
        if (result.IsShortCircuited) return ResolveShortCircuit(result);
        return PrepareResponse(result.Payload ?? result.Model);
    }

    private Koan.Data.Abstractions.Instructions.PatchPayload<TKey> NormalizeFromJsonPatch(TKey id, JsonPatchDocument<TEntity> doc)
    {
        var ops = doc.Operations.Select(o => new Koan.Data.Abstractions.Instructions.PatchOp(
            o.op,
            o.path,
            o.from,
            o.value is null ? null : Newtonsoft.Json.Linq.JToken.FromObject(o.value))
        ).ToList();
        return new Koan.Data.Abstractions.Instructions.PatchPayload<TKey>(id, null, null, "json-patch", ops, BuildPatchOptions());
    }

    private Koan.Data.Abstractions.Instructions.PatchPayload<TKey> NormalizeFromMergePatch(TKey id, Newtonsoft.Json.Linq.JToken body)
        => NormalizeObjectToOps(id, body, kindHint: "merge-patch", mergeSemantics: true);

    private Koan.Data.Abstractions.Instructions.PatchPayload<TKey> NormalizeFromPartialJson(TKey id, Newtonsoft.Json.Linq.JToken body)
        => NormalizeObjectToOps(id, body, kindHint: "partial-json", mergeSemantics: false);

    private Koan.Data.Abstractions.Instructions.PatchPayload<TKey> NormalizeObjectToOps(TKey id, Newtonsoft.Json.Linq.JToken body, string kindHint, bool mergeSemantics)
    {
        var ops = new List<Koan.Data.Abstractions.Instructions.PatchOp>();
        void Walk(Newtonsoft.Json.Linq.JToken token, string basePath)
        {
            if (token is Newtonsoft.Json.Linq.JObject obj)
            {
                foreach (var p in obj.Properties())
                {
                    var path = basePath + "/" + p.Name;
                    if (p.Value.Type == Newtonsoft.Json.Linq.JTokenType.Object)
                    {
                        Walk(p.Value, path);
                    }
                    else if (p.Value.Type == Newtonsoft.Json.Linq.JTokenType.Null)
                    {
                        if (mergeSemantics)
                            ops.Add(new Koan.Data.Abstractions.Instructions.PatchOp("remove", path, null, null));
                        else
                            ops.Add(new Koan.Data.Abstractions.Instructions.PatchOp("replace", path, null, Newtonsoft.Json.Linq.JValue.CreateNull()));
                    }
                    else
                    {
                        ops.Add(new Koan.Data.Abstractions.Instructions.PatchOp("replace", path, null, p.Value.DeepClone()));
                    }
                }
            }
            else
            {
                // primitives/arrays at root -> replace entire document path
                ops.Add(new Koan.Data.Abstractions.Instructions.PatchOp("replace", basePath, null, token.DeepClone()));
            }
        }

        Walk(body, "");
        // Ensure pointers start at root ('/prop')
        ops = ops.Select(o => o with { Path = o.Path.StartsWith('/') ? o.Path : "/" + o.Path.TrimStart('/') }).ToList();
        return new Koan.Data.Abstractions.Instructions.PatchPayload<TKey>(id, null, null, kindHint, ops, BuildPatchOptions());
    }

    private Koan.Data.Abstractions.Instructions.PatchOptions BuildPatchOptions()
    {
        var opts = HttpContext?.RequestServices?.GetService(typeof(IOptions<Koan.Web.Options.KoanWebOptions>)) as IOptions<Koan.Web.Options.KoanWebOptions>;
        var mergePolicy = opts?.Value.MergePatchNullsForNonNullable ?? Koan.Data.Abstractions.Instructions.MergePatchNullPolicy.SetDefault;
        var partialPolicy = opts?.Value.PartialJsonNulls ?? Koan.Data.Abstractions.Instructions.PartialJsonNullPolicy.SetNull;
        return new Koan.Data.Abstractions.Instructions.PatchOptions(mergePolicy, partialPolicy, Koan.Data.Abstractions.Instructions.ArrayBehavior.Replace);
    }

    private Koan.Data.Abstractions.Instructions.PatchOptions? TryBuildPatchOptionsOverride(Microsoft.AspNetCore.Http.IQueryCollection query)
    {
        // Read global override first
        Koan.Data.Abstractions.Instructions.MergePatchNullPolicy? merge = null;
        Koan.Data.Abstractions.Instructions.PartialJsonNullPolicy? partial = null;

        if (query.TryGetValue(Koan.Web.Infrastructure.KoanWebConstants.Query.Nulls, out var global))
        {
            var val = global.ToString().Trim().ToLowerInvariant();
            switch (val)
            {
                case "default":
                    merge = Koan.Data.Abstractions.Instructions.MergePatchNullPolicy.SetDefault;
                    break;
                case "reject":
                    merge = Koan.Data.Abstractions.Instructions.MergePatchNullPolicy.Reject;
                    partial = Koan.Data.Abstractions.Instructions.PartialJsonNullPolicy.Reject;
                    break;
                case "null":
                    partial = Koan.Data.Abstractions.Instructions.PartialJsonNullPolicy.SetNull;
                    break;
                case "ignore":
                    partial = Koan.Data.Abstractions.Instructions.PartialJsonNullPolicy.Ignore;
                    break;
            }
        }

        if (query.TryGetValue(Koan.Web.Infrastructure.KoanWebConstants.Query.MergeNulls, out var mergeStr))
        {
            var val = mergeStr.ToString().Trim().ToLowerInvariant();
            merge = val == "reject"
                ? Koan.Data.Abstractions.Instructions.MergePatchNullPolicy.Reject
                : Koan.Data.Abstractions.Instructions.MergePatchNullPolicy.SetDefault;
        }

        if (query.TryGetValue(Koan.Web.Infrastructure.KoanWebConstants.Query.PartialNulls, out var partialStr))
        {
            var val = partialStr.ToString().Trim().ToLowerInvariant();
            partial = val switch
            {
                "ignore" => Koan.Data.Abstractions.Instructions.PartialJsonNullPolicy.Ignore,
                "reject" => Koan.Data.Abstractions.Instructions.PartialJsonNullPolicy.Reject,
                _ => Koan.Data.Abstractions.Instructions.PartialJsonNullPolicy.SetNull
            };
        }

        if (merge is null && partial is null) return null;
        // Fill the other policy with current defaults to avoid nulls
        var defaults = BuildPatchOptions();
        return new Koan.Data.Abstractions.Instructions.PatchOptions(
            merge ?? defaults.MergeNulls,
            partial ?? defaults.PartialNulls,
            defaults.Arrays);
    }
}

public abstract class EntityController<TEntity> : EntityController<TEntity, string>
    where TEntity : class, IEntity<string>
{ }






















