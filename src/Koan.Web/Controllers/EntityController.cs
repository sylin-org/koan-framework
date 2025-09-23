using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
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

    private IEntityEndpointService<TEntity, TKey> EndpointService
        => _endpointService ??= HttpContext.RequestServices.GetRequiredService<IEntityEndpointService<TEntity, TKey>>();

    protected virtual string GetDisplay(TEntity e)
    {
        var t = e!.GetType();
        var name = t.GetProperty("Name")?.GetValue(e) as string
                   ?? t.GetProperty("Title")?.GetValue(e) as string
                   ?? t.GetProperty("Label")?.GetValue(e) as string
                   ?? e.ToString();
        return name ?? string.Empty;
    }

    private EntityRequestContext CreateRequestContext(QueryOptions options, CancellationToken ct)
        => new EntityRequestContext(HttpContext.RequestServices, options, ct, HttpContext);

    private static Dictionary<string, string?> ToQueryDictionary(IQueryCollection query)
    {
        var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in query)
        {
            dict[kv.Key] = kv.Value.ToString();
        }
        return dict;
    }

    private bool GetBooleanQueryValue(IQueryCollection query, string key)
    {
        if (!query.TryGetValue(key, out var value)) return false;
        var text = value.ToString();
        return text.Equals("true", StringComparison.OrdinalIgnoreCase)
            || text.Equals("1")
            || text.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || text.Equals("on", StringComparison.OrdinalIgnoreCase);
    }

    private KoanDataBehaviorAttribute? GetBehavior()
        => GetType().GetCustomAttributes(typeof(KoanDataBehaviorAttribute), true).FirstOrDefault() as KoanDataBehaviorAttribute;

    protected virtual QueryOptions BuildOptions()
    {
        var q = HttpContext.Request.Query;
        var opts = new QueryOptions();
        var beh = GetBehavior();
        if (q.TryGetValue("q", out var vq)) opts.Q = vq.FirstOrDefault();
        if (q.TryGetValue("page", out var vp) && int.TryParse(vp, out var p) && p > 0) opts.Page = p; else opts.Page = 1;
        var maxSize = beh?.MaxPageSize ?? KoanWebConstants.Defaults.MaxPageSize;
        var defSize = beh?.DefaultPageSize ?? KoanWebConstants.Defaults.DefaultPageSize;
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

        if (HttpContext.Request.Query.TryGetValue("page", out var _vp)
            && int.TryParse(_vp, out var _p)
            && _p < 0)
        {
            return BadRequest(new { error = "page must be >= 0" });
        }

        var options = BuildOptions();
        var context = CreateRequestContext(options, ct);
        var query = HttpContext.Request.Query;
        var behavior = GetBehavior();

        var request = new EntityCollectionRequest
        {
            Context = context,
            FilterJson = query.TryGetValue("filter", out var f) ? f.ToString() : null,
            Set = query.TryGetValue("set", out var setVal) ? setVal.ToString() : null,
            IgnoreCase = GetBooleanQueryValue(query, "ignoreCase"),
            With = query.TryGetValue("with", out var withVal) ? withVal.ToString() : null,
            Shape = query.TryGetValue("shape", out var shapeVal) ? shapeVal.ToString() : null,
            ForcePagination = behavior?.MustPaginate ?? false,
            Accept = HttpContext.Request.Headers["Accept"].ToString(),
            BasePath = HttpContext.Request.Path.HasValue ? HttpContext.Request.Path.Value : "/",
            QueryParameters = ToQueryDictionary(query)
        };

        var result = await EndpointService.GetCollectionAsync(request);
        ApplyResponseMetadata(result);

        if (result.IsShortCircuited)
        {
            return result.ShortCircuitResult!;
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
            return result.ShortCircuitResult!;
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
        if (result.IsShortCircuited) return result.ShortCircuitResult!;
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
            With = query.TryGetValue("with", out var withVal) ? withVal.ToString() : null,
            Accept = HttpContext.Request.Headers["Accept"].ToString()
        };
        var result = await EndpointService.GetByIdAsync(request);
        ApplyResponseMetadata(result);
        if (result.IsShortCircuited) return result.ShortCircuitResult!;
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
        if (result.IsShortCircuited) return result.ShortCircuitResult!;
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
        if (result.IsShortCircuited) return result.ShortCircuitResult!;
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
        if (result.IsShortCircuited) return result.ShortCircuitResult!;
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
        if (result.IsShortCircuited) return result.ShortCircuitResult!;
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
        if (result.IsShortCircuited) return result.ShortCircuitResult!;
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
        if (result.IsShortCircuited) return result.ShortCircuitResult!;
        return PrepareResponse(result.Payload);
    }

    [HttpPatch("{id}")]
    public virtual async Task<IActionResult> Patch([FromRoute] TKey id, [FromBody] JsonPatchDocument<TEntity> patch, CancellationToken ct)
    {
        if (!CanWrite) return Forbid();
        var options = BuildOptions();
        var context = CreateRequestContext(options, ct);
        var query = HttpContext.Request.Query;
        var request = new EntityPatchRequest<TEntity, TKey>
        {
            Context = context,
            Id = id,
            Patch = patch,
            Set = query.TryGetValue("set", out var setVal) ? setVal.ToString() : null,
            Accept = HttpContext.Request.Headers["Accept"].ToString()
        };
        var result = await EndpointService.PatchAsync(request);
        ApplyResponseMetadata(result);
        if (result.IsShortCircuited) return result.ShortCircuitResult!;
        return PrepareResponse(result.Payload ?? result.Model);
    }
}

public abstract class EntityController<TEntity> : EntityController<TEntity, string>
    where TEntity : class, IEntity<string>
{ }





