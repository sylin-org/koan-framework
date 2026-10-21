using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using AwesomeAssertions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Koan.Web.Extensions.Tests;

/// <summary>
/// WEB-0076: <c>POST /query</c> is always paged and clamped to the controller's policy (default 50, maximum 200). A size
/// of 0, a huge size or page 0 used to read the whole table in one response.
/// </summary>
[Collection(RestEntityCollection.Name)]
public sealed class QueryBoundsSpec
{
    private const int Rows = 230;
    private readonly RestEntityWebFactory _fx;

    public QueryBoundsSpec(RestEntityWebFactory fx) => _fx = fx;

    [Theory]
    [InlineData("{\"size\":0}", 50)]
    [InlineData("{\"page\":0}", 50)]
    [InlineData("{\"size\":-5}", 50)]
    [InlineData("{\"size\":100000}", 200)]
    [InlineData("{\"size\":2147483647}", 200)]
    [InlineData("{\"page\":2,\"size\":100}", 100)]
    public async Task A_body_query_never_returns_more_than_the_policy_allows(string body, int expected)
    {
        await Seed();

        var res = await _fx.Client.PostAsync("/api/pebble/query", new StringContent(body, Encoding.UTF8, "application/json"));
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        JArray.Parse(await res.Content.ReadAsStringAsync()).Should().HaveCount(expected, body);
    }

    private async Task Seed()
    {
        var existing = JArray.Parse(await (await _fx.Client.GetAsync("/api/pebble?pageSize=1")).Content.ReadAsStringAsync());
        if (existing.Count > 0) return;
        var rows = new JArray(Enumerable.Range(0, Rows).Select(i => new JObject { ["name"] = $"p{i:000}" }));
        var seeded = await _fx.Client.PostAsync("/api/pebble/bulk", new StringContent(rows.ToString(), Encoding.UTF8, "application/json"));
        seeded.IsSuccessStatusCode.Should().BeTrue(await seeded.Content.ReadAsStringAsync());
    }
}
