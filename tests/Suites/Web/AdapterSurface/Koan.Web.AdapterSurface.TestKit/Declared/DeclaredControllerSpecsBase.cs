using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Xunit;

namespace Koan.Web.AdapterSurface.TestKit.Declared;

/// <summary>
/// WEB-0087: a declared <c>EntityController</c> serves what it declares. Only its overrides are routes, each at its own route; a create only
/// creates and writes what it declares, taking its input from the same body; a change keeps what it leaves out; an address may name a row
/// by another key, or be the one row of a singleton; a gate on an override is judged; the controller is a hook of its own requests and of no
/// other controller's; and a partition no controller declares is refused on every controller.
/// </summary>
public abstract class DeclaredControllerSpecsBase<TFactory> : IClassFixture<TFactory>
    where TFactory : class, IAdapterTestFactory
{
    protected DeclaredControllerSpecsBase(TFactory factory) => Factory = factory;

    protected TFactory Factory { get; }

    private HttpClient Client => Factory.Client;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Only_the_operations_it_overrides_are_routes()
    {
        var id = await Create("Unserved operations");

        // The collection's address answers a create, so a read there is a method it does not allow; every other address answers nothing.
        (await Client.GetAsync("/api/cards", Ct)).StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        foreach (var (method, path) in new[]
                 {
                     (HttpMethod.Put, $"/api/cards/{id}"),
                     (HttpMethod.Delete, $"/api/cards/{id}"),
                     (HttpMethod.Delete, "/api/cards/all"),
                     (HttpMethod.Post, "/api/cards/bulk"),
                     (HttpMethod.Post, "/api/cards/query"),
                     (HttpMethod.Get, "/api/cards/new"),
                 })
        {
            using var request = new HttpRequestMessage(method, path) { Content = method == HttpMethod.Get ? null : Json(new { }) };
            var answer = await Client.SendAsync(request, Ct);
            answer.StatusCode.Should().Be(HttpStatusCode.NotFound, $"{method} {path} is not served");
        }

        (await Client.GetAsync($"/api/cards/by/{id}", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_override_that_states_its_route_answers_there_alone()
    {
        var id = await Create("Own route");

        (await Client.GetAsync($"/api/cards/by/{id}", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Client.GetAsync($"/api/cards/{id}", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound, "the inherited {id} route is replaced");
    }

    [Fact]
    public async Task An_address_names_its_row_by_another_key()
    {
        var id = await Create("Reached By Slug");

        var card = await Read($"/api/cards/by/reached-by-slug");
        card.GetProperty("id").GetString().Should().Be(id);
        (await Client.GetAsync("/api/cards/by/no-such-card", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_singleton_serves_its_one_row_at_its_own_address()
    {
        (await Client.PostAsJsonAsync("/api/plain-cards", new { id = FrontCardController.Front, title = "The front" }, Ct)).EnsureSuccessStatusCode();

        var card = await Read("/api/front-card");
        card.GetProperty("title").GetString().Should().Be("The front");
    }

    [Fact]
    public async Task A_create_only_creates()
    {
        var id = Guid.CreateVersion7().ToString("N");
        (await Post(new { id, title = "First" })).StatusCode.Should().Be(HttpStatusCode.OK);

        var again = await Post(new { id, title = "Second" });

        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Problem(again)).GetProperty("word").GetString().Should().Be("id-taken");
        (await Read($"/api/cards/by/{id}")).GetProperty("title").GetString().Should().Be("First");
    }

    [Fact]
    public async Task A_create_writes_what_it_declares_and_its_hook_reads_the_input_it_takes()
    {
        var answer = await Post(new { title = "Placed Card", place = "top" });

        answer.StatusCode.Should().Be(HttpStatusCode.OK, await answer.Content.ReadAsStringAsync(Ct));
        var card = await answer.Content.ReadFromJsonAsync<JsonElement>(Ct);
        card.GetProperty("shelf").GetString().Should().Be("top");
        card.GetProperty("slug").GetString().Should().Be("placed-card");
    }

    [Fact]
    public async Task A_member_outside_the_write_set_is_refused_with_its_field()
    {
        var answer = await Post(new { title = "Forged", slug = "elsewhere" });

        answer.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        answer.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var problem = await Problem(answer);
        problem.GetProperty("errors").GetProperty("slug")[0].GetString().Should().Be("This address does not write 'slug'.");
    }

    [Fact]
    public async Task An_input_that_cannot_be_its_value_is_refused_with_its_field()
    {
        var answer = await Post(new { title = "Counted", copies = "many" });

        answer.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Problem(answer)).GetProperty("errors").TryGetProperty("copies", out _).Should().BeTrue();
    }

    [Fact]
    public async Task A_change_keeps_what_it_leaves_out_and_refuses_what_it_does_not_write()
    {
        var id = await Create("Before change", place: "middle");

        var changed = await Patch($"/api/cards/by/{id}", new { title = "After change" });
        changed.StatusCode.Should().Be(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync(Ct));
        var card = await Read($"/api/cards/by/{id}");
        card.GetProperty("title").GetString().Should().Be("After change");
        card.GetProperty("shelf").GetString().Should().Be("middle");
        card.GetProperty("edits").GetInt32().Should().Be(1);

        var forged = await Patch($"/api/cards/by/{id}", new { shelf = "bottom" });
        forged.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Problem(forged)).GetProperty("errors").TryGetProperty("shelf", out _).Should().BeTrue();
        (await Read($"/api/cards/by/{id}")).GetProperty("shelf").GetString().Should().Be("middle");
    }

    [Fact]
    public async Task A_gate_its_override_declares_is_judged_though_it_is_no_tool()
    {
        var id = await Create("Kept card");

        var answer = await Client.DeleteAsync($"/api/cards/by/{id}", Ct);

        answer.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Client.GetAsync($"/api/cards/by/{id}", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_controller_is_a_hook_of_its_own_requests_and_of_no_other_controllers()
    {
        var answer = await Client.PostAsJsonAsync("/api/plain-cards", new { title = "Plain Card" }, Ct);

        answer.StatusCode.Should().Be(HttpStatusCode.OK);
        var card = await answer.Content.ReadFromJsonAsync<JsonElement>(Ct);
        card.GetProperty("slug").GetString().Should().BeEmpty();
        card.GetProperty("shelf").GetString().Should().BeEmpty();
    }

    [Fact]
    public async Task A_partition_no_controller_declares_is_refused_on_every_controller()
    {
        var id = await Create("Partitioned");

        foreach (var path in new[] { $"/api/cards/by/{id}?set=audit", $"/api/plain-cards/{id}?set=audit", "/api/plain-cards?set=audit" })
        {
            var answer = await Client.GetAsync(path, Ct);
            answer.StatusCode.Should().Be(HttpStatusCode.BadRequest, path);
            (await Problem(answer)).GetProperty("errors").GetProperty("set")[0].GetString().Should().Be("This address serves no partition 'audit'.");
        }
    }

    [Fact]
    public async Task A_query_word_the_operation_does_not_take_is_refused_with_its_field()
    {
        var id = await Create("Unasked words");

        var answer = await Client.GetAsync($"/api/cards/by/{id}?with=all", Ct);

        answer.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Problem(answer)).GetProperty("errors").GetProperty("with")[0].GetString().Should().Be("This address takes no 'with'.");
        (await Client.GetAsync($"/api/plain-cards/{id}?with=all", Ct)).StatusCode.Should().Be(HttpStatusCode.OK, "an undeclared controller takes what it always took");
    }

    private async Task<string> Create(string title, string? place = null)
    {
        var answer = await Post(new { title, place });
        answer.StatusCode.Should().Be(HttpStatusCode.OK, await answer.Content.ReadAsStringAsync(Ct));
        return (await answer.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!;
    }

    private Task<HttpResponseMessage> Post(object body) => Client.PostAsJsonAsync("/api/cards", body, Ct);

    private Task<HttpResponseMessage> Patch(string path, object body)
        => Client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, path) { Content = Json(body) }, Ct);

    private async Task<JsonElement> Read(string path)
    {
        var answer = await Client.GetAsync(path, Ct);
        answer.StatusCode.Should().Be(HttpStatusCode.OK, await answer.Content.ReadAsStringAsync(Ct));
        return await answer.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static StringContent Json(object body) => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    // A collection that answers JSON whatever is asked keeps its refusal JSON too; the document is the same.
    private static async Task<JsonElement> Problem(HttpResponseMessage answer)
    {
        answer.Content.Headers.ContentType?.MediaType.Should().BeOneOf("application/problem+json", "application/json");
        return await answer.Content.ReadFromJsonAsync<JsonElement>();
    }
}
