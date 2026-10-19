using System.Linq;
using System.Threading.Tasks;
using Koan.Mcp.TestKit;
using Koan.Web.Endpoints;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>
/// WEB-0074 - the one supported way to give <c>q</c> a meaning (WEB-0068): the application's own request-options hook
/// turns the text into a typed filter and clears it, so there is nothing left for the shared endpoint to refuse. The
/// text reaches the data layer as a predicate the application wrote, never as a provider query.
/// </summary>
public sealed class RequestTextHookSpec : IClassFixture<QueryTextFixture>
{
    private readonly QueryTextFixture _fx;

    public RequestTextHookSpec(QueryTextFixture fx) => _fx = fx;

    [Fact]
    public async Task A_request_options_hook_turns_q_into_a_filter_and_the_read_succeeds()
    {
        await Ticket.UpsertMany([
            new Ticket { Id = "t1", Title = "printer on fire" },
            new Ticket { Id = "t2", Title = "password reset" },
            new Ticket { Id = "t3", Title = "printer out of paper" },
        ]);
        var tool = _fx.ResolveToolName("ticket", EntityEndpointOperationKind.Collection);

        var call = await _fx.CallToolAsync(tool, new JObject { ["q"] = "printer" });

        McpHarnessFixtureBase.IsError(call).Should().BeFalse(call.ToString());
        McpHarnessFixtureBase.IsShortCircuited(call).Should().BeFalse(call.ToString());
        var titles = JToken.Parse(McpHarnessFixtureBase.ContentText(call)!)
            .Select(item => item["title"]!.Value<string>()).ToList();
        titles.Should().BeEquivalentTo("printer on fire", "printer out of paper");
    }
}
