using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Web.Attributes;
using Koan.Web.Authorization;
using Koan.Web.Extensions;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace Koan.Web.Extensions.Tests;

/// <summary>ARCH-0144: a request answered in process goes through the application's whole pipeline as the caller of the request in hand
/// or as nobody, keeps its route's constraints, its application's formatter, filters and exception handler, and takes nothing of the
/// request in hand that belongs to that request's own connection, body or answer.</summary>
[Collection(RestEntityCollection.Name)]
public sealed class InProcessSpec(InProcessHost host) : IClassFixture<InProcessHost>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Relayed> Relay(string address, string? who = null, string? whoHeader = null, bool nobody = false, string? accept = null,
        string? handed = null, bool stranger = false, string? ifNoneMatch = null, string? from = null)
    {
        var query = $"/in-process/relay?address={Uri.EscapeDataString(address)}&nobody={nobody}&stranger={stranger}"
                    + (accept is null ? "" : $"&accept={Uri.EscapeDataString(accept)}") + (handed is null ? "" : $"&handed={handed}")
                    + (from is null ? "" : $"&from={from}");
        using var request = new HttpRequestMessage(HttpMethod.Get, query);
        if (who is not null) request.Headers.TryAddWithoutValidation(HeaderNames.Authorization, "Who " + who);
        if (whoHeader is not null) request.Headers.TryAddWithoutValidation(WhoScheme.Header, whoHeader);
        if (ifNoneMatch is not null) request.Headers.TryAddWithoutValidation(HeaderNames.IfNoneMatch, ifNoneMatch);
        using var response = await host.Client.SendAsync(request, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        return JsonSerializer.Deserialize<Relayed>(text, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    [Fact]
    public async Task A_read_as_nobody_inside_a_signed_in_request_is_refused_where_a_visitor_is_whatever_header_the_scheme_reads()
    {
        foreach (var (authorization, header) in new[] { ("mara", (string?)null), (null, "mara") })
        {
            (await Relay("/in-process/members", authorization, header)).Status.Should().Be(200, "the reader is a member");
            var nobody = await Relay("/in-process/members", authorization, header, nobody: true);
            nobody.Status.Should().Be(401, "nobody is a visitor, and a visitor is refused here");
            (await Relay("/in-process/whoami", authorization, header, nobody: true)).Body.Should().Be("visitor");
        }
    }

    [Fact]
    public async Task A_read_as_the_reader_is_the_readers_own_and_a_principal_given_beside_a_request_in_hand_is_not_read()
    {
        (await Relay("/in-process/whoami", "mara")).Body.Should().Be("mara");
        (await Relay("/in-process/whoami", "mara", stranger: true)).Body.Should().Be("mara");
        (await Relay("/in-process/whoami", stranger: true)).Body.Should().Be("visitor");
    }

    [Fact]
    public async Task With_no_request_in_hand_it_is_the_principal_given_or_a_visitor()
    {
        var given = new InProcessRequest(HttpMethods.Get, "/in-process/whoami") { Principal = WhoScheme.Person("sam") };
        var none = new InProcessRequest(HttpMethods.Get, "/in-process/whoami");
        var nobody = new InProcessRequest(HttpMethods.Get, "/in-process/whoami") { Principal = WhoScheme.Person("sam"), AsNobody = true };

        Encoding.UTF8.GetString((await host.Pipeline.Send(given, Ct)).Body.Span).Should().Be("sam");
        Encoding.UTF8.GetString((await host.Pipeline.Send(none, Ct)).Body.Span).Should().Be("visitor");
        Encoding.UTF8.GetString((await host.Pipeline.Send(nobody, Ct)).Body.Span).Should().Be("visitor");
    }

    [Fact]
    public async Task A_get_by_address_keeps_its_route_constraints()
    {
        (await Relay("/in-process/items/abc")).Status.Should().Be(404);
        var found = await Relay("/in-process/items/7?shade=blue");
        found.Status.Should().Be(200);
        found.Body.Should().Be("{\"id\":7,\"shade\":\"blue\"}");
    }

    [Fact]
    public async Task An_applications_text_html_formatter_answers_when_accept_asks_for_it()
    {
        var page = await Relay("/in-process/page/north", accept: "text/html");
        var json = await Relay("/in-process/page/north", accept: "application/json");

        page.ContentType.Should().StartWith("text/html");
        page.Body.Should().Be("<p>north</p>");
        json.ContentType.Should().StartWith("application/json");
        json.Body.Should().Be("{\"text\":\"north\"}");
    }

    [Fact]
    public async Task A_value_handed_in_process_reaches_the_action_and_the_formatter_and_items_are_its_own()
    {
        var seen = await Relay("/in-process/seen", handed: "kept");
        var page = await Relay("/in-process/page/north", accept: "text/html", handed: "kept");

        seen.Body.Should().Be("{\"handed\":\"kept\",\"outerItem\":false}");
        page.Body.Should().Be("<p>north</p><i>kept</i>");
        seen.AccessorIsOuter.Should().BeTrue("the request in hand's accessor is as it was afterwards");
    }

    [Fact]
    public async Task An_exception_downstream_answers_as_the_applications_exception_handler_answers()
    {
        var failed = await Relay("/in-process/throws");

        failed.Status.Should().Be(500);
        failed.Body.Should().Be("handled: it broke");
    }

    [Fact]
    public async Task The_outer_if_none_match_does_not_reach_the_inner_request_and_one_it_is_given_still_answers_304()
    {
        var tag = "W/\"page-north\"";
        var relayed = await Relay("/in-process/page/north", ifNoneMatch: tag);
        var given = new InProcessRequest(HttpMethods.Get, "/in-process/page/north") { Headers = { IfNoneMatch = tag, Accept = "application/json" } };
        var conditional = await host.Pipeline.Send(given, Ct);

        relayed.Status.Should().Be(200);
        relayed.ETag.Should().Be(tag);
        relayed.Body.Should().Be("{\"text\":\"north\"}");
        conditional.Status.Should().Be(304, "the tag is the inner request's own when it is given");
    }

    [Fact]
    public async Task A_refused_forms_page_is_drawn_again_as_the_same_reader_without_running_the_write_again()
    {
        var writes = host.Services.GetRequiredService<Writes>();
        var before = writes.Count;
        using var post = new HttpRequestMessage(HttpMethod.Post, "/in-process/form") { Content = new FormUrlEncodedContent([new("words", "")]) };
        post.Headers.TryAddWithoutValidation(HeaderNames.Authorization, "Who mara");
        post.Headers.TryAddWithoutValidation(HeaderNames.Accept, "text/html");

        using var answer = await host.Client.SendAsync(post, Ct);

        answer.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        answer.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        (await answer.Content.ReadAsStringAsync(Ct)).Should().Be("<p>the form for mara</p><i>Say something.</i>");
        (writes.Count - before).Should().Be(1, "drawing the page again is a GET, which never runs the write");
    }

    [Fact]
    public async Task Many_gets_as_nobody_answer_their_bytes_and_content_type()
    {
        foreach (var name in new[] { "north", "south", "east" })
        {
            var request = new InProcessRequest(HttpMethods.Get, $"/in-process/page/{name}") { AsNobody = true, Headers = { Accept = "text/html" } };
            request.Features.Set(new Handed("standalone"));

            var answer = await host.Pipeline.Send(request, Ct);

            answer.Status.Should().Be(200);
            answer.Headers.ContentType.ToString().Should().StartWith("text/html");
            Encoding.UTF8.GetString(answer.Body.Span).Should().Be($"<p>{name}</p><i>standalone</i>");
        }
    }

    [Fact]
    public async Task Nobody_carries_no_connection_address_so_an_internal_network_admits_the_reader_and_not_nobody()
    {
        var reader = await Relay("/in-process/internal", from: "10.1.2.3");
        var nobody = await Relay("/in-process/internal", from: "10.1.2.3", nobody: true);

        reader.Status.Should().Be(200, reader.Body);
        reader.Body.Should().Be("inside");
        nobody.Status.Should().BeOneOf([401, 403], "nobody arrived from nowhere, which no declared network holds: {0}", nobody.Body);
    }

    [Fact]
    public async Task A_pipeline_the_host_never_built_answers_503_in_one_problem()
    {
        var unbuilt = new KoanWebPipeline(host.Services, host.Services.GetRequiredService<IHttpContextAccessor>());

        var answer = await unbuilt.Send(new InProcessRequest(HttpMethods.Get, "/in-process/whoami"), Ct);

        answer.Status.Should().Be(503);
        answer.Headers.ContentType.ToString().Should().StartWith("application/problem+json");
        using var problem = JsonDocument.Parse(answer.Body);
        problem.RootElement.GetProperty("detail").GetString().Should().Be("This process serves no requests.");
        problem.RootElement.GetProperty("kind").GetString().Should().Be("failed");
    }

    [Fact]
    public async Task What_would_change_the_address_or_the_reader_is_refused_before_anything_runs()
    {
        var credential = new InProcessRequest(HttpMethods.Get, "/in-process/whoami") { Headers = { Cookie = "session=someone-else" } };
        var address = new InProcessRequest(HttpMethods.Get, "https://elsewhere.example/in-process/whoami");
        var dotted = new InProcessRequest(HttpMethods.Get, "/in-process/items/../whoami");
        var escaped = new InProcessRequest(HttpMethods.Get, "/in-process/items/%2E%2E/whoami");
        var feature = new InProcessRequest(HttpMethods.Get, "/in-process/whoami");
        feature.Features.Set<IHttpAuthenticationFeature>(new HttpAuthenticationFeature { User = WhoScheme.Person("someone-else") });

        foreach (var refused in new[] { credential, address, dotted, escaped, feature })
            await FluentActions.Awaiting(() => host.Pipeline.Send(refused, Ct)).Should().ThrowAsync<ArgumentException>(refused.Address);
        Encoding.UTF8.GetString((await host.Pipeline.Send(new InProcessRequest(HttpMethods.Get, "/in-process/whoami?next=../back"), Ct)).Body.Span)
            .Should().Be("visitor", "a dot segment in the query is no segment of the path");
    }
}

/// <summary>What the relay saw of the request it made in process.</summary>
public sealed record Relayed(int Status, string ContentType, string Body, string ETag, bool AccessorIsOuter);

/// <summary>A resource only this spec's formatter renders.</summary>
public sealed record Page(string Text);

/// <summary>A value handed to a request in process.</summary>
public sealed record Handed(string Value);

/// <summary>How many times the form's write ran.</summary>
public sealed class Writes
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public void Ran() => Interlocked.Increment(ref _count);
}

/// <summary>Who a request is: <c>Authorization: Who name</c>, or an application's own header, <c>X-Who: name</c>; none is a visitor.</summary>
public sealed class WhoScheme(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Name = "Who";
    public const string Header = "X-Who";

    public static ClaimsPrincipal Person(string name) => new(new ClaimsIdentity([new Claim(ClaimTypes.Name, name)], Name));

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var raw = Request.Headers.Authorization.ToString();
        var name = raw.StartsWith(Name + " ", StringComparison.Ordinal) ? raw[(Name.Length + 1)..] : Request.Headers[Header].ToString();
        return Task.FromResult(name.Length == 0 ? AuthenticateResult.NoResult() : AuthenticateResult.Success(new AuthenticationTicket(Person(name), Name)));
    }
}

