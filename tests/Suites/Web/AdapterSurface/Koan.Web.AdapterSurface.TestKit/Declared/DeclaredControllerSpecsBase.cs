using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Koan.Data.Core;
using Xunit;

namespace Koan.Web.AdapterSurface.TestKit.Declared;

/// <summary>
/// WEB-0087: a declared <c>EntityController</c> serves what it declares. Only its overrides are routes, each at its own route; a create only
/// creates and writes what it declares, taking its input from the same body; a change keeps what it leaves out; an address may name a row
/// by another key, or be the one row of a singleton; a gate on an override is judged; the controller is a hook of its own requests and of no
/// other controller's; and a partition no controller declares is refused on every controller. WEB-0088: its writes take a page's form,
/// which names the method it cannot send, and answer through <c>Answers.Written</c>: a form is sent on, JSON reads the row created or
/// changed, and a refused form keeps what it sent.
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
        (await Post(new { id, title = "First" })).StatusCode.Should().Be(HttpStatusCode.Created);

        var again = await Post(new { id, title = "Second" });

        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Problem(again)).GetProperty("word").GetString().Should().Be("id-taken");
        (await Read($"/api/cards/by/{id}")).GetProperty("title").GetString().Should().Be("First");
    }

    [Fact]
    public async Task A_create_sent_again_answers_as_the_first_did_where_its_hook_knows_the_replay()
    {
        var id = Guid.CreateVersion7().ToString("N");
        var first = await Client.PostAsJsonAsync("/api/card-replays", new { id, title = "Once" }, Ct);
        first.StatusCode.Should().Be(HttpStatusCode.Created, await first.Content.ReadAsStringAsync(Ct));

        var again = await Client.PostAsJsonAsync("/api/card-replays", new { id, title = "Once" }, Ct);
        again.StatusCode.Should().Be(HttpStatusCode.Created, await again.Content.ReadAsStringAsync(Ct));
        again.Headers.Location!.OriginalString.Should().Be(first.Headers.Location!.OriginalString);
        var card = await again.Content.ReadFromJsonAsync<JsonElement>(Ct);
        card.GetProperty("id").GetString().Should().Be(id);
        card.GetProperty("title").GetString().Should().Be("Once");

        var other = await Client.PostAsJsonAsync("/api/card-replays", new { id, title = "Twice" }, Ct);
        other.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Problem(other)).GetProperty("word").GetString().Should().Be("id-taken");
        (await Read($"/api/cards/by/{id}")).GetProperty("title").GetString().Should().Be("Once");
    }

    [Fact]
    public async Task A_create_writes_what_it_declares_and_its_hook_reads_the_input_it_takes()
    {
        var answer = await Post(new { title = "Placed Card", place = "top" });

        answer.StatusCode.Should().Be(HttpStatusCode.Created, await answer.Content.ReadAsStringAsync(Ct));
        answer.Headers.Location!.OriginalString.Should().Be("/api/cards/by/placed-card");
        answer.Headers.CacheControl!.NoStore.Should().BeTrue();
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
        changed.Headers.Location!.OriginalString.Should().Be("/api/cards/by/before-change");
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

        // A query reads the partition its body names over the one its address names, so each is judged.
        foreach (var path in new[] { "/api/plain-cards/query", "/api/plain-cards/query?set=" })
        {
            var answer = await Client.PostAsJsonAsync(path, new { set = "audit" }, Ct);
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

    /// <summary>DATA-0170: a (text, id) order returns every row present for the whole reading once, a row inserted ahead of the cursor
    /// too, and none inserted behind it, across a tie on the text at a page's edge.</summary>
    [Fact]
    public async Task A_list_by_text_continues_from_its_sealed_cursor_and_reads_every_row_once_across_an_insert()
    {
        var board = Board();
        await Pins(board, ("b", 0), ("d", 0), ("f", 0), ("f", 0), ("h", 0), ("j", 0));

        var first = await List($"/api/pins/by-label?on={board}");
        Labels(first).Should().Equal("b", "d");
        await Pins(board, ("e", 0), ("a", 0));
        var labels = new List<string>(Labels(first));
        for (var next = Next(first); next is not null;)
        {
            var page = await List($"/api/pins/by-label?on={board}&after={Uri.EscapeDataString(next)}");
            labels.AddRange(Labels(page));
            next = Next(page);
        }

        labels.Should().Equal(["b", "d", "e", "f", "f", "h", "j"], "the row inserted ahead is read, the one behind is not, and the tie at the edge loses nothing");
    }

    /// <summary>DATA-0170: a (date desc, id) order does the same, two instants shared, through the page its emit hook composes from
    /// <c>ctx.Next</c>.</summary>
    [Fact]
    public async Task A_list_newest_first_continues_through_the_page_its_emit_composes()
    {
        var board = Board();
        await Pins(board, ("t5", 5), ("t4", 4), ("t3a", 3), ("t3b", 3), ("t2", 2), ("t1", 1));

        var labels = new List<string>();
        string? address = $"/api/pins/newest?on={board}";
        var pages = 0;
        while (address is not null)
        {
            var page = await List(address);
            labels.AddRange(page.GetProperty("labels").EnumerateArray().Select(label => label.GetString()!));
            address = page.TryGetProperty("older", out var older) ? older.GetString() : null;
            if (++pages == 1) await Pins(board, ("t9", 9), ("t3c", 3), ("t0", 0));
        }

        // The identity follows the order's direction, so pins of one instant come by descending id: t3c, t3b, t3a.
        labels.Should().Equal(["t5", "t4", "t3c", "t3b", "t3a", "t2", "t1", "t0"],
            "every row present for the whole reading comes once, newest first; the newer one behind the cursor is not read, the ones ahead are");
    }

    [Fact]
    public async Task An_after_that_does_not_open_is_refused_naming_it()
    {
        var board = Board();
        await Pins(board, ("a", 0), ("b", 0), ("c", 0));
        var next = Next(await List($"/api/pins/by-label?on={board}"))!;
        var middle = next.Length / 2;
        var altered = next[..middle] + (next[middle] == 'A' ? 'B' : 'A') + next[(middle + 1)..];

        foreach (var path in new[]
                 {
                     $"/api/pins/by-label?on={board}&after={Uri.EscapeDataString(altered)}",
                     $"/api/pins/by-label?on={board}&after=not-a-cursor",
                     $"/api/pins/newest?on={board}&after={Uri.EscapeDataString(next)}",
                 })
        {
            var answer = await Client.GetAsync(path, Ct);
            answer.StatusCode.Should().Be(HttpStatusCode.BadRequest, path);
            (await Problem(answer)).GetProperty("errors").TryGetProperty("after", out _).Should().BeTrue(path);
        }
    }

    [Fact]
    public async Task A_declared_list_counts_nothing_and_pages_by_nothing_but_its_cursor()
    {
        var board = Board();
        await Pins(board, ("a", 0), ("b", 0), ("c", 0), ("d", 0));

        var answer = await Client.GetAsync($"/api/pins/by-label?on={board}&size=50", Ct);
        answer.StatusCode.Should().Be(HttpStatusCode.OK);
        answer.Headers.Select(header => header.Key).Should().NotContain(["X-Total-Count", "X-Total-Pages", "X-Page", "X-Page-Size", "Link"]);
        Labels(await answer.Content.ReadFromJsonAsync<JsonElement>(Ct)).Should().Equal(["a", "b", "c"], "a page holds at most its bound");

        var paged = await Client.GetAsync($"/api/pins/by-label?on={board}&page=2", Ct);
        paged.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Problem(paged)).GetProperty("errors").TryGetProperty("page", out _).Should().BeTrue();
        var sized = await Client.GetAsync($"/api/pins/by-label?on={board}&size=many", Ct);
        sized.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Problem(sized)).GetProperty("errors").TryGetProperty("size", out _).Should().BeTrue();
    }

    private static string Board() => Guid.CreateVersion7().ToString("N");

    private static int _pinned;

    private static readonly DateTimeOffset Pinned = new DateTimeOffset(2026, 11, 7, 9, 0, 0, TimeSpan.Zero).AddTicks(1234567);

    private async Task Pins(string board, params (string Label, int Hour)[] pins)
    {
        using var scope = Koan.Core.Hosting.App.AppHost.PushScope(Factory.Services);
        // Ids rise with each pin written, so a tie in the order is broken the same way on every store.
        foreach (var (label, hour) in pins)
            await new Pin { Id = $"{board}-{Interlocked.Increment(ref _pinned):D4}-{label}", Label = label, Board = board, PinnedAt = Pinned.AddHours(hour) }.Save(Ct);
    }

    private async Task<JsonElement> List(string path)
    {
        var answer = await Client.GetAsync(path, Ct);
        answer.StatusCode.Should().Be(HttpStatusCode.OK, await answer.Content.ReadAsStringAsync(Ct));
        return await answer.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static IReadOnlyList<string> Labels(JsonElement page)
        => page.GetProperty("items").EnumerateArray().Select(pin => pin.GetProperty("label").GetString()!).ToArray();

    private static string? Next(JsonElement page) => page.GetProperty("next").GetString();

    [Fact]
    public async Task A_form_create_is_sent_on_to_the_rows_address()
    {
        var answer = await Form("/api/cards", ("title", "Formed Card"), ("place", "side"));

        answer.StatusCode.Should().Be(HttpStatusCode.SeeOther, await answer.Content.ReadAsStringAsync(Ct));
        answer.Headers.Location!.OriginalString.Should().Be("/api/cards/by/formed-card");
        answer.Headers.CacheControl!.NoStore.Should().BeTrue();
        (await Read("/api/cards/by/formed-card")).GetProperty("shelf").GetString().Should().Be("side");
    }

    [Fact]
    public async Task A_form_is_sent_on_to_its_return_where_that_is_a_path_here()
    {
        var returned = await Form("/api/cards", ("title", "Returned Card"), ("return", "/boards/front?seen=1"));
        returned.StatusCode.Should().Be(HttpStatusCode.SeeOther);
        returned.Headers.Location!.OriginalString.Should().Be("/boards/front?seen=1");

        foreach (var unsafeReturn in new[] { "//elsewhere.example/x", "https://elsewhere.example/x", "/\\elsewhere.example", "boards" })
        {
            var title = "Unsafe " + Guid.CreateVersion7().ToString("N");
            var answer = await Form("/api/cards", ("title", title), ("return", unsafeReturn));
            answer.StatusCode.Should().Be(HttpStatusCode.SeeOther, unsafeReturn);
            answer.Headers.Location!.OriginalString.Should().Be("/api/cards/by/" + title.ToLowerInvariant().Replace(' ', '-'), unsafeReturn);
        }
    }

    [Fact]
    public async Task A_form_that_reads_json_reads_the_row_and_one_that_also_reads_pages_is_sent_on()
    {
        var json = await Form("/api/cards", [("title", "Json Form Card")], "application/json");
        json.StatusCode.Should().Be(HttpStatusCode.Created, await json.Content.ReadAsStringAsync(Ct));
        json.Headers.Location!.OriginalString.Should().Be("/api/cards/by/json-form-card");
        (await json.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("title").GetString().Should().Be("Json Form Card");

        var both = await Form("/api/cards", [("title", "Paged Form Card")], "application/json, text/html");
        both.StatusCode.Should().Be(HttpStatusCode.SeeOther);
    }

    [Fact]
    public async Task A_form_asks_for_the_change_it_cannot_send()
    {
        var id = await Create("Formed change");

        var answer = await Form($"/api/cards/by/{id}", ("_method", "PATCH"), ("title", "Changed by form"));

        answer.StatusCode.Should().Be(HttpStatusCode.SeeOther, await answer.Content.ReadAsStringAsync(Ct));
        answer.Headers.Location!.OriginalString.Should().Be("/api/cards/by/formed-change");
        var card = await Read($"/api/cards/by/{id}");
        card.GetProperty("title").GetString().Should().Be("Changed by form");
        card.GetProperty("edits").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task A_form_delete_takes_its_input_from_the_form_and_is_sent_on_to_the_list()
    {
        var made = await Client.PostAsJsonAsync("/api/loose-cards", new { title = "Leaving" }, Ct);
        made.StatusCode.Should().Be(HttpStatusCode.Created, await made.Content.ReadAsStringAsync(Ct));
        var id = (await made.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!;
        made.Headers.Location!.OriginalString.Should().Be($"/api/loose-cards/{id}");

        var changed = await Form($"/api/loose-cards/{id}", ("_method", "PATCH"), ("edits", "2"));
        changed.StatusCode.Should().Be(HttpStatusCode.SeeOther, await changed.Content.ReadAsStringAsync(Ct));
        changed.Headers.Location!.OriginalString.Should().Be($"/api/loose-cards/{id}");

        var stale = await Form($"/api/loose-cards/{id}", ("_method", "DELETE"), ("seen", "0"));
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Problem(stale)).GetProperty("sent").GetProperty("seen")[0].GetString().Should().Be("0");

        var gone = await Form($"/api/loose-cards/{id}", ("_method", "DELETE"), ("seen", "2"));
        gone.StatusCode.Should().Be(HttpStatusCode.SeeOther, await gone.Content.ReadAsStringAsync(Ct));
        gone.Headers.Location!.OriginalString.Should().Be("/api/loose-cards");
        (await Client.GetAsync($"/api/cards/by/{id}", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_refused_form_keeps_what_it_sent()
    {
        var forged = await Form("/api/cards", ("title", "Forged Form"), ("slug", "elsewhere"), ("return", "/boards"));
        forged.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await Problem(forged);
        problem.GetProperty("errors").GetProperty("slug")[0].GetString().Should().Be("This address does not write 'slug'.");
        problem.GetProperty("sent").GetProperty("title")[0].GetString().Should().Be("Forged Form");
        problem.GetProperty("sent").GetProperty("return")[0].GetString().Should().Be("/boards");

        var unfit = await Form("/api/cards", ("title", "Many Copies"), ("copies", "many"));
        unfit.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var refused = await Problem(unfit);
        refused.GetProperty("errors").TryGetProperty("copies", out _).Should().BeTrue();
        refused.GetProperty("sent").GetProperty("copies")[0].GetString().Should().Be("many");

        var json = await Post(new { title = "Forged Json", slug = "elsewhere" });
        (await Problem(json)).TryGetProperty("sent", out _).Should().BeFalse("only a form is answered with what it sent");
    }

    [Fact]
    public async Task A_verb_answers_as_an_entity_write_does()
    {
        var form = await Form("/api/card-verbs/kept/pin", ("return", "/boards"));
        form.StatusCode.Should().Be(HttpStatusCode.SeeOther);
        form.Headers.Location!.OriginalString.Should().Be("/boards");

        var bare = await Form("/api/card-verbs/kept/pin");
        bare.Headers.Location!.OriginalString.Should().Be("/api/cards/by/kept");

        var json = await Client.PostAsync("/api/card-verbs/kept/pin", null, Ct);
        json.StatusCode.Should().Be(HttpStatusCode.OK, "a sender that sends no form and asks for no page reads JSON");
        json.Headers.Location!.OriginalString.Should().Be("/api/cards/by/kept");
        (await json.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("pinned").GetBoolean().Should().BeTrue();
    }

    private async Task<string> Create(string title, string? place = null)
    {
        var answer = await Post(new { title, place });
        answer.StatusCode.Should().Be(HttpStatusCode.Created, await answer.Content.ReadAsStringAsync(Ct));
        return (await answer.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!;
    }

    private Task<HttpResponseMessage> Post(object body) => Client.PostAsJsonAsync("/api/cards", body, Ct);

    /// <summary>A page's form, as a browser posts it: urlencoded, asking for a page.</summary>
    private Task<HttpResponseMessage> Form(string path, params (string Name, string Value)[] fields) => Form(path, fields, "text/html");

    private Task<HttpResponseMessage> Form(string path, (string Name, string Value)[] fields, string accept)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new FormUrlEncodedContent(fields.Select(field => new KeyValuePair<string, string>(field.Name, field.Value)))
        };
        request.Headers.TryAddWithoutValidation("Accept", accept);
        return Client.SendAsync(request, Ct);
    }

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
