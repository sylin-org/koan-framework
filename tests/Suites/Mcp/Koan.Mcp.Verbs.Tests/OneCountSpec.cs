using System.Threading.RateLimiting;
using AwesomeAssertions;
using Koan.Web.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Koan.Mcp.Verbs.Tests;

/// <summary>ARCH-0141: a global limiter counts a tool call once, at the edge, so a concurrency limit does not wait on itself.</summary>
public sealed class OneCountSpec
{
    private sealed class Limited(int queue) : ForumFixture
    {
        protected override void Compose(IServiceCollection services)
        {
            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = 429;
                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
                    RateLimitPartition.GetConcurrencyLimiter("all", _ => new ConcurrencyLimiterOptions { PermitLimit = 1, QueueLimit = queue }));
            });
            services.Configure<WebPipelineOptions>(pipeline => pipeline.UseRateLimiter = true);
        }
    }

    [Theory]
    [InlineData(5)]
    [InlineData(0)]
    public async Task A_tool_call_answers_under_one_permit(int queue)
    {
        await using var host = new Limited(queue);
        await host.InitializeAsync();

        var route = await host.Route(HttpMethod.Get, "/fidelity/whoami", null, "sam");
        var tool = await host.Tool("whoami_get", new JObject(), "sam").WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        route.Status.Should().Be(200, route.ToString());
        tool.Status.Should().Be(200, tool.ToString());
    }
}
