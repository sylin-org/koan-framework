namespace Koan.Web.Auth.Infrastructure;

/// <summary>
/// SEC-0025 — marks a controller or action that is not part of the application's resource: it never authenticates the
/// request by a bearer, so the resource edge's answer to a rejected token (401 <c>Bearer error="invalid_token"</c> before
/// the action) does not apply to it. The embedded authorization server's protocol endpoints carry it: a client that
/// attaches a stale token by default still refreshes, registers, starts a device request and reads the discovery
/// documents, and gets their own protocol answers. A provider endpoint that checks a token of its own — the dev Test
/// provider's authorize, token, userinfo and discovery endpoints — must carry it too, or the edge refuses that token
/// before the endpoint sees it.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
public sealed class ResourceServerExemptAttribute : Attribute;
