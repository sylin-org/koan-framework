using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Koan.Web.Auth.Contributors;
using Koan.Web.Auth.Flow;
using Koan.Web.Auth.Flow.Builtin;
using Koan.Web.Auth.Options;
using Xunit;

namespace Koan.Web.Auth.Tests;

/// <summary>
/// Behavior tests for the WEB-0066 flow handler pipeline. The dispatcher's job is to fan
/// every event out to every handler in Priority order, soft-fail on per-handler exceptions, and
/// honor ResponseHandled / Reject short-circuit signals.
/// </summary>
public sealed class AuthFlowDispatcherTests
{
    [Fact]
    public async Task DispatchChallenge_runs_handlers_in_priority_order()
    {
        var trace = new List<string>();
        var dispatcher = NewDispatcher(
            new RecordingHandler("low",  priority: -1000, trace),
            new RecordingHandler("mid",  priority: 0,     trace),
            new RecordingHandler("high", priority: 1000,  trace));

        await dispatcher.DispatchChallenge(NewChallengeCtx(), CancellationToken.None);

        trace.Should().Equal("low", "mid", "high");
    }

    [Fact]
    public async Task DispatchChallenge_responseHandled_does_not_stop_later_handlers_but_marks_response_final()
    {
        var trace = new List<string>();
        var dispatcher = NewDispatcher(
            new ResponseShapingHandler("shaper", priority: -1000, status: StatusCodes.Status401Unauthorized, trace),
            new RecordingHandler("observer", priority: 0, trace));

        var ctx = NewChallengeCtx();
        await dispatcher.DispatchChallenge(ctx, CancellationToken.None);

        ctx.ResponseHandled.Should().BeTrue();
        ctx.HttpContext.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        trace.Should().Equal("shaper", "observer");
    }

    [Fact]
    public async Task DispatchSignIn_stops_when_reject_is_set()
    {
        var trace = new List<string>();
        var dispatcher = NewDispatcher(
            new RejectingSignInHandler("rejector", priority: -1000, trace),
            new RecordingHandler("observer", priority: 0, trace));

        var ctx = new AuthSignInContext
        {
            Provider = "test",
            Identity = new System.Security.Claims.ClaimsIdentity("test"),
            Services = NewHttpContext().RequestServices,
            HttpContext = NewHttpContext(),
        };
        await dispatcher.DispatchSignIn(ctx, CancellationToken.None);

        ctx.RejectReason.Should().Be("rejected by test");
        trace.Should().Equal("rejector");
    }

    [Fact]
    public async Task DispatchChallenge_handler_exception_is_swallowed_and_pipeline_continues()
    {
        var trace = new List<string>();
        var dispatcher = NewDispatcher(
            new ThrowingHandler("thrower", priority: -1000),
            new RecordingHandler("after", priority: 0, trace));

        var ctx = NewChallengeCtx();
        await dispatcher.DispatchChallenge(ctx, CancellationToken.None);

        trace.Should().Equal("after");
    }

