using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Koan.Security.Trust.Dev;
using Koan.Web.Context;

namespace Koan.Web.Auth.Hosting;

/// <summary>
/// SEC-0001 §4 (Rung 0) — contributes the zero-config dev identity into Koan's pipeline between authentication
/// and authorization, Development-only (never in production — the §4.2 fail-closed invariant).
/// </summary>
internal sealed class DevIdentityContributor(
    IHostEnvironment environment,
    IOptions<DevIdentityOptions> options) : IWebContextContributor
{
    public int Order => 0;

    public ValueTask ContributeAsync(WebContext context)
    {
        if (environment.IsDevelopment()
            && DevIdentity.Resolve(context.HttpContext, options.Value) is { } principal)
            context.UsePrincipal(principal);
        return ValueTask.CompletedTask;
    }
}
