using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.Testing.Integration;

/// <summary>
/// Canonical entry point for Koan integration tests, per ARCH-0079. Builds a real
/// <see cref="IHost"/> with config seeded from a dictionary, lets the test supply additional
/// service registrations (typically <c>services.AddKoan()</c>), and produces an
/// <c>await using</c>-disposable host.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not call <c>AddKoan()</c> inside the helper?</b> Different tests need different
/// bootstrap shapes — full <c>AddKoan()</c>, partial <c>AddKoanCore()</c>, mock-injecting
/// variants. Keeping the helper bootstrap-agnostic lets it serve every Koan integration
/// suite without becoming opinionated about which assemblies must be referenced.
/// </para>
/// <para>
/// <b>Cross-suite reuse:</b> replaces the per-suite ad-hoc <c>RedisCacheNode</c>-style
/// helpers introduced during the cache pillar's M4 cornerstone work. Adapter integration
/// suites adopt this helper directly; the canon (ARCH-0079) mandates new adapters use it
/// instead of inventing their own host wiring.
/// </para>
/// <para>
/// <b>Project placement (ARCH-0091):</b> this type lives in the xUnit-agnostic
/// <c>Koan.Testing.Hosting</c> project (no xUnit dependency) so it can be referenced by both
/// xUnit v2 and v3 consumers across the test-suite's framework migration.
/// </para>
/// </remarks>
public static class KoanIntegrationHost
{
    /// <summary>Start a fluent configuration chain for a new integration host.</summary>
    public static Builder Configure() => new();

    public sealed class Builder
    {
        private readonly Dictionary<string, string?> _settings = new(StringComparer.Ordinal);
        private Action<IServiceCollection>? _configureServices;
        private Action<IConfigurationBuilder>? _configureAppConfiguration;
        // A test host must never run as Production: the bare generic-host default ("Production") trips the
        // relational DDL guard (DDL-not-allowed-in-production), so durable adapters (Postgres/SqlServer) never
        // auto-create their tables. "Test" is the neutral choice — non-production (DDL allowed) but not
        // "Development" (which would arm self-orchestration heuristics). Override via WithEnvironment.
        private string _environment = "Test";

        /// <summary>Set a single configuration key/value pair (merged into in-memory config).</summary>
        public Builder WithSetting(string key, string? value)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("Configuration key cannot be empty.", nameof(key));
            _settings[key] = value;
            return this;
        }

        /// <summary>Override the host environment name (default "Test"). A non-production environment is
        /// required for relational adapters to auto-create schema in integration tests.</summary>
        public Builder WithEnvironment(string environment)
        {
            if (string.IsNullOrWhiteSpace(environment)) throw new ArgumentException("Environment cannot be empty.", nameof(environment));
            _environment = environment;
            return this;
        }

        /// <summary>Merge a dictionary of configuration key/value pairs into in-memory config.</summary>
        public Builder WithSettings(IEnumerable<KeyValuePair<string, string?>> settings)
        {
            if (settings is null) throw new ArgumentNullException(nameof(settings));
            foreach (var kvp in settings)
            {
                _settings[kvp.Key] = kvp.Value;
            }
            return this;
        }

        /// <summary>
        /// Extend the configuration pipeline with additional sources (env vars, JSON files, etc.).
        /// Runs after the in-memory <see cref="WithSetting"/> seed so later sources can override.
        /// </summary>
        public Builder ConfigureAppConfiguration(Action<IConfigurationBuilder> configure)
        {
            if (configure is null) throw new ArgumentNullException(nameof(configure));
            _configureAppConfiguration += configure;
            return this;
        }

        /// <summary>
        /// Register services. Typically called as
        /// <c>.ConfigureServices(s => s.AddKoan())</c> to trigger full reflective discovery,
        /// or with additional manual registrations for tests that need to inject mocks.
        /// </summary>
        public Builder ConfigureServices(Action<IServiceCollection> configure)
        {
            if (configure is null) throw new ArgumentNullException(nameof(configure));
            _configureServices += configure;
            return this;
        }

        /// <summary>
        /// Build the host without starting hosted services. Use when the test needs to
        /// inspect or manipulate the service provider before lifecycle start.
        /// </summary>
        public IntegrationHost Build()
        {
            var host = new HostBuilder()
                .UseEnvironment(_environment)
                .ConfigureAppConfiguration(cfg =>
                {
                    if (_settings.Count > 0) cfg.AddInMemoryCollection(_settings);
                    _configureAppConfiguration?.Invoke(cfg);
                })
                .ConfigureServices(services => _configureServices?.Invoke(services))
                .Build();

            return new IntegrationHost(host);
        }

        /// <summary>
        /// Build the host AND start hosted services. The returned <see cref="IntegrationHost"/>
        /// is ready to use; dispose via <c>await using</c> or call
        /// <see cref="IntegrationHost.StopAsync"/> explicitly.
        /// </summary>
        public async Task<IntegrationHost> StartAsync(CancellationToken ct = default)
        {
            var host = Build();
            await host.StartAsync(ct).ConfigureAwait(false);
            return host;
        }
    }
}

/// <summary>
/// Test-friendly handle over an <see cref="IHost"/>. Implements <see cref="IAsyncDisposable"/>
/// so test code can use <c>await using</c> — <see cref="IHost"/> itself only declares
/// <see cref="IDisposable"/>, which would prevent async disposal at the interface level.
/// </summary>
public sealed class IntegrationHost : IAsyncDisposable
{
    private readonly IHost _host;
    private bool _disposed;

    internal IntegrationHost(IHost host) { _host = host; }

    /// <summary>The DI container produced by the host.</summary>
    public IServiceProvider Services => _host.Services;

    /// <summary>The underlying <see cref="IHost"/> for tests that need direct lifecycle control.</summary>
    public IHost Host => _host;

    /// <summary>Start the host's hosted services. Most tests should use <c>StartAsync</c> on the builder instead.</summary>
    public Task StartAsync(CancellationToken ct = default) => _host.StartAsync(ct);

    /// <summary>Stop the host's hosted services gracefully.</summary>
    public Task StopAsync(CancellationToken ct = default) => _host.StopAsync(ct);

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try { await _host.StopAsync(CancellationToken.None).ConfigureAwait(false); }
        catch { /* teardown is best-effort */ }
        _host.Dispose();
    }
}