/// <summary>A page that writes the value handed to it, when there is one.</summary>
public sealed class PageFormatter : TextOutputFormatter
{
    public PageFormatter()
    {
        SupportedMediaTypes.Add("text/html");
        SupportedEncodings.Add(Encoding.UTF8);
    }

    protected override bool CanWriteType(Type? type) => type == typeof(Page);

    public override Task WriteResponseBodyAsync(OutputFormatterWriteContext context, Encoding selectedEncoding)
    {
        var handed = context.HttpContext.Features.Get<Handed>() is { } value ? $"<i>{value.Value}</i>" : "";
        return context.HttpContext.Response.WriteAsync($"<p>{((Page)context.Object!).Text}</p>{handed}", selectedEncoding);
    }
}

/// <summary>Tags a page by its text, and answers <c>304</c> when the request already holds the tag.</summary>
public sealed class PageTags : IAsyncResultFilter
{
    public Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Result is ObjectResult { Value: Page page })
        {
            var tag = $"W/\"page-{page.Text}\"";
            context.HttpContext.Response.Headers.ETag = tag;
            if (context.HttpContext.Request.Headers.IfNoneMatch.ToString() == tag) context.Result = new StatusCodeResult(StatusCodes.Status304NotModified);
        }
        return next();
    }
}

[Route("/in-process")]
public sealed class InProcessController(KoanWebPipeline pipeline) : ControllerBase
{
    [HttpGet("relay")]
    public async Task<IActionResult> Relay(string address, bool nobody, bool stranger, string? accept, string? handed, string? from, CancellationToken ct)
    {
        HttpContext.Items["outer"] = true;
        // The address the request in hand arrived from, as a declared proxy would have resolved it.
        if (from is not null) HttpContext.Connection.RemoteIpAddress = IPAddress.Parse(from);
        var request = new InProcessRequest(HttpMethods.Get, address) { AsNobody = nobody, Principal = stranger ? WhoScheme.Person("stranger") : null };
        request.Headers.Accept = accept ?? "application/json";
        if (handed is not null) request.Features.Set(new Handed(handed));
        var answer = await pipeline.Send(request, ct);
        var accessor = HttpContext.RequestServices.GetRequiredService<IHttpContextAccessor>();
        return Ok(new Relayed(answer.Status, answer.Headers.ContentType.ToString(), Encoding.UTF8.GetString(answer.Body.Span), answer.Headers.ETag.ToString(),
            ReferenceEquals(accessor.HttpContext, HttpContext)));
    }

