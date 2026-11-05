using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using AwesomeAssertions;
using Koan.Data.Core.Commands;
using Koan.Web.Authorization;
using Koan.Web.Hooks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Koan.Web.Extensions.Tests;

/// <summary>ARCH-0146: a routed action that declares access is judged before anything is bound, whether or not it is a tool; the
/// application's provider refuses with a reason, or allows with what it found, which the action reads from the request; a 401 carries the
/// application's challenge; and a host that asks for it stops at start over an action of its own that declares nothing.</summary>
[Collection(RestEntityCollection.Name)]
public sealed class RouteGateSpec(RestEntityWebFactory fx)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(HttpStatusCode Status, JsonElement Body, string[] Challenges)> Send(HttpMethod method, string path, HttpContent? content = null,
        string? user = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        if (user is not null) request.Headers.TryAddWithoutValidation("X-Test-User", user);
        using var response = await fx.Client.SendAsync(request, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        var challenges = response.Headers.TryGetValues("WWW-Authenticate", out var said) ? said.ToArray() : [];
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone(), challenges);
    }

    private static FormUrlEncodedContent Many() => new(new Dictionary<string, string> { ["count"] = "many" });

    [Fact]
    public async Task A_declared_route_is_judged_before_its_body_is_bound()
    {
        var visitor = await Send(HttpMethod.Post, "/route-gate/poetry/books", Many());

        visitor.Status.Should().Be(HttpStatusCode.Unauthorized, "who may act is told before what was sent is judged");
        visitor.Body.GetProperty("word").GetString().Should().Be("sign-in");
        visitor.Body.GetProperty("detail").GetString().Should().Be(ShelfAccess.SignIn.Text);
        visitor.Challenges.Should().Equal(ShelfChallenge.Said);

        var reader = await Send(HttpMethod.Post, "/route-gate/poetry/books", Many(), user: "ada");
        reader.Status.Should().Be(HttpStatusCode.BadRequest, "once judged, binding refuses what it cannot take");
        reader.Challenges.Should().BeEmpty("only a 401 asks for a credential");
    }

    [Fact]
    public async Task What_the_gate_found_reaches_the_action_and_a_refusal_answers_its_reason()
    {
        var found = await Send(HttpMethod.Get, "/route-gate/poetry", user: "ada");
        found.Status.Should().Be(HttpStatusCode.OK, found.Body.ToString());
        found.Body.GetProperty("name").GetString().Should().Be("poetry");
        found.Body.GetProperty("need").GetString().Should().Be("read", "the provider read the action's own declaration");

        var missing = await Send(HttpMethod.Get, "/route-gate/missing", user: "ada");
        missing.Status.Should().Be(HttpStatusCode.NotFound);
        missing.Body.GetProperty("word").GetString().Should().Be("absent");
        missing.Body.GetProperty("detail").GetString().Should().Be(Koan.Web.Controllers.Answers.Nothing);
    }

    [Fact]
    public async Task A_page_the_application_answers_401_itself_carries_the_challenge_too()
    {
        var page = await Send(HttpMethod.Get, "/route-gate/own-page");
        page.Status.Should().Be(HttpStatusCode.Unauthorized);
        page.Challenges.Should().Equal(ShelfChallenge.Said);
    }

    [Fact]
    public async Task An_open_route_and_an_undeclared_one_meet_no_gate()
    {
        (await Send(HttpMethod.Get, "/route-gate/catalogue")).Status.Should().Be(HttpStatusCode.OK);
        (await Send(HttpMethod.Get, "/route-gate/plain")).Status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_start_check_names_each_action_of_the_application_that_declares_nothing()
    {
        var registered = fx.Services.GetServices<IHostedService>().Single(service => service.GetType().Name == "RouteAccessStartCheck").GetType();
        var off = (IHostedService)ActivatorUtilities.CreateInstance(fx.Services, registered, Microsoft.Extensions.Options.Options.Create(new AuthorizeOptions()));
        await off.StartAsync(Ct);

        var on = (IHostedService)ActivatorUtilities.CreateInstance(fx.Services, registered, Microsoft.Extensions.Options.Options.Create(new AuthorizeOptions { RoutesDeclareAccess = true }));
        var refused = await on.Invoking(check => check.StartAsync(Ct)).Should().ThrowAsync<InvalidOperationException>();
        refused.Which.Message.Should().Contain($"{nameof(RouteGateController)}.{nameof(RouteGateController.Plain)}")
            .And.NotContain($"{nameof(RouteGateController)}.{nameof(RouteGateController.Catalogue)}")
            .And.NotContain($"{nameof(RouteGateController)}.{nameof(RouteGateController.Read)}")
            .And.NotContain("HealthController", "a Koan module's own controllers answer for themselves");
    }
}

/// <summary>What a shelf route needs, in the application's own terms.</summary>
public sealed class ShelvedAttribute(string need) : AccessAttribute<string>(need);

/// <summary>What the gate found, which the action reads from the request.</summary>
public sealed record Shelf(string Name, string Need);

/// <summary>The application's provider: it judges only the routes of <see cref="RouteGateController"/>, and defers on a listing.</summary>
public sealed class ShelfAccess : IAuthorizationProvider
{
    public static readonly Reason SignIn = Reason.Gated("sign-in", "Sign in to use the shelves.");

    public int Order => 10;

    public Task<AuthorizeDecision?> EvaluateAsync(AuthorizeRequest request, CancellationToken ct = default)
    {
        if (request.Resource is not MethodInfo verb || verb.ReflectedType != typeof(RouteGateController) || request.Context is null)
            return Task.FromResult<AuthorizeDecision?>(null);
        if (request.Subject.Identity?.IsAuthenticated != true) return Task.FromResult<AuthorizeDecision?>(AuthorizeDecision.Refused(SignIn));
        var shelf = request.Context.GetValueOrDefault("shelf") as string;
        return Task.FromResult<AuthorizeDecision?>(shelf == "missing"
            ? AuthorizeDecision.Refused(Reason.Absent("No shelf by that name."))
            : AuthorizeDecision.Allowed(new Shelf(shelf ?? "", string.Join(",", AccessAttribute<string>.Of(verb)))));
    }
}

/// <summary>The application's challenge on a 401.</summary>
public sealed class ShelfChallenge : IChallenge
{
    public const string Said = "Shelf proof=\"/route-gate/proof\"";

    public string For(HttpContext http) => Said;
}

public sealed record BookInput(int Count);

[Route("/route-gate")]
public sealed class RouteGateController : ControllerBase
{
    [HttpGet("catalogue")]
    [Open("Anyone may read what the shelves hold.")]
    public IActionResult Catalogue() => Ok();

    [HttpGet("plain")]
    public IActionResult Plain() => Ok();

    [HttpGet("own-page")]
    [Open("A page that asks for a sign-in in its own words.")]
    public IActionResult OwnPage() => new ObjectResult(new { said = "Sign in to read the shelves." }) { StatusCode = StatusCodes.Status401Unauthorized };

    [HttpGet("{shelf}")]
    [Shelved("read")]
    public IActionResult Read(string shelf) => Ok(HttpContext.Features.Get<Shelf>());

    [HttpPost("{shelf}/books")]
    [Shelved("write")]
    public IActionResult Add(string shelf, BookInput input) => ModelState.IsValid ? Created($"/route-gate/{shelf}", input) : BadRequest(ModelState);
}
