using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Koan.Data.Core.Model;
using Koan.Data.Core.Querying;
using Koan.Web.Attributes;
using Koan.Web.Authorization;
using Koan.Web.Endpoints;
using Koan.Web.Hooks;
using Koan.Web.Infrastructure;
using System.Net.Mime;
using Koan.Web.Options;
using Koan.Data.Abstractions.Instructions;
using Koan.Web.Queries;
using Koan.Web.PatchOps;

namespace Koan.Web.Controllers;

/// <summary>
/// Generic controller providing CRUD endpoints for any aggregate.
/// Integrates pluggable hooks, pagination, capability headers, and content shaping. A derived controller that implements
/// <see cref="IModelHook{TEntity}"/>, <see cref="IRequestOptionsHook{TEntity}"/>, <see cref="ICollectionHook{TEntity}"/> or
/// <see cref="IEmitHook{TEntity}"/> runs as that hook for its own requests (WEB-0087); one marked <see cref="DeclaredAttribute"/> serves only
/// the operations it overrides; a request naming a partition it does not serve (<see cref="PartitionsAttribute"/>) is refused.
/// </summary>
[ApiController]
public abstract class EntityController<TEntity, TKey> : ControllerBase
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private IEntityEndpointService<TEntity, TKey>? _endpointService;
    private EntityOperation? _operation;
    private object? _input;
    private static readonly ConcurrentDictionary<Type, bool> MapsKeys = new();

    protected EntityController()
    {
    }

    protected EntityController(IEntityEndpointService<TEntity, TKey> endpointService)
    {
        _endpointService = endpointService;
    }

    // ARCH-0092 (§D): the CanRead/CanWrite/CanRemove virtuals were hard-cut — authorization is no longer a
    // REST-only controller toggle. Base CRUD is gated by the unified IAuthorize seam inside the shared
    // IEntityEndpointService (so REST and MCP enforce the same declaration). Declare access on the entity with
    // standard [Authorize]/[AllowAnonymous] + [RequireScope].

    protected IEntityEndpointService<TEntity, TKey> EndpointService =>
        _endpointService ?? HttpContext.RequestServices.GetRequiredService<IEntityEndpointService<TEntity, TKey>>();

    private EntityEndpointOptions EndpointOptions => HttpContext.RequestServices.GetRequiredService<IOptions<EntityEndpointOptions>>().Value;

    private EntityRequestContextBuilder ContextBuilder => HttpContext.RequestServices.GetRequiredService<EntityRequestContextBuilder>();

    /// <summary>The collection view used only when the request omits an explicit view.</summary>
    protected virtual string DefaultCollectionView => EndpointOptions.DefaultView;

    protected virtual string GetDisplay(TEntity e)
        => e?.ToString() ?? "";

    private EntityRequestContext CreateRequestContext(QueryOptions options, CancellationToken ct)
    {
        var context = ContextBuilder.Build(options, ct, HttpContext, HttpContext?.User);
        // WEB-0087: this controller runs as a hook of its own requests, and its hooks read what the operation took.
        context.Owner = this;
        context.Input = _input;
        return context;
    }

    /// <summary>What the operation answering this request serves and takes (WEB-0087).</summary>
    private EntityOperation Operation
        => _operation ??= EntityOperation.Of((ControllerContext?.ActionDescriptor as ControllerActionDescriptor)?.MethodInfo, GetType());

    /// <summary>
    /// The id of the row an address names (WEB-0087). <c>GetById</c>, <c>Patch</c> and <c>Delete</c> ask it with their key segment, as the
    /// route gave it, or <c>""</c> for a singleton that routes them at <c>""</c>, and then read the row by that id with every filter, as a read
    /// by id does; null answers 404. Left alone the segment is the id. Override it to answer at another address of the row, such as a handle
    /// or a slug, or to name the one row of a singleton.
    /// </summary>
    protected virtual ValueTask<TKey?> KeyOf(string segment, CancellationToken ct)
    {
        if (typeof(TKey) == typeof(string)) return ValueTask.FromResult((TKey?)(object)segment);
        try
        {
            return ValueTask.FromResult((TKey?)TypeDescriptor.GetConverter(typeof(TKey)).ConvertFromInvariantString(segment));
        }
        catch (Exception error) when (error is FormatException or NotSupportedException or ArgumentException)
        {
            return ValueTask.FromResult(default(TKey?));
        }
    }

    /// <summary>The row an address names: its segment where <see cref="KeyOf"/> is left alone, otherwise what it answers.</summary>
    private async ValueTask<(bool Found, TKey Key)> Keyed(TKey id, CancellationToken ct)
    {
        var maps = MapsKeys.GetOrAdd(GetType(), static type => type.GetMethod(nameof(KeyOf), BindingFlags.Instance | BindingFlags.NonPublic,
            [typeof(string), typeof(CancellationToken)])?.DeclaringType != typeof(EntityController<TEntity, TKey>));
        if (!maps) return (true, id);
        var key = await KeyOf(id is null ? "" : Convert.ToString(id, CultureInfo.InvariantCulture) ?? "", ct);
        return key is null ? (false, default!) : (true, key);
    }

    /// <summary>
    /// What a request is refused with before anything else is read (WEB-0087), or null where it is admitted: a partition this controller does
    /// not serve; on a declared controller, a query word, or a word of a body that is all words, the operation does not take; and a value of
    /// the declared input that cannot be one. What it admits of the input is what this request's hooks read.
    /// </summary>
    private IActionResult? Admit(JObject? body = null, bool bodyIsWords = false)
    {
        var query = HttpContext.Request.Query;
        // Each place a request can name a partition is judged, since a query reads the one its body names over the one its address names.
        if (query.TryGetValue(KoanWebConstants.Query.Set, out var named) && PartitionsAttribute.Refusal(GetType(), named.ToString()) is { } unserved)
            return this.Refused(unserved);
        if (bodyIsWords && body?[KoanWebConstants.Query.Set] is JValue { Type: JTokenType.String } inBody
            && PartitionsAttribute.Refusal(GetType(), inBody.ToString()) is { } unservedInBody)
            return this.Refused(unservedInBody);

        var operation = Operation;
        if (operation.Declared)
        {
            foreach (var word in query.Keys)
                if (!operation.Takes(word)) return this.Refused(EntityOperation.NotTaken(word));
            if (bodyIsWords && body is not null)
                foreach (var word in body.Properties())
                    if (!operation.Takes(word.Name)) return this.Refused(EntityOperation.NotTaken(word.Name));
        }

        return operation.Input is null ? null : BindInput(operation, body);
    }

    /// <summary>The declared input, each member from the body where it names it and otherwise from the query.</summary>
    private IActionResult? BindInput(EntityOperation operation, JObject? body)
    {
        var query = HttpContext.Request.Query;
        var serializer = JsonSerializer.Create(HttpContext.RequestServices.GetRequiredService<IOptions<MvcNewtonsoftJsonOptions>>().Value.SerializerSettings);
        var values = new JObject();
        foreach (var member in operation.InputMembers)
        {
            var name = EntityOperation.WireName(member);
            var many = member.PropertyType != typeof(string) && typeof(System.Collections.IEnumerable).IsAssignableFrom(member.PropertyType);
            var token = body?.GetValue(name, StringComparison.OrdinalIgnoreCase)
                        ?? (query.TryGetValue(name, out var given)
                            ? many ? new JArray(given.Select(value => (JToken)new JValue(value))) : new JValue(given.ToString())
                            : null);
            if (token is null || token.Type == JTokenType.Null) continue;
            try
            {
                token.ToObject(member.PropertyType, serializer);
            }
            catch (Exception error) when (error is JsonException or FormatException or InvalidCastException or ArgumentException or OverflowException)
            {
                return this.Refused(Reason.Invalid(name, $"'{name}' cannot take this value."));
            }
            values[member.Name] = token;
        }

        _input = values.ToObject(operation.Input!, serializer);
        return null;
    }

    /// <summary>The members a body names that this write writes, or the refusal of the first it does not write (WEB-0087); an input member is
    /// the input's, not the entity's.</summary>
    private (IActionResult? Refused, IReadOnlyCollection<string>? Writes) Written(EntityOperation operation, IEnumerable<string> names)
    {
        if (operation.Writes is null) return (null, null);
        var writes = new List<string>();
        foreach (var name in names)
        {
            if (operation.Written(name) is { } member) writes.Add(member.Name);
            else if (!operation.InputMembers.Any(input => EntityOperation.Named(input, name)))
                return (this.Refused(Reason.Invalid(name, $"This address does not write '{name}'.")), null);
        }
        return (null, writes);
    }

    /// <summary>A declared write's members as it sent them (WEB-0088): a page's form as the object of its fields, each typed as the member
    /// it names, or a value that cannot be its member refused with its field; otherwise the JSON body.</summary>
    private async Task<(JObject? Words, IActionResult? Refused)> Sent(EntityOperation operation, CancellationToken ct)
    {
        if (!HttpContext.Request.HasFormContentType) return (await SentBody(ct), null);
        var form = await HttpContext.Request.ReadFormAsync(ct);
        var serializer = JsonSerializer.Create(HttpContext.RequestServices.GetRequiredService<IOptions<MvcNewtonsoftJsonOptions>>().Value.SerializerSettings);
        var (words, unfit) = SentForm.Words(form, typeof(TEntity), operation.InputMembers, serializer);
        return unfit is null ? (words, null) : (null, this.Refused(unfit));
    }

    /// <summary>
    /// The address a row this controller wrote answers at, as a path under the application's base (WEB-0088): where a committed write's
    /// <c>Location</c> points and a form is sent on to when it names no <c>return</c>. Left alone it is the address the write was sent to
    /// where that address named the row, and otherwise that address followed by the row's id, as a create's is. Override it where the row
    /// answers at another address, such as its slug's.
    /// </summary>
    protected virtual string AddressOf(TEntity row)
    {
        var sent = HttpContext.Request.Path.ToUriComponent();
        var created = (ControllerContext?.ActionDescriptor as ControllerActionDescriptor)?.MethodInfo.GetBaseDefinition().Name == nameof(Upsert);
        return created ? sent.TrimEnd('/') + "/" + Uri.EscapeDataString(Convert.ToString(row.Id, CultureInfo.InvariantCulture) ?? "") : sent;
    }

    /// <summary>What a single write answers once it committed: on a declared controller through <see cref="Answers.Written"/> (WEB-0088), at
    /// the written row's address, or for a delete at the address the row was listed under.</summary>
    private IActionResult Answered(EntityModelResult<TEntity> result, bool created, bool deleted = false)
    {
        var body = result.Payload ?? result.Model;
        if (!Operation.Declared) return PrepareResponse(body);
        var address = deleted || result.Model is null ? Listed() : AddressOf(result.Model);
        return this.Written(HttpContext.Request.PathBase.ToUriComponent() + address, body, created);
    }

    /// <summary>The address the row a request names is listed under: the request's own, less its last segment.</summary>
    private string Listed()
    {
        var sent = HttpContext.Request.Path.ToUriComponent().TrimEnd('/');
        var last = sent.LastIndexOf('/');
        return last <= 0 ? "/" : sent[..last];
    }

    /// <summary>A declared create's body as it was sent, kept readable by its buffering (WEB-0087).</summary>
    private async Task<JObject?> SentBody(CancellationToken ct)
    {
        var body = HttpContext.Request.Body;
        if (!body.CanSeek) throw new InvalidOperationException("A declared create's body was not kept readable after binding.");
        body.Position = 0;
        using var reader = new System.IO.StreamReader(body, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var raw = await reader.ReadToEndAsync(ct);
        body.Position = 0;
        return string.IsNullOrWhiteSpace(raw) ? null : JToken.Parse(raw) as JObject;
    }

    private IActionResult ResolveShortCircuit(EntityEndpointResult result)
    {
        // ARCH-0092 (§D): a seam denial is carried as a transport-agnostic AuthorizeDecision; it answers here as every gate's does
        // (WEB-0086): a refusal with its reason, otherwise the one sentence for its kind.
        if (result.DeniedDecision is { } decision) return Answers.Denied(HttpContext, decision);

        return result.ShortCircuitResult ?? PrepareResponse(result.ShortCircuitPayload ?? result.Payload);
    }

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

        // The operation's [Pagination], as the route tool reads it, so both doors state and serve one bound.
        return PaginationPolicy.Resolve(HttpContext.RequestServices, Operation.Pagination);
    }

    protected virtual QueryOptions BuildOptions()
    {
        // Delegate to static parser for pure query transformation. Sort fields are resolved against TEntity here;
        // unresolvable fields throw InvalidSortFieldException, which the controller actions convert to 400.
        return EntityQueryParser.Parse<TEntity>(HttpContext.Request.Query, EndpointOptions);
    }

    /// <summary>
    /// Declares that <c>?q=</c> on this controller's collection and <c>POST query</c> endpoints is a provider-native
    /// query - a SQL predicate on relational connectors - run as written. Off by default: a request that still carries
    /// <c>q</c> when the read executes is refused with <c>400</c>, because request text is never a provider query
    /// unless the endpoint says so (WEB-0074, DATA-0096 section 7.5). The MCP surface never grants it.
    /// <para>Override it only on an endpoint restricted to trusted callers: it lets whoever can read the collection
    /// read whatever the connector's database user can. SQLite and PostgreSQL confine the text to one read-only
    /// statement, CockroachDB to one statement (its read-only mode refuses DML, not DDL); the other relational
    /// connectors run it as written. To give <c>q</c> a meaning for untrusted callers, turn it into a filter in an
    /// <c>IRequestOptionsHook&lt;TEntity&gt;</c> instead.</para>
    /// </summary>
    protected virtual bool AllowRawQuery => false;

    protected virtual ObjectResult PrepareResponse(object? content)
    {
        Response.Headers["Vary"] = "Accept";
        // SEC-0004 (§C): the single Koan-Access list header (the permitted verbs for this principal) is computed
        // from the seam inside the endpoint service and flows via result.Headers (ApplyResponseMetadata) — the
        // single-item form of the per-row capability projection, not the old CanRead/CanWrite/CanRemove virtuals.
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
        if (Admit() is { } refused) return refused;
        if (Operation.Keyset is Keyset<TEntity> order) return await Continue(order, ct);
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
        else if (query.TryGetValue("size", out var vs) && int.TryParse(vs, out var aliasSize))
        {
            requestedSize = aliasSize;
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

        QueryOptions options;
        try
        {
            options = BuildOptions();
        }
        catch (Koan.Data.Abstractions.Sorting.InvalidSortFieldException ex)
        {
            return BadRequest(new { error = ex.Message, field = ex.Field });
        }
        if (!query.TryGetValue("view", out var requestedView) || string.IsNullOrWhiteSpace(requestedView))
            options.View = DefaultCollectionView;
        options.Page = applyPagination ? page : 0;
        options.PageSize = applyPagination ? pageSize : 0;

        if (!string.IsNullOrWhiteSpace(policy.DefaultSort) && options.Sort.Count == 0)
        {
            try
            {
                options.Sort.AddRange(Koan.Data.Core.Sorting.SortSpecParser.ParseStrict<TEntity>(policy.DefaultSort));
            }
            catch (Koan.Data.Abstractions.Sorting.InvalidSortFieldException ex)
            {
                return BadRequest(new { error = $"Invalid PaginationAttribute.DefaultSort: {ex.Message}", field = ex.Field });
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
            // SEC-0004 (§C): ?access=true opts into the per-row `access` sidecar (decoupled from ?with= expansion).
            IncludeAccess = GetBooleanQueryValue(query, Koan.Web.Authorization.AccessProjection.QueryToggle),
            QueryParameters = ToQueryDictionary(query),
            AllowRawQuery = AllowRawQuery
        };

        var result = await EndpointService.GetCollection(request);
        ApplyResponseMetadata(result);

        if (result.IsShortCircuited)
        {
            return ResolveShortCircuit(result);
        }

        return PrepareResponse(result.Payload ?? result.Items);
    }

    /// <summary>
    /// A declared collection (DATA-0170): a page of at most <c>size</c> rows, bounded by its <see cref="PaginationAttribute"/>, after the row
    /// its sealed <c>after</c> names, in the order its <c>DefaultSort</c> declares with the identity breaking ties, answered as its rows and the
    /// <c>after</c> of the next page; no offset, count or total header. An <c>after</c> that does not open is refused, naming it.
    /// </summary>
    private async Task<IActionResult> Continue(Keyset<TEntity> order, CancellationToken ct)
    {
        var query = HttpContext.Request.Query;
        var policy = GetPaginationPolicy();
        var size = policy.DefaultSize;
        if (query.TryGetValue(KoanWebConstants.Query.Size, out var asked)
            && !int.TryParse(asked.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out size))
            return this.Refused(Reason.Invalid(KoanWebConstants.Query.Size, $"'{KoanWebConstants.Query.Size}' is a whole number of rows, at most {policy.MaxSize}."));
        size = policy.Clamp(1, size).Size;

        var protection = HttpContext.RequestServices.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>();
        var purpose = GetType().FullName ?? GetType().Name;
        Koan.Data.Core.Querying.KeysetKey? after = null;
        if (query.TryGetValue(KoanWebConstants.Query.After, out var sealedAfter) && !string.IsNullOrEmpty(sealedAfter.ToString())
            && (after = SealedCursor.Open(protection, purpose, order, sealedAfter.ToString())) is null)
            return this.Refused(SealedCursor.Refusal());

        QueryOptions options;
        try
        {
            options = BuildOptions();
        }
        catch (Koan.Data.Abstractions.Sorting.InvalidSortFieldException ex)
        {
            return BadRequest(new { error = ex.Message, field = ex.Field });
        }
        if (!query.TryGetValue("view", out var requestedView) || string.IsNullOrWhiteSpace(requestedView))
            options.View = DefaultCollectionView;
        options.Page = 0;
        options.PageSize = 0;

        var request = new EntityCollectionRequest
        {
            Context = CreateRequestContext(options, ct),
            Set = query.TryGetValue(KoanWebConstants.Query.Set, out var set) ? set.ToString() : null,
            Policy = policy,
            AbsoluteMaxRecords = policy.AbsoluteMaxRecords,
            Accept = HttpContext.Request.Headers["Accept"].ToString(),
            BasePath = HttpContext.Request.Path.HasValue ? HttpContext.Request.Path.Value : "/",
            QueryParameters = ToQueryDictionary(query),
            AllowRawQuery = AllowRawQuery,
            Keyset = new KeysetRead(order, after, size, key => SealedCursor.Seal(protection, purpose, order, key)),
        };

        var result = await EndpointService.GetCollection(request);
        ApplyResponseMetadata(result);
        if (result.IsShortCircuited) return ResolveShortCircuit(result);
        return PrepareResponse(result.Payload ?? result.Items);
    }

    // POST /query
    [HttpPost("query")]
    [Produces(MediaTypeNames.Application.Json)]
    public virtual async Task<IActionResult> Query([FromBody] object body, CancellationToken ct)
    {

        QueryOptions options;
        try
        {
            options = BuildOptions();
        }
        catch (Koan.Data.Abstractions.Sorting.InvalidSortFieldException ex)
        {
            return BadRequest(new { error = ex.Message, field = ex.Field });
        }
        var context = CreateRequestContext(options, ct);

        string? filterJson = null;
        string? set = null;
        bool ignoreCase = false;

        // X-entitycontroller-query-parse: parse the body explicitly and fail loud on bad input (GET-parity).
        // The previous bare catch swallowed JObject.Parse + (int) cast failures and ran the query with
        // DEFAULTS — silently dropping the filter and returning unfiltered results (200). A non-object body
        // now 400s, page/size mirror the GET-list path (non-coercible -> ignored; negative page rejected),
        // and coercion no longer throws.
        JObject jobj;
        try
        {
            if (JToken.Parse(JsonConvert.SerializeObject(body)) is not JObject parsed)
            {
                return BadRequest(new { error = "Request body must be a JSON object." });
            }
            jobj = parsed;
        }
        catch (JsonException)
        {
            return BadRequest(new { error = "Invalid request body." });
        }

        if (Admit(jobj, bodyIsWords: true) is { } refused) return refused;
        if (jobj.TryGetValue("filter", out var f)) filterJson = f.ToString(Formatting.None);
        if (jobj.TryGetValue("page", out var pageToken) && TryCoerceQueryInt(pageToken, out var pageValue))
        {
            if (pageValue < 0) return BadRequest(new { error = "page must be >= 0" });
            options.Page = pageValue;
        }
        if (jobj.TryGetValue("size", out var sizeToken) && TryCoerceQueryInt(sizeToken, out var sizeValue))
        {
            options.PageSize = sizeValue;
        }
        if (jobj.TryGetValue("set", out var st) && st.Type == JTokenType.String) set = st.ToString();
        // ADR-0093: body sort field. Accepts string array of URL-grammar specs (e.g. ["-createdAt", "+title"]).
        // Body sort overrides any ?sort= query-string value to keep the schema unambiguous.
        if (jobj.TryGetValue("sort", out var sortNode))
        {
            IEnumerable<string>? rawSpecs = null;
            if (sortNode.Type == JTokenType.Array)
            {
                rawSpecs = sortNode.Children<JToken>()
                    .Where(t => t.Type == JTokenType.String)
                    .Select(t => t.Value<string>()!)
                    .Where(s => !string.IsNullOrWhiteSpace(s));
            }
            else if (sortNode.Type == JTokenType.String)
            {
                var raw = sortNode.Value<string>();
                if (!string.IsNullOrWhiteSpace(raw)) rawSpecs = new[] { raw };
            }

            if (rawSpecs is not null)
            {
                try
                {
                    var collected = new List<SortSpec>();
                    foreach (var spec in rawSpecs)
                    {
                        collected.AddRange(Koan.Data.Core.Sorting.SortSpecParser.ParseStrict<TEntity>(spec));
                    }
                    options.Sort.Clear();
                    options.Sort.AddRange(collected);
                }
                catch (Koan.Data.Abstractions.Sorting.InvalidSortFieldException ex)
                {
                    return BadRequest(new { error = ex.Message, field = ex.Field });
                }
            }
        }
        if (jobj.TryGetValue("", out var opt) && opt.Type == JTokenType.Object)
        {
            var o = (JObject)opt;
            if (o.TryGetValue("ignoreCase", out var ic) && ic.Type == JTokenType.Boolean && (bool)ic) ignoreCase = true;
        }

        var request = new EntityQueryRequest
        {
            Context = context,
            FilterJson = filterJson,
            Set = set,
            IgnoreCase = ignoreCase,
            Accept = HttpContext.Request.Headers["Accept"].ToString(),
            // WEB-0076: size and page are clamped to this controller's policy by the shared endpoint.
            Policy = GetPaginationPolicy(),
            AllowRawQuery = AllowRawQuery
        };

        var result = await EndpointService.Query(request);
        ApplyResponseMetadata(result);
        if (result.IsShortCircuited)
        {
            return ResolveShortCircuit(result);
        }
        return PrepareResponse(result.Payload ?? result.Items);
    }

    // Coerce a JSON token to an int for paging without throwing (GET-parity: non-coercible -> ignored,
    // out-of-range integers treated as absent rather than overflowing).
    private static bool TryCoerceQueryInt(JToken token, out int value)
    {
        value = 0;
        switch (token.Type)
        {
            case JTokenType.Integer:
                var l = token.Value<long>();
                if (l < int.MinValue || l > int.MaxValue) return false;
                value = (int)l;
                return true;
            case JTokenType.String:
                return int.TryParse(
                    token.Value<string>(),
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out value);
            default:
                return false;
        }
    }

    [HttpGet("new")]
    public virtual async Task<IActionResult> GetNew(CancellationToken ct)
    {
        if (Admit() is { } refused) return refused;
        var options = BuildOptions();
        var context = CreateRequestContext(options, ct);
        var request = new EntityGetNewRequest
        {
            Context = context,
            Accept = HttpContext.Request.Headers["Accept"].ToString()
        };
        var result = await EndpointService.GetNew(request);
        ApplyResponseMetadata(result);
        if (result.IsShortCircuited) return ResolveShortCircuit(result);
        return PrepareResponse(result.Payload ?? result.Model);
    }

    [HttpGet("{id}")]
    public virtual async Task<IActionResult> GetById([FromRoute] TKey id, CancellationToken ct)
    {
        if (Admit() is { } refused) return refused;
        var (found, key) = await Keyed(id, ct);
        if (!found) return NotFound();
        var options = BuildOptions();
        var context = CreateRequestContext(options, ct);
        var query = HttpContext.Request.Query;
        var request = new EntityGetByIdRequest<TKey>
        {
            Context = context,
            Id = key,
            Set = query.TryGetValue("set", out var setVal) ? setVal.ToString() : null,
            With = GetRelationshipParameter(query),
            Accept = HttpContext.Request.Headers["Accept"].ToString()
        };
        var result = await EndpointService.GetById(request);
        ApplyResponseMetadata(result);
        if (result.IsShortCircuited) return ResolveShortCircuit(result);
        return PrepareResponse(result.Payload ?? result.Model);
    }

    [HttpPost("")]
    public virtual async Task<IActionResult> Upsert([FromBody][ValidateNever] TEntity model, CancellationToken ct)
    {
        if (model is null) return BadRequest(new { error = "Request body is required" });
        // WEB-0087: a declared create only creates, writes what it declares, and takes its input from the same body.
        var operation = Operation;
        JObject? body = null;
        IReadOnlyCollection<string>? writes = null;
        if (operation.Declared && (operation.Writes is not null || operation.Input is not null || HttpContext.Request.HasFormContentType))
        {
            var (sent, unfit) = await Sent(operation, ct);
            if (unfit is not null) return unfit;
            body = sent;
            var written = Written(operation, body?.Properties().Select(member => member.Name) ?? []);
            if (written.Refused is { } stranger) return stranger;
            writes = written.Writes;
        }
        if (Admit(body) is { } refused) return refused;
        return await ExecuteUpsert(model, ct, default!, writes, operation.Declared);
    }

    // WEB-0073: governed full-replace shim. The verb adds no authority of its own - the body travels
    // the identical Upsert choke point (validation, [Access], hooks, stamps, audit, facts). The only
    // PUT-specific rule is id authority: the route wins. The body's id is checked at the JSON level
    // (the Entity constructor assigns an id at bind time, so the bound model cannot reveal whether
    // the client sent one), then the route id is forced before materialization.
    [HttpPut("{id}")]
    public virtual async Task<IActionResult> Put([FromRoute] TKey id, CancellationToken ct)
    {
        if (Admit() is { } refused) return refused;
        Microsoft.AspNetCore.Http.HttpRequestRewindExtensions.EnableBuffering(HttpContext.Request);
        string raw;
        using (var reader = new System.IO.StreamReader(HttpContext.Request.Body, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true))
        {
            raw = await reader.ReadToEndAsync(ct);
            HttpContext.Request.Body.Position = 0;
        }

        if (string.IsNullOrWhiteSpace(raw))
            return BadRequest(new { error = "Request body is required" });

        Newtonsoft.Json.Linq.JObject body;
        try
        {
            body = Newtonsoft.Json.Linq.JObject.Parse(raw);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = $"Invalid PUT body: {ex.Message}" });
        }

        // Defense-in-depth, mirroring PATCH: a body that states an id must agree with the route.
        if (body["id"] is not null)
        {
            var bodyId = body["id"]!.ToObject<TKey>();
            if (bodyId is not null && !EqualityComparer<TKey>.Default.Equals(bodyId, id))
            {
                return Conflict(new { code = Infrastructure.KoanWebConstants.Codes.Put.IdMismatch, message = "Route id does not match body id.", routeId = id, bodyId });
            }
        }
        body["id"] = JValue.FromObject(id);

        TEntity? model;
        try
        {
            var effective = ContextBuilder.Build(new QueryOptions(), ct, HttpContext);
            var fields = await FieldAccess.Prepare(typeof(TEntity), effective.Services, effective.User, ct);
            fields.DemandReplacement();
            var settings = HttpContext.RequestServices.GetRequiredService<IOptions<MvcNewtonsoftJsonOptions>>().Value.SerializerSettings;
            model = body.ToObject<TEntity>(JsonSerializer.Create(fields.CreateSerializerSettings(settings)));
        }
        catch (UnauthorizedAccessException error)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { code = KoanWebConstants.Codes.FieldAccess.Denied, error = error.Message });
        }
        catch (Exception error) when (error is JsonException or NotSupportedException)
        {
            return BadRequest(new { code = KoanWebConstants.Codes.FieldAccess.Unsupported, error = error.Message });
        }
        if (model is null) return BadRequest(new { error = "Request body could not be bound to the entity" });
        return await ExecuteUpsert(model, ct, id);
    }

    private async Task<IActionResult> ExecuteUpsert(TEntity model, CancellationToken ct, TKey routeId,
        IReadOnlyCollection<string>? writes = null, bool insert = false)
    {
        var options = BuildOptions();
        var context = CreateRequestContext(options, ct);
        var query = HttpContext.Request.Query;
        var request = new EntityUpsertRequest<TEntity, TKey>
        {
            Context = context,
            Model = model,
            RouteId = routeId,
            Set = query.TryGetValue("set", out var setVal) ? setVal.ToString() : null,
            Accept = HttpContext.Request.Headers["Accept"].ToString(),
            Insert = insert,
            Writes = writes
        };
        var result = await EndpointService.Upsert(request);
        ApplyResponseMetadata(result);
        if (result.IsShortCircuited) return ResolveShortCircuit(result);
        // A declared create only creates, so a replay its hook answers is answered as the create was.
        return Answered(result, insert || Equals(result.Context.Items.TryGetValue(EntityMutationProbe.OperationKey, out var done) ? done : null, "create"));
    }

    [HttpPost("bulk")]
    public virtual async Task<IActionResult> UpsertMany([FromBody][ValidateNever] IEnumerable<TEntity> models, CancellationToken ct)
    {
        if (models is null) return BadRequest(new { error = "Request body is required" });
        if (Admit() is { } refused) return refused;
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
        var result = await EndpointService.UpsertMany(request);
        ApplyResponseMetadata(result);
        if (result.IsShortCircuited) return ResolveShortCircuit(result);
        return PrepareResponse(result.Payload);
    }

    [HttpDelete("{id}")]
    public virtual async Task<IActionResult> Delete([FromRoute] TKey id, CancellationToken ct)
    {
        // WEB-0088: a declared delete sent as a form takes its input from the form's fields, as one sent as a tool takes it from the query.
        JObject? words = null;
        if (Operation.Declared && HttpContext.Request.HasFormContentType)
        {
            var (sent, unfit) = await Sent(Operation, ct);
            if (unfit is not null) return unfit;
            words = sent;
        }
        if (Admit(words, bodyIsWords: words is not null) is { } refused) return refused;
        var (found, key) = await Keyed(id, ct);
        if (!found) return NotFound();
        var options = BuildOptions();
        var context = CreateRequestContext(options, ct);
        var query = HttpContext.Request.Query;
        var request = new EntityDeleteRequest<TKey>
        {
            Context = context,
            Id = key,
            Set = query.TryGetValue("set", out var setVal) ? setVal.ToString() : null,
            Accept = HttpContext.Request.Headers["Accept"].ToString()
        };
        var result = await EndpointService.Delete(request);
        ApplyResponseMetadata(result);
        if (result.IsShortCircuited) return ResolveShortCircuit(result);
        return Answered(result, created: false, deleted: true);
    }

    [HttpDelete("bulk")]
    public virtual async Task<IActionResult> DeleteMany([FromBody] IEnumerable<TKey> ids, CancellationToken ct)
    {
        if (Admit() is { } refused) return refused;
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
        var result = await EndpointService.DeleteMany(request);
        ApplyResponseMetadata(result);
        if (result.IsShortCircuited) return ResolveShortCircuit(result);
        return PrepareResponse(result.Payload);
    }

    [HttpDelete("")]
    public virtual async Task<IActionResult> DeleteByQuery([FromQuery] string? q, CancellationToken ct)
    {
        if (Admit() is { } refused) return refused;
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
        var result = await EndpointService.DeleteByQuery(request);
        ApplyResponseMetadata(result);
        if (result.IsShortCircuited) return ResolveShortCircuit(result);
        return PrepareResponse(result.Payload);
    }

    [HttpDelete("all")]
    public virtual async Task<IActionResult> DeleteAll(CancellationToken ct)
    {
        if (Admit() is { } refused) return refused;
        var options = BuildOptions();
        var context = CreateRequestContext(options, ct);
        var query = HttpContext.Request.Query;
        var request = new EntityDeleteAllRequest
        {
            Context = context,
            Set = query.TryGetValue("set", out var setVal) ? setVal.ToString() : null
        };
        var result = await EndpointService.DeleteAll(request);
        ApplyResponseMetadata(result);
        if (result.IsShortCircuited) return ResolveShortCircuit(result);
        return PrepareResponse(result.Payload);
    }

    // Single PATCH action that dispatches by Content-Type. Previously this was three sibling
    // actions distinguished only by [Consumes(...)], but ASP.NET Core's media-type matcher treats
    // a `+json` structured suffix (application/json-patch+json, application/merge-patch+json) as
    // a subset of application/json — so all three endpoints matched any JSON-shaped payload and
    // the router raised AmbiguousMatchException. One action with explicit dispatch sidesteps that.
    [HttpPatch("{id}")]
    [Consumes(
        Infrastructure.KoanWebConstants.ContentTypes.ApplicationJsonPatch,
        Infrastructure.KoanWebConstants.ContentTypes.ApplicationMergePatch,
        Infrastructure.KoanWebConstants.ContentTypes.ApplicationJson)]
    public virtual async Task<IActionResult> Patch([FromRoute] TKey id, CancellationToken ct)
    {
        string raw;
        string mediaType;
        if (Operation.Declared && HttpContext.Request.HasFormContentType)
        {
            // WEB-0088: a page's form is the partial JSON object of the members it names, admitted and written as that object would be.
            var (sent, unfit) = await Sent(Operation, ct);
            if (unfit is not null) return unfit;
            raw = sent!.ToString(Formatting.None);
            mediaType = Infrastructure.KoanWebConstants.ContentTypes.ApplicationJson;
        }
        else
        {
            // Buffer the body so we can parse it with the right shape per Content-Type.
            Microsoft.AspNetCore.Http.HttpRequestRewindExtensions.EnableBuffering(HttpContext.Request);
            using (var reader = new System.IO.StreamReader(HttpContext.Request.Body, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true))
            {
                raw = await reader.ReadToEndAsync(ct);
                HttpContext.Request.Body.Position = 0;
            }

            var contentType = HttpContext.Request.ContentType ?? Infrastructure.KoanWebConstants.ContentTypes.ApplicationJson;
            // Take only the media type portion (strip ;charset=...).
            mediaType = contentType.Split(';', 2)[0].Trim();
        }

        object patch;
        var kind = EntityPatchKind.PartialJson;
        var operation = Operation;
        JObject? words = null;
        IReadOnlyCollection<string>? writes = null;
        JToken? payloadId = null;
        try
        {
            if (string.Equals(mediaType, Infrastructure.KoanWebConstants.ContentTypes.ApplicationJsonPatch, StringComparison.OrdinalIgnoreCase))
            {
                var doc = string.IsNullOrWhiteSpace(raw)
                    ? new JsonPatchDocument<TEntity>()
                    : JsonConvert.DeserializeObject<JsonPatchDocument<TEntity>>(raw)
                        ?? new JsonPatchDocument<TEntity>();
                // WEB-0087: each operation writes the member its path, and a move's source, begin with.
                var written = Written(operation, doc.Operations
                    .SelectMany(change => new[] { change.path, string.Equals(change.op, "move", StringComparison.OrdinalIgnoreCase) ? change.from : null })
                    .Where(path => !string.IsNullOrEmpty(path)).Select(path => path!.TrimStart('/').Split('/')[0]));
                if (written.Refused is { } stranger) return stranger;
                patch = doc;
                kind = EntityPatchKind.JsonPatch6902;
            }
            else
            {
                var token = string.IsNullOrWhiteSpace(raw) ? JValue.CreateNull() : JToken.Parse(raw);
                payloadId = token["id"];
                if (operation.Writes is not null || operation.Input is not null)
                {
                    // WEB-0087: the body names what it writes, and the declared input travels in it beside those members.
                    if (token is not JObject members) return this.Refused(Reason.Invalid("A change is one JSON object of the members it writes."));
                    var written = Written(operation, members.Properties().Select(member => member.Name));
                    if (written.Refused is { } stranger) return stranger;
                    writes = written.Writes;
                    words = members;
                    var kept = (JObject)members.DeepClone();
                    foreach (var input in members.Properties().Where(member => operation.InputMembers.Any(item => EntityOperation.Named(item, member.Name))))
                        kept.Remove(input.Name);
                    token = kept;
                }

                patch = token!;
                if (string.Equals(mediaType, Infrastructure.KoanWebConstants.ContentTypes.ApplicationMergePatch, StringComparison.OrdinalIgnoreCase))
                    kind = EntityPatchKind.MergePatch7386;
            }
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = $"Invalid PATCH body for content type '{mediaType}': {ex.Message}" });
        }

        if (Admit(words) is { } refused) return refused;
        var (found, key) = await Keyed(id, ct);
        if (!found) return NotFound();

        // Defense-in-depth: an id inside the payload must agree with the row the route names.
        if (payloadId is not null && payloadId.Type != JTokenType.Null)
        {
            TKey? stated;
            try { stated = payloadId.ToObject<TKey>(); }
            catch (Exception ex) { return BadRequest(new { error = $"Invalid PATCH body for content type '{mediaType}': {ex.Message}" }); }
            if (stated is not null && !Equals(stated, key))
                return BadRequest(new { code = Infrastructure.KoanWebConstants.Codes.Patch.IdMismatch, message = "Route id does not match payload id." });
        }

        // SEC-0004: route through the governed choke point - gate, row-scope constrain, stamps, hooks and
        // audit all live there. The data-layer Patch instruction bypasses every one of those and must never
        // be called directly from a public surface.
        return await PatchNormalized(key, patch, kind, ct, writes);
    }

    private async Task<IActionResult> PatchNormalized(TKey id, object patch, EntityPatchKind kind, CancellationToken ct,
        IReadOnlyCollection<string>? writes)
    {
        var options = BuildOptions();
        var context = CreateRequestContext(options, ct);
        var query = HttpContext.Request.Query;
        var set = query.TryGetValue(Koan.Web.Infrastructure.KoanWebConstants.Query.Set, out var setVal) ? setVal.ToString() : null;
        using var _ = EntityContext.With(partition: string.IsNullOrWhiteSpace(set) ? null : set);

        var request = new EntityPatchRequest<TEntity, TKey>
        {
            Context = context,
            Id = id,
            Patch = patch,
            Kind = kind,
            Set = set,
            Accept = HttpContext.Request.Headers["Accept"].ToString(),
            Options = TryBuildPatchOptionsOverride(query) ?? BuildPatchOptions(),
            Writes = writes,
        };

        var result = await EndpointService.Patch(request);
        ApplyResponseMetadata(result);
        if (result.IsShortCircuited) return ResolveShortCircuit(result);
        return Answered(result, created: false);
    }

    private Koan.Data.Abstractions.Instructions.PatchPayload<TKey> NormalizeFromJsonPatch(TKey id, JsonPatchDocument<TEntity> doc)
    {
        // Delegate to static normalizer for pure patch transformation
        return PatchNormalizer.NormalizeJsonPatch<TEntity, TKey>(id, doc, BuildPatchOptions());
    }

    private Koan.Data.Abstractions.Instructions.PatchPayload<TKey> NormalizeFromMergePatch(TKey id, Newtonsoft.Json.Linq.JToken body)
    {
        // Delegate to static normalizer for merge patch semantics
        return PatchNormalizer.NormalizeMergePatch(id, body, BuildPatchOptions());
    }

    private Koan.Data.Abstractions.Instructions.PatchPayload<TKey> NormalizeFromPartialJson(TKey id, Newtonsoft.Json.Linq.JToken body)
    {
        // Delegate to static normalizer for partial JSON semantics
        return PatchNormalizer.NormalizePartialJson(id, body, BuildPatchOptions());
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






















