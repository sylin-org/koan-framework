using System.Collections.Concurrent;
using Koan.Core;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Core.Orchestration.Composition;
using Koan.Core.Logging;
using Koan.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Koan.Tests.Core.Unit.Specs.Redaction;

public sealed class RedactionSpec
{
    [Fact]
    public void Structured_Koan_log_context_is_deidentified_at_the_shared_sink()
    {
        const string action = "redaction.structured-sink";
        const string connectionSecret = "SinkConnectionSecret";
        const string uriSecret = "SinkUriSecret";
        const string exceptionSecret = "SinkExceptionSecret";
        (string Key, object? Value)[]? captured = null;

        KoanLog.TestSink = (_, _, candidateAction, _, context) =>
        {
            if (candidateAction == action) captured = context;
        };

        try
        {
            KoanLog.ConfigInfo(
                logger: null,
                action,
                "probe",
                ("connection", $"Server=db;Password={connectionSecret};Database=app"),
                ("endpoint", new Uri($"https://agent:{uriSecret}@example.test/path?view=full")),
                ("failure", new InvalidOperationException($"Request failed (Password={exceptionSecret})")),
                ("count", 7));
        }
        finally
        {
            KoanLog.TestSink = null;
        }

        captured.Should().NotBeNull();
        var rendered = string.Join(" | ", captured!.Select(item => $"{item.Key}={item.Value}"));
        rendered.Should().NotContain(connectionSecret);
        rendered.Should().NotContain(uriSecret);
        rendered.Should().NotContain(exceptionSecret);
        rendered.ToLowerInvariant().Should().Contain("password=***");
        rendered.Should().Contain("https://***@example.test/path?view=full");
        captured.Single(item => item.Key == "count").Value.Should().Be(7,
            "non-string structured values must retain their type and value");
    }

    [Theory]
    [InlineData("Server=db;Password=plain-secret;Database=app", "plain-secret")]
    [InlineData("Server=db;Password=\"Super Secret\";Database=app", "Super Secret")]
    [InlineData("Server=db;Password=\"Super;Secret\";Database=app", "Super;Secret")]
    [InlineData("Server=db;User Id=\"Jane Doe\";Password=secret;Database=app", "Jane Doe")]
    public void Connection_string_credentials_are_masked(string input, string sensitiveValue)
    {
        var result = Koan.Core.Redaction.DeIdentify(input);

        result.Should().NotContain(sensitiveValue);
        result.Should().Contain("***");
        result.ToLowerInvariant().Should().Contain("database=app");
    }

    [Theory]
    [InlineData("Server=db;Password=\"Super Secret")]
    [InlineData("Server=db;Password=secret;Broken")]
    [InlineData("localhost:6379,password=secret,ssl=true")]
    [InlineData("Connection failed (Password=secret)")]
    [InlineData("Connection failed (Password)=secret")]
    public void Malformed_or_nonstandard_sensitive_strings_fail_closed(string input)
    {
        Koan.Core.Redaction.DeIdentify(input).Should().Be("(masked)");
    }

    [Fact]
    public void Quoted_sensitive_keys_are_masked_when_the_connection_grammar_accepts_them()
    {
        var result = Koan.Core.Redaction.DeIdentify("Server=db;'Password'=QuotedKeySecret");

        result.Should().NotContain("QuotedKeySecret");
        result.Should().Contain("***");
    }

    [Fact]
    public void Uri_user_information_and_sensitive_query_parameters_are_masked()
    {
        const string input =
            "mongodb+srv://agent:UserInfoSecret@cluster.example/app?view=full&api_key=QuerySecret&password=\"Query;With&Delimiters\"&sig=SignedSecret#access_token=FragmentSecret&status=ok";

        var result = Koan.Core.Redaction.DeIdentify(input);

        result.Should().Be(
            "mongodb+srv://***@cluster.example/app?view=full&api_key=***&password=***&sig=***#access_token=***&status=ok");
    }

    [Fact]
    public void Nonstandard_uri_path_credentials_fail_closed()
    {
        Koan.Core.Redaction.DeIdentify("https://example.test/app;password=PathSecret")
            .Should().Be("(masked)");
    }

    [Theory]
    [InlineData("Failed to connect to mongodb://agent:EmbeddedSecret@db.example/app", "EmbeddedSecret")]
    [InlineData("Endpoint=https://agent:NestedSecret@db.example;Database=app", "NestedSecret")]
    public void Credentialed_uris_are_masked_inside_prose_and_connection_values(
        string input,
        string sensitiveValue)
    {
        var result = Koan.Core.Redaction.DeIdentify(input);

        result.Should().NotContain(sensitiveValue);
        result.Should().Contain("***@", "the useful endpoint shape should remain when its grammar is unambiguous");
    }

    [Theory]
    [InlineData("x-amz-security-token")]
    [InlineData("session_token")]
    [InlineData("auth_token")]
    [InlineData("api_token")]
    [InlineData("x-goog-signature")]
    [InlineData("x-goog-credential")]
    public void Common_signed_endpoint_token_keys_are_masked(string key)
    {
        var result = Koan.Core.Redaction.DeIdentify($"https://example.test/object?view=full&{key}=CloudSecret");

        result.Should().NotContain("CloudSecret");
        result.Should().Contain($"{key}=***");
    }