    [Fact]
    public async Task JsonChallengeHandler_sets_401_for_accept_json_requests()
    {
        var http = NewHttpContext();
        http.Request.Headers.Accept = "application/json";

        var ctx = new AuthChallengeContext
        {
            HttpContext = http,
            Services = http.RequestServices,
            DefaultRedirectUri = "/sign-in",
            RedirectUri = "/sign-in",
        };
        var handler = new JsonChallengeHandler(new TestOptionsMonitor<ChallengeOptions>(new ChallengeOptions()));

        await handler.OnChallenge(ctx, CancellationToken.None);

        ctx.ResponseHandled.Should().BeTrue();
        http.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task JsonChallengeHandler_sets_401_for_configured_api_path_even_without_accept_header()
    {
        var http = NewHttpContext("/v1/account/export");

        var ctx = new AuthChallengeContext
        {
            HttpContext = http,
            Services = http.RequestServices,
            DefaultRedirectUri = "/sign-in",
            RedirectUri = "/sign-in",
        };
        var handler = new JsonChallengeHandler(new TestOptionsMonitor<ChallengeOptions>(new ChallengeOptions
        {
            ApiPaths = new[] { "/v1" },
        }));

        await handler.OnChallenge(ctx, CancellationToken.None);

        ctx.ResponseHandled.Should().BeTrue();
        http.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task JsonChallengeHandler_noops_when_disabled()
    {
        var http = NewHttpContext();
        http.Request.Headers.Accept = "application/json";

        var ctx = new AuthChallengeContext
        {
            HttpContext = http,
            Services = http.RequestServices,
            DefaultRedirectUri = "/sign-in",
            RedirectUri = "/sign-in",
        };
        var handler = new JsonChallengeHandler(new TestOptionsMonitor<ChallengeOptions>(new ChallengeOptions
        {
            Enabled = false,
        }));

        await handler.OnChallenge(ctx, CancellationToken.None);

        ctx.ResponseHandled.Should().BeFalse();
        http.Response.StatusCode.Should().Be(200);  // default
    }

    [Fact]
    public async Task JsonChallengeHandler_does_not_overwrite_a_response_already_handled_by_an_earlier_handler()
    {
        var http = NewHttpContext();
        http.Request.Headers.Accept = "application/json";

        var ctx = new AuthChallengeContext
        {
            HttpContext = http,
            Services = http.RequestServices,
            DefaultRedirectUri = "/sign-in",
            RedirectUri = "/sign-in",
            ResponseHandled = true,
        };
        http.Response.StatusCode = StatusCodes.Status418ImATeapot;

        var handler = new JsonChallengeHandler(new TestOptionsMonitor<ChallengeOptions>(new ChallengeOptions()));
        await handler.OnChallenge(ctx, CancellationToken.None);

        http.Response.StatusCode.Should().Be(StatusCodes.Status418ImATeapot);
    }

    // ─── helpers ──────────────────────────────────────────────────────────────

    private static AuthFlowDispatcher NewDispatcher(params IKoanAuthFlowHandler[] handlers)
        => new(handlers, NullLogger<AuthFlowDispatcher>.Instance);

    private static AuthChallengeContext NewChallengeCtx()
    {
        var http = NewHttpContext();
        return new AuthChallengeContext
        {
            HttpContext = http,
            Services = http.RequestServices,
            DefaultRedirectUri = "/sign-in",
            RedirectUri = "/sign-in",
        };
    }

    private static HttpContext NewHttpContext(string path = "/some/page")
    {
        var http = new DefaultHttpContext();
        http.Request.Path = path;
        return http;
    }

    private sealed class RecordingHandler : IKoanAuthFlowHandler
    {
        private readonly string _name;
        private readonly List<string> _trace;
        public RecordingHandler(string name, int priority, List<string> trace) { _name = name; Priority = priority; _trace = trace; }
        public int Priority { get; }
        public Task OnChallenge(AuthChallengeContext ctx, CancellationToken ct) { _trace.Add(_name); return Task.CompletedTask; }
        public Task OnSignIn(AuthSignInContext ctx, CancellationToken ct) { _trace.Add(_name); return Task.CompletedTask; }
    }

    private sealed class ResponseShapingHandler : IKoanAuthFlowHandler
    {
        private readonly string _name;
        private readonly int _status;
        private readonly List<string> _trace;
        public ResponseShapingHandler(string name, int priority, int status, List<string> trace) { _name = name; Priority = priority; _status = status; _trace = trace; }
        public int Priority { get; }
        public Task OnChallenge(AuthChallengeContext ctx, CancellationToken ct)
        {
            _trace.Add(_name);
            ctx.HttpContext.Response.StatusCode = _status;
            ctx.ResponseHandled = true;
            return Task.CompletedTask;
        }
    }

    private sealed class RejectingSignInHandler : IKoanAuthFlowHandler
    {
        private readonly string _name;
        private readonly List<string> _trace;
        public RejectingSignInHandler(string name, int priority, List<string> trace) { _name = name; Priority = priority; _trace = trace; }
        public int Priority { get; }
        public Task OnSignIn(AuthSignInContext ctx, CancellationToken ct)
        {
            _trace.Add(_name);
            ctx.Reject("rejected by test");
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingHandler : IKoanAuthFlowHandler
    {
        private readonly string _name;
        public ThrowingHandler(string name, int priority) { _name = name; Priority = priority; }
        public int Priority { get; }
        public Task OnChallenge(AuthChallengeContext ctx, CancellationToken ct)
            => throw new InvalidOperationException($"{_name} blew up");
    }

    private sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public TestOptionsMonitor(T value) { CurrentValue = value; }
        public T CurrentValue { get; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
