using Koan.Core;
using Koan.Web.Hosting;
using Koan.Web.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Web.Middleware;

/// <summary>
/// Mounts <see cref="KoanCacheControlMiddleware"/> before routing, so the request's cache behaviour is
/// established by the time any Entity call runs.
/// </summary>
/// <remarks>
/// Honouring a caller's cache headers is convenient everywhere and risky only where the callers are not
/// the application's own: any client can then force an expensive query to miss. That is the
/// <see cref="KoanMagic"/> shape, so the decision goes to <c>KoanEnv.Gate</c> rather than being re-derived
/// here (ARCH-0128). Skipping is a coherent outcome — the application still serves, it just stops taking
/// cache instructions from callers — so this announces rather than refusing the host.
/// </remarks>
internal sealed class CacheControlPipelineContributor(
    IOptions<KoanCacheControlOptions> options,
    IHostEnvironment environment,
    ILogger<CacheControlPipelineContributor> logger) : IKoanWebPipelineContributor
{
    public KoanWebPipelineStage Stage => KoanWebPipelineStage.BeforeRouting;

    public void Configure(IApplicationBuilder app)
    {
        var magic = new KoanMagic(
            Capability: "client-steered cache behaviour",
            Risk: "any caller can send Cache-Control: no-store and force this application's cached reads to miss.",
            Remedy: $"set {KoanCacheControlOptions.SectionPath}:{nameof(KoanCacheControlOptions.HonorClientHeaders)} once the callers are known",
            Consent: options.Value.HonorClientHeaders);

        if (!KoanEnv.Gate.Announce(magic, logger, environment))
        {
            return;
        }

        app.UseMiddleware<KoanCacheControlMiddleware>();
    }
}
