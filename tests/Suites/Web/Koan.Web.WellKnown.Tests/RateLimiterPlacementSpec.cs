using System.Net;
using System.Threading.RateLimiting;
using Koan.Core;
using Koan.Web.Extensions;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Koan.Web.WellKnown.Tests;

/// <summary>
/// WEB-0075: <c>WithRateLimit()</c> places the rate limiter before the endpoints Koan maps, so it limits controller
/// routes and every <see cref="IKoanEndpointContributor"/> route (MCP among them), not only requests that match nothing.
/// </summary>
public sealed class RateLimiterPlacementSpec
{
    private const string ContributedPath = "/rate-limit-probe";

    [Theory]
    [InlineData("/health/live", true, HttpStatusCode.TooManyRequests)]
    [InlineData(ContributedPath, true, HttpStatusCode.TooManyRequests)]
    [InlineData("/health/live", false, HttpStatusCode.OK)]
    [InlineData(ContributedPath, false, HttpStatusCode.OK)]
    public async Task The_second_request_in_a_one_permit_window_is_limited_only_when_opted_in(
        string path, bool withRateLimit, HttpStatusCode expectedSecond)
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.UseEnvironment("Test");
                web.UseContentRoot(AppContext.BaseDirectory);
                web.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Koan:Environment"] = "Test",
                    ["Koan:BackgroundServices:Enabled"] = "false",
                    ["Logging:LogLevel:Default"] = "Warning"
                }));
                web.ConfigureServices(services =>
                {
                    services.AddKoan();
                    services.AddSingleton<IKoanEndpointContributor, ProbeContributor>();
                    services.AddRateLimiter(options =>
                    {
                        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                            RateLimitPartition.GetFixedWindowLimiter(context.Request.Path.Value ?? "", _ => new FixedWindowRateLimiterOptions
                            {
                                PermitLimit = 1,
                                Window = TimeSpan.FromMinutes(1),
                                QueueLimit = 0
                            }));
                    });
                    if (withRateLimit) services.WithRateLimit();
                });
                web.Configure(_ => { });
            })
            .Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            using var client = host.GetTestClient();
            using var first = await client.GetAsync(path, TestContext.Current.CancellationToken);
            using var second = await client.GetAsync(path, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(expectedSecond, second.StatusCode);
        }
        finally { await host.StopAsync(CancellationToken.None); }
    }

    private sealed class ProbeContributor : IKoanEndpointContributor
    {
        public void Map(IEndpointRouteBuilder endpoints)
            => endpoints.MapGet(ContributedPath, () => Results.Ok(new { probe = true }));
    }
}
