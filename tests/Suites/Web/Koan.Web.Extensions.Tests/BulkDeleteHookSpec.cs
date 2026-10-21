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
/// WEB-0077: every delete verb raises the model delete hooks. <see cref="LockerDeleteHook"/> refuses a locked row; a
/// bulk delete, a delete-by-query and a delete-all that include one are refused whole, and the AfterDelete count of a
/// bulk delete equals what it deleted. Each test uses unique tags in the shared store.
/// </summary>
[Collection(RestEntityCollection.Name)]
public sealed class BulkDeleteHookSpec
{
    private readonly RestEntityWebFactory _fx;

    public BulkDeleteHookSpec(RestEntityWebFactory fx) => _fx = fx;

    [Fact]
    public async Task A_locked_row_survives_a_bulk_delete()
    {
        var open = await Create("bulk-open");
        var locked = await Create("bulk-locked", locked: true);

        var res = await Send(HttpMethod.Delete, "/api/locker/bulk", $"[\"{open}\",\"{locked}\"]");

        res.StatusCode.Should().Be(HttpStatusCode.Conflict, await res.Content.ReadAsStringAsync());
        (await Exists(locked)).Should().BeTrue();
        (await Exists(open)).Should().BeTrue("a refused batch deletes nothing");
    }

    [Fact]
    public async Task A_locked_row_survives_a_delete_by_query()
    {
        var locked = await Create("dbq-locked", locked: true);

        var path = "/api/locker?q=" + System.Uri.EscapeDataString("{\"tag\":\"dbq-locked\"}");
        (await Send(HttpMethod.Delete, path)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Exists(locked)).Should().BeTrue();
    }

    [Fact]
    public async Task A_locked_row_survives_a_delete_all()
    {
        var locked = await Create("all-locked", locked: true);

        (await Send(HttpMethod.Delete, "/api/locker/all")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Exists(locked)).Should().BeTrue();
    }

    [Fact]
    public async Task A_bulk_delete_raises_after_delete_once_per_deleted_row()
    {
        var a = await Create("after-a");
        var b = await Create("after-b");

        var res = await Send(HttpMethod.Delete, "/api/locker/bulk", $"[\"{a}\",\"{b}\"]");
        res.IsSuccessStatusCode.Should().BeTrue(await res.Content.ReadAsStringAsync());
        JObject.Parse(await res.Content.ReadAsStringAsync())["deleted"]!.Value<int>().Should().Be(2);

        LockerDeleteHook.After.Count(tag => tag is "after-a" or "after-b").Should().Be(2);
    }

    private async Task<string> Create(string tag, bool locked = false)
    {
        var res = await Send(HttpMethod.Post, "/api/locker", $"{{\"tag\":\"{tag}\",\"locked\":{(locked ? "true" : "false")}}}");
        res.IsSuccessStatusCode.Should().BeTrue(await res.Content.ReadAsStringAsync());
        var o = JObject.Parse(await res.Content.ReadAsStringAsync());
        return (string)(o["id"] ?? o["Id"])!;
    }

    private async Task<bool> Exists(string id)
        => (await Send(HttpMethod.Get, $"/api/locker/{id}")).StatusCode == HttpStatusCode.OK;

    private Task<HttpResponseMessage> Send(HttpMethod method, string path, string? json = null)
    {
        var req = new HttpRequestMessage(method, path);
        if (json is not null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return _fx.Client.SendAsync(req);
    }
}
