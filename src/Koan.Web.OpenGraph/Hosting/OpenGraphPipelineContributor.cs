using Koan.Web.Hosting;
using Microsoft.AspNetCore.Builder;

namespace Koan.Web.OpenGraph.Hosting;

/// <summary>
/// Places social-card rendering at Koan's early web-pipeline boundary. The middleware remains inert
/// until an application declares both a shell and a matching card.
/// </summary>
internal sealed class OpenGraphPipelineContributor : IKoanWebPipelineContributor
{
    public KoanWebPipelineStage Stage => KoanWebPipelineStage.BeforeRouting;

    public void Configure(IApplicationBuilder app) => app.UseOpenGraphCards();
}
