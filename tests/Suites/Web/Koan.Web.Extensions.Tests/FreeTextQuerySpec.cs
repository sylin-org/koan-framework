using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Web.Hooks;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Koan.Web.Extensions.Tests;

/// <summary>
/// WEB-0074 - <c>?q=</c> is request text, not a provider query. A controller that did not declare otherwise (every terse
/// <c>[RestEntity]</c> controller cannot) refuses it with the correction; the one supported way to give it a meaning
/// is an application hook that turns it into a typed filter (WEB-0068) and clears it.
/// </summary>
[Collection(RestEntityCollection.Name)]
public sealed class FreeTextQuerySpec
{
    private readonly RestEntityWebFactory _fx;

    public FreeTextQuerySpec(RestEntityWebFactory fx) => _fx = fx;

    [Theory]
    [InlineData("1=0) UNION SELECT name, sql FROM sqlite_master --")]
    [InlineData("1=1; DROP TABLE rest_trinkets; --")]
    [InlineData("select 1; drop table rest_trinkets; --")]
    [InlineData("Anything")]
    public async Task A_terse_REST_entity_refuses_q_with_the_correction(string payload)
    {
        var response = await _fx.Client.GetAsync("/api/trinket?q=" + Uri.EscapeDataString(payload));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());
        ((string?)body["code"]).Should().Be("web.read.freeTextUnsupported");
        ((string?)body["message"]).Should().Contain("filter").And.Contain("IRequestOptionsHook");
    }

    [Fact]
    public async Task The_body_query_refuses_q_too()
    {
        var response = await _fx.Client.PostAsync("/api/trinket/query?q=" + Uri.EscapeDataString("1=1) --"),
            new StringContent("{}", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ((string?)JObject.Parse(await response.Content.ReadAsStringAsync())["code"]).Should().Be("web.read.freeTextUnsupported");
    }

    [Fact]
    public async Task A_blank_q_is_not_text()
    {
        foreach (var blank in new[] { "", "%20" })
            (await _fx.Client.GetAsync($"/api/trinket?q={blank}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_application_hook_gives_q_its_meaning_and_the_read_succeeds()
    {
        foreach (var label in new[] { "nk007 red kettle", "nk007 blue toaster", "nk007 green kettle" })
        {
            var created = await _fx.Client.PostAsync("/api/gizmos",
                new StringContent($"{{\"label\":\"{label}\"}}", Encoding.UTF8, "application/json"));
            created.IsSuccessStatusCode.Should().BeTrue(await created.Content.ReadAsStringAsync());
        }

        var response = await _fx.Client.GetAsync("/api/gizmos?q=" + Uri.EscapeDataString("kettle"));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var labels = JArray.Parse(await response.Content.ReadAsStringAsync())
            .Select(item => (string?)(item["label"] ?? item["Label"])).ToList();
        labels.Should().BeEquivalentTo("nk007 red kettle", "nk007 green kettle");
    }
}

/// <summary>Maps <c>q</c> to "Label contains" and clears it, so nothing is left for the endpoint to refuse.</summary>
internal sealed class GizmoLabelSearch : IRequestOptionsHook<Gizmo>
{
    public int Order => 0;

    public Task OnBuildingOptions(HookContext<Gizmo> context, QueryOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Q)) return Task.CompletedTask;
        var term = options.Q!;
        options.AddPredicate<Gizmo>(gizmo => gizmo.Label.Contains(term));
        options.Q = null;
        return Task.CompletedTask;
    }
}
