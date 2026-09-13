using System.ComponentModel.DataAnnotations;
using Koan.Identity.Roles;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Koan.Identity.Web;

[ApiController]
[Authorize]
[ServiceFilter(typeof(ScopedRoleAntiforgeryFilter))]
[ServiceFilter(typeof(ScopedRoleExceptionFilter))]
[Route("api/identity/scoped-roles/{tenantId}/{scopeType}/{scopeId}")]
public sealed class ScopedRoleManagementController(RoleEngine engine) : ControllerBase
{
    [HttpGet("/api/identity/scoped-roles/descriptor")]
    public async Task<ActionResult<ScopedRoleEngineDescriptor>> Descriptor(CancellationToken ct)
        => Ok(await engine.Describe(ct));

    [HttpGet("roles")]
    public async Task<ActionResult<Page<RoleResponse>>> Roles(string tenantId, string scopeType, string scopeId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
        => Ok(Map(await engine.Roles(Scope(tenantId, scopeType, scopeId), page, pageSize, ct), RoleResponse.From));

    [HttpGet("roles/{roleId}")]
    public async Task<ActionResult<RoleResponse>> Role(string tenantId, string scopeType, string scopeId,
        string roleId, CancellationToken ct)
    {
        var role = await engine.Role(roleId, Scope(tenantId, scopeType, scopeId), ct);
        if (role is null) return NotFound(new Error("resource.unavailable", "The resource is unavailable."));
        SetEtag(role.Version);
        return Ok(RoleResponse.From(role));
    }

    [HttpPost("roles")]
    public async Task<ActionResult<RoleResponse>> Define(string tenantId, string scopeType, string scopeId,
        [FromBody] DefineRoleRequest request, CancellationToken ct)
    {
        var role = await engine.Define(new(Scope(tenantId, scopeType, scopeId), request.Name,
            request.Grants ?? [], Purpose: request.Purpose, Presentation: request.Presentation));
        SetEtag(role.Version);
        return CreatedAtAction(nameof(Role), new { tenantId, scopeType, scopeId, roleId = role.Id }, RoleResponse.From(role));
    }

    [HttpPut("roles/{roleId}")]
    public async Task<ActionResult<RoleResponse>> Edit(string tenantId, string scopeType, string scopeId,
        string roleId, [FromBody] EditRoleRequest request, CancellationToken ct)
    {
        var target = Scope(tenantId, scopeType, scopeId);
        if (await engine.Role(roleId, target, ct) is null)
            return NotFound(new Error("resource.unavailable", "The resource is unavailable."));
        if (!TryExpectedVersion(out var version, out var failure)) return failure!;
        var role = await engine.Edit(new(roleId, version, request.Name, request.Purpose,
            request.Grants, request.Presentation));
        SetEtag(role.Version);
        return Ok(RoleResponse.From(role));
    }

    [HttpPost("roles/{roleId}/disable")]
    public Task<ActionResult<RoleResponse>> Disable(string tenantId, string scopeType, string scopeId,
        string roleId, CancellationToken ct) => ChangeStatus(tenantId, scopeType, scopeId, roleId, false, ct);

    [HttpPost("roles/{roleId}/retire")]
    public Task<ActionResult<RoleResponse>> Retire(string tenantId, string scopeType, string scopeId,
        string roleId, CancellationToken ct) => ChangeStatus(tenantId, scopeType, scopeId, roleId, true, ct);

    [HttpPut("roles/{roleId}/members/{subject}")]
    public async Task<IActionResult> AddMember(string tenantId, string scopeType, string scopeId,
        string roleId, string subject, CancellationToken ct)
    {
        _ = await engine.Add(new(Scope(tenantId, scopeType, scopeId), subject, roleId), ct);
        return NoContent();
    }

    [HttpGet("roles/{roleId}/members")]
    public async Task<ActionResult<Page<MemberResponse>>> Members(string tenantId, string scopeType, string scopeId,
        string roleId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
        => Ok(Map(await engine.Members(roleId, Scope(tenantId, scopeType, scopeId), page, pageSize, ct),
            item => new MemberResponse(item.Subject)));

    [HttpGet("members/{subject}")]
    public async Task<ActionResult<MembershipsResponse>> Memberships(string tenantId, string scopeType, string scopeId,
        string subject, CancellationToken ct)
    {
        var memberships = await engine.Memberships(subject, Scope(tenantId, scopeType, scopeId), ct);
        return Ok(new MembershipsResponse(memberships.Subject, memberships.Roles, memberships.Groups));
    }

    [HttpDelete("roles/{roleId}/members/{subject}")]
    public async Task<IActionResult> RemoveMember(string tenantId, string scopeType, string scopeId,
        string roleId, string subject, CancellationToken ct)
    {
        _ = await engine.Remove(new(Scope(tenantId, scopeType, scopeId), subject, roleId), ct);
        return NoContent();
    }

    [HttpGet("policies")]
    public async Task<ActionResult<Page<PolicyResponse>>> Policies(string tenantId, string scopeType, string scopeId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
        => Ok(Map(await engine.Policies(Scope(tenantId, scopeType, scopeId), page, pageSize, ct), PolicyResponse.From));

    [HttpGet("policies/{capability}")]
    public async Task<ActionResult<PolicyResponse>> Policy(string tenantId, string scopeType, string scopeId,
        string capability, CancellationToken ct)
    {
        var policy = await engine.Policy(capability, Scope(tenantId, scopeType, scopeId), ct);
        if (policy is null) return NotFound(new Error("resource.unavailable", "The resource is unavailable."));
        SetEtag(policy.Version);
        return Ok(PolicyResponse.From(policy));
    }

    [HttpPut("policies/{capability}")]
    public async Task<ActionResult<PolicyResponse>> ReplacePolicy(string tenantId, string scopeType, string scopeId,
        string capability, [FromBody] ReplacePolicyRequest request, CancellationToken ct)
    {
        var target = Scope(tenantId, scopeType, scopeId);
        var current = await engine.Policy(capability, target, ct);
        long? version = null;
        if (current is not null)
        {
            if (!TryExpectedVersion(out var expected, out var failure)) return failure!;
            version = expected;
        }
        else if (Request.Headers.ContainsKey("If-Match"))
            return StatusCode(412, new Error("version.stale", "The resource version changed."));
        var policy = await engine.Replace(new(target, capability, request.Audience ?? [], version), ct);
        SetEtag(policy.Version);
        return Ok(PolicyResponse.From(policy));
    }

    [HttpDelete("policies/{capability}")]
    public async Task<ActionResult<PolicyResponse>> ResetPolicy(string tenantId, string scopeType, string scopeId,
        string capability, CancellationToken ct)
    {
        if (!TryExpectedVersion(out var version, out var failure)) return failure!;
        var policy = await engine.Inherit(Scope(tenantId, scopeType, scopeId), capability, version, ct);
        SetEtag(policy.Version);
        return Ok(PolicyResponse.From(policy));
    }

    [HttpGet("effective/{capability}")]
    public async Task<ActionResult<DecisionResponse>> Effective(string tenantId, string scopeType, string scopeId,
        string capability, CancellationToken ct)
    {
        try
        {
            var plan = await engine.Plan(capability, Scope(tenantId, scopeType, scopeId), ct: ct);
            return Ok(DecisionResponse.From(plan));
        }
        catch (ScopedRoleException)
        {
            return Ok(DecisionResponse.Denied);
        }
    }

    [HttpPost("preview")]
    public async Task<ActionResult<DecisionResponse>> Preview(string tenantId, string scopeType, string scopeId,
        [FromBody] PreviewRequest request, CancellationToken ct)
        => Ok(DecisionResponse.From((await engine.Preview(request.Subject, request.Capability,
            Scope(tenantId, scopeType, scopeId), request.Parameters, ct)).Plan));

    private async Task<ActionResult<RoleResponse>> ChangeStatus(string tenantId, string scopeType, string scopeId,
        string roleId, bool retire, CancellationToken ct)
    {
        if (await engine.Role(roleId, Scope(tenantId, scopeType, scopeId), ct) is null)
            return NotFound(new Error("resource.unavailable", "The resource is unavailable."));
        if (!TryExpectedVersion(out var version, out var failure)) return failure!;
        var role = retire ? await engine.Retire(roleId, version, ct) : await engine.Disable(roleId, version, ct);
        SetEtag(role.Version);
        return Ok(RoleResponse.From(role));
    }

    private static ScopedRoleScopeRef Scope(string tenantId, string scopeType, string scopeId)
        => new(tenantId, scopeType, scopeId);

    private bool TryExpectedVersion(out long version, out ActionResult? failure)
    {
        var value = Request.Headers.IfMatch.ToString().Trim();
        if (string.IsNullOrEmpty(value))
        {
            version = 0;
            failure = StatusCode(428, new Error("version.required", "If-Match is required."));
            return false;
        }
        value = value.Trim('"');
        if (!long.TryParse(value, out version) || version < 1)
        {
            failure = BadRequest(new Error("version.invalid", "If-Match must contain a positive version."));
            return false;
        }
        failure = null;
        return true;
    }

    private void SetEtag(long version) => Response.Headers.ETag = $"\"{version}\"";

    private static Page<TResponse> Map<TEntity, TResponse>(ScopedRolePage<TEntity> page,
        Func<TEntity, TResponse> map) => new(page.Items.Select(map).ToArray(), page.TotalCount, page.Page, page.PageSize);

    public sealed record DefineRoleRequest(
        [StringLength(ScopedRoleInputLimits.NameLength)] string Name,
        IReadOnlyList<ScopedRoleGrantClause>? Grants,
        [StringLength(ScopedRoleInputLimits.DescriptionLength)] string? Purpose = null,
        IReadOnlyDictionary<string, string>? Presentation = null);
    public sealed record EditRoleRequest(
        [StringLength(ScopedRoleInputLimits.NameLength)] string? Name = null,
        [StringLength(ScopedRoleInputLimits.DescriptionLength)] string? Purpose = null,
        IReadOnlyList<ScopedRoleGrantClause>? Grants = null, IReadOnlyDictionary<string, string>? Presentation = null);
    public sealed record ReplacePolicyRequest(IReadOnlyList<ScopedRoleAudienceClause>? Audience);
    public sealed record PreviewRequest(
        [StringLength(ScopedRoleInputLimits.IdentifierLength)] string Subject,
        [StringLength(ScopedRoleInputLimits.IdentifierLength)] string Capability,
        IReadOnlyDictionary<string, object?>? Parameters = null);
    public sealed record Error(string Code, string Message);
    public sealed record DecisionResponse(bool Allowed, string Code)
    {
        internal static DecisionResponse Denied { get; } = new(false, "access.denied");
        internal static DecisionResponse From(ScopedRolePlan plan)
            => plan.Allowed ? new(true, "access.allowed") : Denied;
    }
    public sealed record Page<T>(IReadOnlyList<T> Items, long TotalCount, int PageNumber, int PageSize);
    public sealed record MemberResponse(string Subject);
    public sealed record MembershipsResponse(string Subject, IReadOnlyList<string> Roles, IReadOnlyList<string> Groups);
    public sealed record RoleResponse(string Id, string Name, string? Purpose, ScopedRoleStatus Status,
        IReadOnlyList<ScopedRoleGrantClause> Grants, IReadOnlyDictionary<string, string> Presentation,
        long Version, long AuthorityVersion)
    {
        internal static RoleResponse From(ScopedRoleDefinition role) => new(role.Id, role.Name, role.Purpose,
            role.Status, role.Grants, role.Presentation, role.Version, role.AuthorityVersion);
    }
    public sealed record PolicyResponse(string Id, string Capability, ScopedRoleOverrideMode Mode,
        IReadOnlyList<ScopedRoleAudienceClause> Audience, long Version)
    {
        internal static PolicyResponse From(ScopedRolePolicy policy) => new(policy.Id, policy.Capability,
            policy.Mode, policy.Audience, policy.Version);
    }
}

internal sealed class ScopedRoleExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        var mapped = context.Exception switch
        {
            ScopedRoleConcurrencyException error => (412, error.Code, "The resource version changed."),
            ScopedRoleAuthorizationException or ScopedRoleValidationException =>
                (404, "resource.unavailable", "The resource is unavailable."),
            NotSupportedException => (409, "provider.capability.unsupported", "The active data provider cannot enforce this operation safely."),
            ArgumentException => (400, "request.invalid", "The request is invalid."),
            _ => ((int status, string code, string message)?)null,
        };
        if (mapped is not { } result) return;
        var (status, code, message) = result;
        context.Result = new ObjectResult(new ScopedRoleManagementController.Error(code, message)) { StatusCode = status };
        context.ExceptionHandled = true;
    }
}

internal sealed class ScopedRoleAntiforgeryFilter(IAntiforgery antiforgery,
    IAuthenticationSchemeProvider schemes) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var request = context.HttpContext.Request;
        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) ||
            HttpMethods.IsOptions(request.Method) || HttpMethods.IsTrace(request.Method)) return;
        if (!request.Headers.ContainsKey("Cookie")) return;
        var authenticatedScheme = context.HttpContext.Features.Get<IAuthenticateResultFeature>()?
            .AuthenticateResult?.Ticket?.AuthenticationScheme;
        var scheme = string.IsNullOrWhiteSpace(authenticatedScheme)
            ? null : await schemes.GetSchemeAsync(authenticatedScheme);
        var cookieAuthenticated = scheme is not null &&
            typeof(CookieAuthenticationHandler).IsAssignableFrom(scheme.HandlerType);
        var nonCookieAuthenticated = scheme is not null && !cookieAuthenticated;
        if (nonCookieAuthenticated) return;
        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            context.Result = new BadRequestObjectResult(
                new ScopedRoleManagementController.Error("antiforgery.invalid", "A valid antiforgery token is required."));
        }
    }
}
