using Koan.Core;
using Koan.Web.Options;
using Microsoft.Extensions.Hosting;

namespace Koan.Web.Infrastructure;

/// <summary>
/// SEC-0018: whether a host discloses its runtime self-description (the observability snapshot, the runtime facts):
/// in Development, or when <see cref="KoanWebOptions.ExposeObservabilitySnapshot"/> says so. The well-known endpoints
/// and the MCP <c>koan://facts</c> resource ask this one decision.
/// </summary>
public static class ObservabilityExposure
{
    public static bool Allowed(IHostEnvironment? environment, KoanWebOptions? options)
        => KoanEnv.Gate.DevelopmentOnly(environment) || options?.ExposeObservabilitySnapshot == true;
}
