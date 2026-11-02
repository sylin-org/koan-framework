using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Mcp.Execution;
using Koan.Mcp.TestKit;
using Koan.Web.Endpoints;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>
/// WEB-0074 - an agent can be steered into any argument it is offered. The MCP edge used to advertise <c>q</c> as a
/// "full-text query term" and forward it to the data layer, where a relational adapter ran it as SQL. These pin the
/// contract that replaced it: <c>q</c> is not offered, and a <c>q</c> that arrives anyway is refused by the shared
/// endpoint with the correction instead of reaching the adapter's raw-query path.
/// </summary>
public sealed class RawQueryRefusalSpec : IClassFixture<ConformanceFixture>
{
    // Hostile in some engine's dialect and meaningless in the others; the refusal does not depend on which.
    private static readonly string[] ProviderQueryPayloads =
    [
        "1=0) UNION SELECT name, sql FROM sqlite_master --",
        "1=1; DROP TABLE conformance_gadgets; --",
        "select 1; drop table conformance_gadgets; --",
        "1 = 1",
        "gizmo",
    ];

    private readonly ConformanceFixture _fx;

    public RawQueryRefusalSpec(ConformanceFixture fx) => _fx = fx;

    [Theory]
    [InlineData(EntityEndpointOperationKind.Collection)]
    [InlineData(EntityEndpointOperationKind.Query)]
    public void No_read_tool_advertises_q(EntityEndpointOperationKind operation)
    {
        var schema = _fx.GetToolInputSchema(_fx.ResolveToolName("gadget", operation));

        schema["properties"]!["q"].Should().BeNull("q is not an MCP argument; the structured filter is");
        schema["properties"]!["filter"].Should().NotBeNull();
    }

    [Theory]
    [InlineData(EntityEndpointOperationKind.Collection)]
    [InlineData(EntityEndpointOperationKind.Query)]
    public async Task The_translator_carries_q_for_hooks_but_never_grants_the_raw_query_opt_in(
        EntityEndpointOperationKind operation)
    {
        var registry = _fx.Services.GetRequiredService<McpEntityRegistry>();
        registry.TryGetRegistration("gadget", out var registration).Should().BeTrue();
        var tool = registration.Tools.Single(candidate => candidate.Operation == operation);

        using var scope = _fx.Services.CreateScope();
        var translation = await new RequestTranslator().Translate(scope.ServiceProvider, registration, tool,
            new JObject { ["q"] = "1=1) UNION SELECT 1 --" }, CancellationToken.None);

        var (allowRawQuery, context) = translation.Request switch
        {
            EntityCollectionRequest collection => (collection.AllowRawQuery, collection.Context),
            EntityQueryRequest query => (query.AllowRawQuery, query.Context),
            var other => throw new InvalidOperationException($"Unexpected request type {other.GetType().Name}.")
        };
        allowRawQuery.Should().BeFalse("the MCP edge never grants the raw-query opt-in");
        context.Options.Q.Should().Be("1=1) UNION SELECT 1 --", "q is carried so an application hook can give it a meaning");
    }

    [Theory]
    [InlineData(EntityEndpointOperationKind.Collection)]
    [InlineData(EntityEndpointOperationKind.Query)]
    public async Task A_q_argument_is_refused_with_the_correction_and_never_runs_as_a_provider_query(
        EntityEndpointOperationKind operation)
    {
        var tool = _fx.ResolveToolName("gadget", operation);

        foreach (var payload in ProviderQueryPayloads)
        {
            var call = await _fx.CallToolAsync(tool, new JObject { ["q"] = payload });

            McpHarnessFixtureBase.IsShortCircuited(call).Should().BeTrue($"q={payload}: {call}");
            call["_meta"]!["diagnostics"]!["shortCircuitStatusCode"]!.Value<int>().Should().Be(400);
            var refusal = call["_meta"]!["shortCircuit"]!["payload"]!;
            refusal["code"]!.Value<string>().Should().Be("web.read.freeTextUnsupported");
            refusal["message"]!.Value<string>().Should().Contain("filter", "the refusal names the fix");
            call.ToString().Should().NotContain("does not support raw",
                "the adapter's own refusal would mean the text reached the raw-query path");
        }
    }

    [Fact]
    public async Task A_blank_q_argument_carries_no_text_and_reads_normally()
    {
        var tool = _fx.ResolveToolName("gadget", EntityEndpointOperationKind.Collection);

        var call = await _fx.CallToolAsync(tool, new JObject { ["q"] = "  " });

        McpHarnessFixtureBase.IsError(call).Should().BeFalse(call.ToString());
        McpHarnessFixtureBase.IsShortCircuited(call).Should().BeFalse(call.ToString());
    }
}
