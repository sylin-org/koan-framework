using AwesomeAssertions;
using Koan.Web.Extensions;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Web.WellKnown.Tests;

/// <summary>ARCH-0144 with WEB-0081: a request the pipeline makes in process arrived on no origin, so the request in hand counts its
/// origin once, and one made with no request in hand notes nothing.</summary>
public sealed class InProcessOriginSpec
{
    [Fact]
    public async Task A_request_made_in_process_is_not_an_observed_origin()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await BaseUrlPathBaseSpec.BaseUrlHost.Start(new Dictionary<string, string?>(),
            services => services.AddKoanControllersFrom<InProcessOriginController>());
        var origins = host.Services.GetRequiredService<KoanWebOrigins>();

        var alone = await host.Services.GetRequiredService<KoanWebPipeline>().Send(new InProcessRequest(HttpMethods.Get, InProcessOriginController.Path), ct);
        using var client = host.Client;
        using var relayed = await client.GetAsync(InProcessOriginController.Path + "/relay", ct);

        alone.Status.Should().Be(StatusCodes.Status200OK);
        relayed.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        origins.Seen().Select(origin => (origin.Origin, origin.Count)).Should().Equal([("http://localhost", 1L)],
            "the request in hand arrived on its origin once, and the requests made in process arrived on none");
    }
}

[Route(Path)]
public sealed class InProcessOriginController(KoanWebPipeline pipeline) : ControllerBase
{
    public const string Path = "/in-process-origin";

    [HttpGet]
    public IActionResult Answer() => Ok();

    [HttpGet("relay")]
    public async Task<IActionResult> Relay(CancellationToken ct) => StatusCode((await pipeline.Send(new InProcessRequest(HttpMethods.Get, Path), ct)).Status);
}
