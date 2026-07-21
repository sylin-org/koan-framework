using Koan.Web.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Web.Context;

/// <summary>Mounts the one ordered request-context lifecycle after ASP.NET Core authentication.</summary>
internal sealed class WebContextPipelineContributor : IKoanWebPipelineContributor
{
    public KoanWebPipelineStage Stage => KoanWebPipelineStage.AfterAuthentication;

    public void Configure(IApplicationBuilder app)
    {
        app.Use(async (httpContext, next) =>
        {
            var context = new WebContext(httpContext);
            var entered = new List<IDisposable>();
            try
            {
                var contributors = httpContext.RequestServices
                    .GetServices<IWebContextContributor>()
                    .OrderBy(static contributor => contributor.Order)
                    .ToArray();

                foreach (var contributor in contributors)
                {
                    await contributor.ContributeAsync(context).ConfigureAwait(false);
                    if (context.IsRejected)
                    {
                        context.DiscardPending();
                        httpContext.Response.StatusCode = context.RejectionStatusCode;
                        return;
                    }

                    if (context.EnterPending() is { } scope)
                        entered.Add(scope);
                }

                await next().ConfigureAwait(false);
            }
            finally
            {
                context.DiscardPending();
                for (var index = entered.Count - 1; index >= 0; index--)
                    entered[index].Dispose();
            }
        });
    }
}
