using AwesomeAssertions;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Hosting.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Koan.Mcp.Verbs.Tests;

/// <summary>The host builds its pipeline into a pipeline of its own, so the one a tool call is sent through is never built: as in a process
/// that serves no web requests, while <c>/mcp</c> still answers.</summary>
public sealed class UnbuiltPipelineFixture : ForumFixture
{
    protected override void Compose(IServiceCollection services)
        => services.Replace(ServiceDescriptor.Singleton<IApplicationBuilderFactory>(provider =>
            new KoanWebPipeline(provider, provider.GetRequiredService<IHttpContextAccessor>())));
}

/// <summary>ARCH-0144: a tool call whose pipeline was never built is refused with the one <c>Problem</c> the pipeline answers, which the
/// call carries as any route's refusal.</summary>
public sealed class UnbuiltPipelineSpec(UnbuiltPipelineFixture host) : IClassFixture<UnbuiltPipelineFixture>
{
    [Fact]
    public async Task A_tool_call_carries_the_503_problem_in_its_structured_content_and_meta()
    {
        var result = await host.Call("request_read", new JObject { ["q"] = "x" }, "bob");

        result["isError"]!.Value<bool>().Should().BeTrue(result.ToString());
        result["content"]![0]!["text"]!.Value<string>().Should().Be("This process serves no requests.");
        foreach (var problem in new[] { result["structuredContent"], result["_meta"]!["problem"] })
        {
            problem!["status"]!.Value<int>().Should().Be(503, result.ToString());
            problem["kind"]!.Value<string>().Should().Be("failed");
            problem["detail"]!.Value<string>().Should().Be("This process serves no requests.");
        }
        result["_meta"]!["errorCode"]!.Value<string>().Should().Be("failed");
    }
}