    [HttpGet("whoami")]
    public IActionResult Who() => Content(User.Identity?.IsAuthenticated == true ? User.Identity.Name! : "visitor");

    [HttpGet("members"), Authorize]
    public IActionResult Members() => Content("member");

    [HttpGet("internal"), Tool, Access(all: "origin:internal")]
    public IActionResult Internal() => Content("inside");

    [HttpGet("items/{id:int}")]
    public IActionResult Item(int id, string? shade) => Ok(new { id, shade });

    [HttpGet("page/{text}")]
    public IActionResult Drawn(string text) => Ok(new Page(text));

    [HttpGet("seen")]
    public IActionResult Seen() => Ok(new { handed = HttpContext.Features.Get<Handed>()?.Value, outerItem = HttpContext.Items.ContainsKey("outer") });

    [HttpGet("throws")]
    public IActionResult Throws() => throw new InvalidOperationException("it broke");

    [HttpGet("form")]
    public IActionResult Form() => Ok(new Page($"the form for {User.Identity?.Name ?? "a visitor"}"));

    [HttpPost("form")]
    public async Task<IActionResult> Write([FromForm] string? words, [FromServices] Writes writes, CancellationToken ct)
    {
        writes.Ran();
        if (!string.IsNullOrWhiteSpace(words)) return NoContent();
        var again = new InProcessRequest(HttpMethods.Get, "/in-process/form");
        again.Headers.Accept = Request.Headers.Accept;
        again.Features.Set(new Handed("Say something."));
        var page = await pipeline.Send(again, ct);
        return new ContentResult { StatusCode = StatusCodes.Status400BadRequest, ContentType = page.Headers.ContentType, Content = Encoding.UTF8.GetString(page.Body.Span) };
    }
}