    [Theory]
    [InlineData(null, "(null)")]
    [InlineData("", "(null)")]
    [InlineData("Server=db;Database=app", "Server=db;Database=app")]
    [InlineData("Server=db;NotPassword=visible", "Server=db;NotPassword=visible")]
    [InlineData("https://example.test/path?view=full", "https://example.test/path?view=full")]
    public void Inputs_without_sensitive_material_preserve_the_existing_contract(string? input, string expected)
    {
        Koan.Core.Redaction.DeIdentify(input).Should().Be(expected);
    }

    [Fact]
    public async Task Shared_discovery_logs_never_emit_raw_candidate_credentials()
    {
        var capture = new LogCapture();
        var adapter = new SensitiveCandidateAdapter(new CapturingLogger<SensitiveCandidateAdapter>(capture));
        using var services = new ServiceCollection().BuildServiceProvider();
        var coordinator = new ServiceDiscoveryCoordinator(
            [adapter],
            new ServiceDiscoveryRuntime(services, ServiceDiscoveryPlan.Empty),
            new CapturingLogger<ServiceDiscoveryCoordinator>(capture));

        var result = await coordinator.DiscoverService("redaction-test", new DiscoveryContext
        {
            RequireHealthValidation = false
        });

        result.IsSuccessful.Should().BeTrue();
        result.ServiceUrl.Should().Contain("CandidateSecret", "discovery results remain application-facing values");
        capture.Messages.Should().NotContain(message => message.Contains("CandidateSecret", StringComparison.Ordinal));
        capture.Messages.Should().NotContain(message => message.Contains("QuerySecret", StringComparison.Ordinal));
        capture.Messages.Should().Contain(message => message.Contains("***@", StringComparison.Ordinal));
        capture.Messages.Should().Contain(message => message.Contains("api_key=***", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Shared_discovery_health_failures_never_emit_raw_exception_credentials()
    {
        var capture = new LogCapture();
        var adapter = new FailingSensitiveCandidateAdapter(
            new CapturingLogger<FailingSensitiveCandidateAdapter>(capture));
        using var services = new ServiceCollection().BuildServiceProvider();
        var coordinator = new ServiceDiscoveryCoordinator(
            [adapter],
            new ServiceDiscoveryRuntime(services, ServiceDiscoveryPlan.Empty),
            new CapturingLogger<ServiceDiscoveryCoordinator>(capture));

        var result = await coordinator.DiscoverService("redaction-test", new DiscoveryContext
        {
            RequireHealthValidation = true
        });

        result.IsSuccessful.Should().BeFalse();
        capture.Messages.Should().NotContain(message => message.Contains("CandidateSecret", StringComparison.Ordinal));
        capture.Messages.Should().NotContain(message => message.Contains("QuerySecret", StringComparison.Ordinal));
        capture.Messages.Should().NotContain(message => message.Contains("HealthSecret", StringComparison.Ordinal));
        capture.Messages.Should().Contain(message => message.Contains("error=(masked)", StringComparison.OrdinalIgnoreCase));
    }

    [KoanService(ServiceKind.Database, shortCode: "redaction-test", name: "Redaction Test",
        Scheme = "tcp", Host = "redaction-test", EndpointPort = 1,
        LocalScheme = "tcp", LocalHost = "localhost", LocalPort = 1)]
    private sealed class SensitiveCandidateFactory { }

    private sealed class SensitiveCandidateAdapter(ILogger<SensitiveCandidateAdapter> logger)
        : ServiceDiscoveryAdapterBase(new ConfigurationBuilder().Build(), logger)
    {
        private const string SensitiveCandidate =
            "amqp://agent:CandidateSecret@broker.example:5672/app?api_key=QuerySecret";

        public override string ServiceName => "redaction-test";

        protected override Type GetFactoryType() => typeof(SensitiveCandidateFactory);

        protected override IEnumerable<DiscoveryCandidate> GetEnvironmentCandidates() =>
            [new DiscoveryCandidate(SensitiveCandidate, "environment", 0)];

        protected override Task<bool> ValidateServiceHealth(
            string serviceUrl,
            DiscoveryContext context,
            CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class FailingSensitiveCandidateAdapter(ILogger<FailingSensitiveCandidateAdapter> logger)
        : ServiceDiscoveryAdapterBase(new ConfigurationBuilder().Build(), logger)
    {
        private const string SensitiveCandidate =
            "amqp://agent:CandidateSecret@broker.example:5672/app?api_key=QuerySecret";

        public override string ServiceName => "redaction-test";

        protected override Type GetFactoryType() => typeof(SensitiveCandidateFactory);

        protected override IEnumerable<DiscoveryCandidate> GetEnvironmentCandidates() =>
            [new DiscoveryCandidate(SensitiveCandidate, "environment", 0)];

        protected override Task<bool> ValidateServiceHealth(
            string serviceUrl,
            DiscoveryContext context,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Health probe failed (Password=HealthSecret)");
    }

    private sealed class LogCapture
    {
        public ConcurrentQueue<string> Messages { get; } = new();
    }

    private sealed class CapturingLogger<T>(LogCapture capture) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            capture.Messages.Enqueue(formatter(state, exception));
    }
}
