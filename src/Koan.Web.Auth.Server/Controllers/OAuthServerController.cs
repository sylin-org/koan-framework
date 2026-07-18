using Koan.Web.Auth.Server.Hosting;
using Koan.Web.Auth.Server.Infrastructure;
using Koan.Web.Auth.Server.Protocol;
using Microsoft.AspNetCore.Mvc;

namespace Koan.Web.Auth.Server.Controllers;

/// <summary>
/// Controller-owned HTTP boundary for the embedded OAuth 2.1 authorization server. Protocol mechanics remain in
/// their concern-specific handlers; this type is the single route/method inventory.
/// </summary>
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class OAuthServerController : ControllerBase
{
    [HttpGet(AuthServerRoutes.Authorize)]
    public Task Authorize() => OAuthProtocolEndpoints.Authorize(HttpContext);

    [HttpGet(AuthServerRoutes.Request)]
    public Task GetRequest([FromRoute] string rid) => OAuthProtocolEndpoints.GetRequest(HttpContext, rid);

    [HttpPost(AuthServerRoutes.Approve)]
    public Task Approve([FromRoute] string rid) => OAuthProtocolEndpoints.Approve(HttpContext, rid);

    [HttpPost(AuthServerRoutes.Deny)]
    public Task Deny([FromRoute] string rid) => OAuthProtocolEndpoints.Deny(HttpContext, rid);

    [HttpPost(AuthServerRoutes.Token)]
    public Task Token() => OAuthProtocolEndpoints.Token(HttpContext);

    [HttpPost(AuthServerRoutes.Device)]
    public Task Device() => DeviceEndpoint.Device(HttpContext);

    [HttpPost(AuthServerRoutes.Register)]
    public Task Register() => DcrEndpoint.Register(HttpContext);

    [HttpGet(AuthServerRoutes.DevToken)]
    public Task DevToken() => DevTokenEndpoint.HandleAsync(HttpContext);

    [HttpGet(AuthServerRoutes.AuthorizationServerMetadata)]
    [HttpGet(AuthServerRoutes.OpenIdConfiguration)]
    public Task Metadata() => WellKnownEndpoints.Metadata(HttpContext);

    [HttpGet(AuthServerRoutes.Jwks)]
    public Task Jwks() => WellKnownEndpoints.Jwks(HttpContext);
}
