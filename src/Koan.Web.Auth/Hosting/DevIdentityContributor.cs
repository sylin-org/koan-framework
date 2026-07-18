using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Web.Hosting;
using Koan.Security.Trust.Dev;

namespace Koan.Web.Auth.Hosting;

/// <summary>
/// SEC-0001 §4 (Rung 0) — contributes the zero-config dev identity into Koan's pipeline between authentication
/// and authorization, Development-only (never in production — the §4.2 fail-closed invariant).
/// </summary>
internal sealed class DevIdentityContributor : IKoanWebPipelineContributor
{
    public KoanWebPipelineStage Stage => KoanWebPipelineStage.AfterAuthentication;

    public void Configure(IApplicationBuilder app)
    {
        if (app.ApplicationServices.GetService<IHostEnvironment>()?.IsDevelopment() == true)
            app.UseKoanDevIdentity();
    }
}