/// <summary>An application of its own for <see cref="InProcessSpec"/>: a scheme reading <c>Authorization</c> or an application's header, a
/// <c>text/html</c> formatter for its own resource only, its own exception handler and a result filter that answers <c>304</c>.</summary>
public sealed class InProcessHost : IAsyncLifetime
{
    private WebApplication? _app;
    private IServiceProvider? _shared;

    public HttpClient Client { get; private set; } = null!;

    public IServiceProvider Services => _app?.Services ?? throw new InvalidOperationException("Host not started.");

    public KoanWebPipeline Pipeline => Services.GetRequiredService<KoanWebPipeline>();

    public async ValueTask InitializeAsync()
    {
        // The collection's shared host is the process default; this one takes it for its own specs and gives it back.
        _shared = AppHost.Current;
        // Outside Development, so WebApplication adds no developer exception page inside the application's own handler.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Test" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Koan:Environment"] = "Test",
            ["Koan:Data:Sources:Default:Adapter"] = "inmemory",
            ["Koan:Data:Sources:Default:ConnectionString"] = "memory://in-process-tests",
            ["Koan:BackgroundServices:Enabled"] = "false",
            ["Logging:LogLevel:Default"] = "Warning",
        });
        builder.Services.AddKoan();
        builder.Services.AddKoanControllersFrom<InProcessController>();
        builder.Services.AddSingleton<Writes>();
        builder.Services.Configure<OriginOptions>(origin => origin.InternalNetworks.Add("10.0.0.0/8"));
        builder.Services.AddAuthentication(WhoScheme.Name).AddScheme<AuthenticationSchemeOptions, WhoScheme>(WhoScheme.Name, _ => { });
        builder.Services.PostConfigure<AuthenticationOptions>(options => options.DefaultScheme = options.DefaultAuthenticateScheme = options.DefaultChallengeScheme = WhoScheme.Name);
        builder.Services.Configure<MvcOptions>(mvc =>
        {
            mvc.RespectBrowserAcceptHeader = true;
            mvc.OutputFormatters.Insert(0, new PageFormatter());
            mvc.Filters.Add(new PageTags());
        });
        builder.Services.WithExceptionHandler();
        builder.Services.AddExceptionHandler(options => options.ExceptionHandler = async http =>
        {
            http.Response.StatusCode = StatusCodes.Status500InternalServerError;
            var failure = http.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
            await http.Response.WriteAsync($"handled: {failure?.Message}");
        });
        _app = builder.Build();
        await _app.StartAsync(TestContext.Current.CancellationToken);
        Client = _app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
        AppHost.Current = _shared;
    }
}
