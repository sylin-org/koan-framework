using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Koan.Data.Core.Model;
using Koan.Web.Attributes;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Koan.Web.WellKnown.Tests;

/// <summary>
/// WEB-0088 under WEB-0078's base address: <c>Answers.Written</c> takes the address written as a path under the application's base and
/// puts the base before it, for an entity write's row and a verb's address alike, and a form's <c>return</c> is a path under the base
/// that never climbs out of it. WEB-0090: a page sent on without the words it does not take stays under the base.
/// </summary>
public sealed class FormWriteUnderBaseSpec
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_write_is_answered_at_its_address_under_the_base()
    {
        await using var host = await BaseUrlPathBaseSpec.BaseUrlHost.Start(new Dictionary<string, string?>
        {
            ["Koan:Web:ApplicationUrl"] = "https://www.example.com/forum",
        });
        using var client = host.Client;

        var json = await client.PostAsJsonAsync("/forum/based-notes", new { text = "First" }, Ct);
        json.StatusCode.Should().Be(HttpStatusCode.Created, await json.Content.ReadAsStringAsync(Ct));
        var id = (await json.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString();
        json.Headers.Location!.OriginalString.Should().Be($"/forum/based-notes/{id}", "the base is put before the row's address once");

        var form = await Form(client, "/forum/based-notes", ("text", "Second"));
        form.StatusCode.Should().Be(HttpStatusCode.SeeOther, await form.Content.ReadAsStringAsync(Ct));
        form.Headers.Location!.OriginalString.Should().StartWith("/forum/based-notes/").And.NotStartWith("/forum/forum");

        (await Form(client, "/forum/based-notes/kept/pin")).Headers.Location!.OriginalString
            .Should().Be("/forum/based-notes/kept", "a verb names its address under the base as an entity write does");
        (await Form(client, "/forum/based-notes/kept/pin", ("return", "/boards"))).Headers.Location!.OriginalString.Should().Be("/forum/boards");
    }

    [Theory]
    [InlineData("/../admin")]
    [InlineData("/boards/../../admin")]
    [InlineData("/%2E%2E/admin")]
    [InlineData("/./admin")]
    public async Task A_return_that_climbs_out_of_the_base_is_not_followed(string climbing)
    {
        await using var host = await BaseUrlPathBaseSpec.BaseUrlHost.Start(new Dictionary<string, string?>
        {
            ["Koan:Web:ApplicationUrl"] = "https://www.example.com/forum",
        });
        using var client = host.Client;

        var answer = await Form(client, "/forum/based-notes/kept/pin", ("return", climbing));

        answer.StatusCode.Should().Be(HttpStatusCode.SeeOther);
        answer.Headers.Location!.OriginalString.Should().Be("/forum/based-notes/kept");
    }

    /// <summary>WEB-0090: a page reached with words it does not take is sent on to its own address under the base, without them.</summary>
    [Fact]
    public async Task A_page_sent_on_without_the_words_it_does_not_take_stays_under_the_base()
    {
        await using var host = await BaseUrlPathBaseSpec.BaseUrlHost.Start(new Dictionary<string, string?>
        {
            ["Koan:Web:ApplicationUrl"] = "https://www.example.com/forum",
        });
        using var client = host.Client;
        var made = await client.PostAsJsonAsync("/forum/based-notes", new { text = "Shared" }, Ct);
        made.StatusCode.Should().Be(HttpStatusCode.Created, await made.Content.ReadAsStringAsync(Ct));
        var id = (await made.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString();

        using var tagged = new HttpRequestMessage(HttpMethod.Get, $"/forum/based-notes/{id}?fbclid=IwAR0x");
        tagged.Headers.TryAddWithoutValidation("Accept", "text/html");
        var answer = await client.SendAsync(tagged, Ct);

        answer.StatusCode.Should().Be(HttpStatusCode.Found);
        answer.Headers.Location!.OriginalString.Should().Be($"/forum/based-notes/{id}");
    }

    private static Task<HttpResponseMessage> Form(HttpClient client, string path, params (string Name, string Value)[] fields)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new FormUrlEncodedContent(fields.Select(field => new KeyValuePair<string, string>(field.Name, field.Value)))
        };
        request.Headers.TryAddWithoutValidation("Accept", "text/html");
        return client.SendAsync(request, Ct);
    }
}

public sealed class BasedNote : Entity<BasedNote>
{
    public string Text { get; set; } = "";
}

[Route("based-notes")]
[Declared]
public sealed class BasedNotesController : EntityController<BasedNote>
{
    [HttpGet("{note}")]
    public override Task<IActionResult> GetById(string note, CancellationToken ct) => base.GetById(note, ct);

    [Writes(nameof(BasedNote.Id), nameof(BasedNote.Text))]
    public override Task<IActionResult> Upsert(BasedNote model, CancellationToken ct) => base.Upsert(model, ct);
}

[ApiController]
[Route("based-notes")]
public sealed class BasedNoteVerbsController : ControllerBase
{
    [HttpPost("{note}/pin")]
    public IActionResult Pin(string note) => this.Written("/based-notes/" + Uri.EscapeDataString(note), new { note, pinned = true }, created: false);
}
