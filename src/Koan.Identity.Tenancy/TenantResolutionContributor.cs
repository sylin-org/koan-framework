using Microsoft.AspNetCore.Builder;
using Koan.Web.Hosting;

namespace Koan.Identity.Tenancy;

/// <summary>
/// SEC-0007 P4 — mounts <see cref="TenantResolutionMiddleware"/> at the <c>AfterAuthentication</c> pipeline stage
/// (after the subject is established, before authorization), ordered to run after dev-identity / claims-enrichment
/// contributors so the resolved subject is final. WEB-0069: the ordering-safe seam (never <c>IStartupFilter</c> order).
/// </summary>
internal sealed class TenantResolutionContributor : IKoanWebPipelineContributor
{
    public KoanWebPipelineStage Stage => KoanWebPipelineStage.AfterAuthentication;

    public int Order => 100;

    public void Configure(IApplicationBuilder app) => app.UseMiddleware<TenantResolutionMiddleware>();
}
