using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

/// <summary>
/// Security regression guard (F2-sqlite). A malformed SQLite connection string that carries a secret
/// (<c>Password=</c> / SQLCipher key) must be DE-IDENTIFIED via <see cref="Redaction.DeIdentify"/>
/// before it is written to the <c>parse-failed</c> warning. The original swallow logged nothing; the
/// F2 burn-down added the warning, so this test pins that the warning is redacted, not raw.
/// Reverting <c>SqliteRepository</c>'s <c>Redaction.DeIdentify(cs)</c> back to <c>cs</c> makes this test
/// fail (the raw secret appears in the captured log). ARCH-0079: exercised through real <c>AddKoan()</c>.
/// </summary>
public sealed class SqliteConnectionStringRedactionSpec
{
    [Fact(DisplayName = "Sqlite: a malformed connection string is redacted (no secret leak) in the parse-failed warning")]
    public async Task Malformed_connection_string_secret_is_redacted_in_logs()
    {
        const string secret = "SuperSecret_DoNotLeak_9f3a";
        // 'Password' is a valid SQLite keyword (the SQLCipher key); 'Mode=NotARealMode' is an invalid
        // value for the typed Mode keyword, which forces SqliteConnectionStringBuilder to throw
        // ArgumentException -> the parse-failed catch that logs the (redacted) connection string.
        var malformedCs = $"Data Source=koan-redaction-probe.db;Password={secret};Mode=NotARealMode";

        var capture = new CapturingLoggerProvider();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Koan:Environment"] = "Test",
                ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
                // The SQLite adapter reads its connection string from this provider-specific key
                // (Constants.Configuration.Keys.ConnectionString) and assigns it WITHOUT validation,
                // so a malformed value reaches SqliteRepository's parse-failed path.
                ["Koan:Data:Sqlite:ConnectionString"] = malformedCs,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IHostApplicationLifetime, NoopLifetime>();
        services.AddSingleton<IHostEnvironment, TestHostEnvironment>();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(b => { b.SetMinimumLevel(LogLevel.Trace); b.AddProvider(capture); });
        services.AddKoan();

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = false,
        });
        try { KoanEnv.TryInitialize(provider); } catch { /* KoanEnv is sticky per-process; ignore. */ }

        var data = provider.GetRequiredService<IDataService>();
        var repo = data.GetRepository<RedactionProbe, string>();

        // Triggering an open with the malformed connection string fires the parse-failed catch. The
        // open ultimately faults, but what we pin is the (redacted) warning emitted along the way, not
        // whether the op throws — so capture any exception without asserting on it.
        var ex = await Record.ExceptionAsync(async () => await repo.Get("probe", CancellationToken.None));

        var messages = capture.Messages.ToArray();
        var parseFailed = messages.Where(m => m.Contains("parse-failed", StringComparison.Ordinal)).ToArray();

        parseFailed.Should().NotBeEmpty(
            "a malformed connection string must surface a parse-failed warning (op threw: {0}); connection-related captures: {1}",
            ex?.GetType().Name ?? "none",
            string.Join(" || ", messages.Where(m => m.Contains("connection", StringComparison.OrdinalIgnoreCase)).Take(15)));
        parseFailed.Should().NotContain(m => m.Contains(secret, StringComparison.Ordinal),
            "the raw connection-string secret must never appear in any log line");
        parseFailed.Should().Contain(m => m.Contains("Password=***", StringComparison.Ordinal),
            "the connection string must be de-identified via Redaction.DeIdentify before logging");
    }

    private sealed class RedactionProbe : Entity<RedactionProbe>
    {
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);
        public void Dispose() { }

        private sealed class CapturingLogger(ConcurrentQueue<string> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
                => sink.Enqueue($"{logLevel}|{formatter(state, exception)}");
        }
    }

    private sealed class NoopLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Koan.Data.Connector.Sqlite.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.PhysicalFileProvider(AppContext.BaseDirectory);
    }
}
