using System.Net;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Koan.Mcp.Verbs.Tests;

/// <summary>
/// WEB-0091: wherever an action answers <c>GET</c> it answers <c>HEAD</c>, over real HTTP: an action that declares only <c>GET</c>, a declared
/// entity read among them, answers <c>HEAD</c> with its <c>GET</c>'s status and headers and no body, as the health alias does; one that
/// declares its own <c>HEAD</c> keeps it; and a read is still one tool.
/// </summary>
public sealed class HeadSpec(ForumFixture forum) : IClassFixture<ForumFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_action_that_declares_only_get_answers_head_as_its_get_with_no_body()
    {
        var posted = await forum.Route(HttpMethod.Post, "/boards/notices", new JObject { ["text"] = "read by head" }, "mara");
        posted.Status.Should().Be(201, posted.ToString());
        using var client = Client();

        foreach (var path in new[] { $"/boards/notices/{JObject.Parse(posted.Text)["id"]}", "/heads/plain", Koan.Web.Infrastructure.KoanWebConstants.Routes.ApiHealth })
        {
            using var get = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, path), Ct);
            using var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, path), Ct);

            get.StatusCode.Should().Be(HttpStatusCode.OK, path);
            head.StatusCode.Should().Be(get.StatusCode, path);
            head.Content.Headers.ContentType.Should().Be(get.Content.Headers.ContentType, path);
            (await get.Content.ReadAsByteArrayAsync(Ct)).Should().NotBeEmpty(path);
            (await head.Content.ReadAsByteArrayAsync(Ct)).Should().BeEmpty(path);
        }
    }

    [Fact]
    public async Task An_action_that_declares_its_own_head_keeps_it()
    {
        using var client = Client();

        using var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/heads/own"), Ct);

        head.StatusCode.Should().Be(HttpStatusCode.OK);
        head.Headers.GetValues(HeadsController.Answered).Should().Equal(["by its own head"]);
    }

    [Fact]
    public async Task A_read_is_one_tool()
    {
        var names = (await forum.Tools("mara")).Select(tool => tool["name"]!.Value<string>()!).ToArray();

        names.Should().Contain(["read_notices", "read_notice"]).And.OnlyHaveUniqueItems();
    }

    private HttpClient Client()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(forum.BaseUrl), Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Test mara");
        return client;
    }
}

/// <summary>Two reads: one that declares only <c>GET</c>, and one that answers its own <c>HEAD</c>.</summary>
[Route("/heads")]
public sealed class HeadsController : ControllerBase
{
    /// <summary>The header the read's own <c>HEAD</c> answers with.</summary>
    public const string Answered = "Head-Answered";

    [HttpGet("plain")]
    public IActionResult Plain() => Ok(new { plain = true });

    [HttpGet("own")]
    public IActionResult Own() => Ok(new { own = true });

    [HttpHead("own")]
    public IActionResult OwnHead()
    {
        Response.Headers[Answered] = "by its own head";
        return Ok();
    }
}
