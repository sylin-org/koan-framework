using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Xunit;

namespace Koan.Testing.Containers;

/// <summary>
/// ARCH-0091 base for engine container fixtures. Owns the xUnit <see cref="IAsyncLifetime"/>
/// lifecycle, an explicit env-var connection override (the only piece of the old harness's
/// env→local→container cascade kept — for CI lanes that provide a pre-running service), the
/// fail-closed infrastructure contract, and the Koan settings hand-off. Subclasses only build/start their
/// official Testcontainers module container and return its connection string.
/// </summary>
/// <remarks>
/// Shared once per engine assembly via <c>[assembly: AssemblyFixture(typeof(TFixture))]</c>; the
/// container starts in <see cref="InitializeAsync"/> before any test and disposes after all.
/// Dockerless file/in-memory adapters (JSON, SQLite, in-memory) reuse this base unchanged: their
/// <see cref="StartContainerAsync"/> resolves a temp path instead of a container and never throws,
/// so <see cref="IsAvailable"/> is always true after successful setup.
/// Native Docker-host auto-discovery (Testcontainers 4.x) makes the old Docker.DotNet probe and the
/// MissingMethodException→docker-run CLI fallback obsolete. Infrastructure startup and teardown
/// failures remain test failures; a required native lane never treats missing Docker as evidence.
/// </remarks>
public abstract class KoanContainerFixture : IAsyncLifetime
{
    /// <summary>Short engine key, e.g. <c>"postgres"</c>. Drives the env override + partition prefix.</summary>
    public abstract string Engine { get; }

    /// <summary>The Koan data adapter id, e.g. <c>"postgres"</c>.</summary>
    protected abstract string Adapter { get; }

    /// <summary>True once a usable backing store (container or env override) is ready.</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>Why fixture startup failed. Null when available.</summary>
    public string? Reason { get; private set; }

    /// <summary>The resolved connection string (container or env override).</summary>
    public string ConnectionString { get; private set; } = "";

    /// <summary>Koan configuration to feed <c>KoanIntegrationHost</c> (adapter + connection string + engine extras).</summary>
    public IReadOnlyDictionary<string, string?> Settings { get; private set; } =
        new Dictionary<string, string?>();

    /// <summary>
    /// The settings handed to <c>KoanIntegrationHost</c> for a SINGLE boot. Defaults to the shared
    /// <see cref="Settings"/> — container fixtures reuse one backing store across every boot, isolating
    /// per test by partition. File-based dockerless fixtures override this to provision a fresh per-boot
    /// location so a spec that asserts over the WHOLE on-disk store (not just its partition) sees only its
    /// own data — restoring the legacy one-store-per-test isolation at zero cost (no container to restart).
    /// </summary>
    public virtual IReadOnlyDictionary<string, string?> SettingsForBoot() => Settings;

    /// <summary>Build + start the module container and return its connection string. Throws if Docker is absent.</summary>
    protected abstract Task<string> StartContainerAsync();

    /// <summary>Stop/dispose the container. Best-effort.</summary>
    protected abstract ValueTask StopContainerAsync();

    /// <summary>
    /// Engine-specific configuration beyond the canonical <c>Koan:Data:Sources:Default:*</c> keys —
    /// e.g. the adapter-scoped connection string alias and disabling Koan's redundant readiness gate
    /// (Testcontainers already waited; per-boot gating churns across rapid host cycles — the JobsHarness lesson).
    /// </summary>
    protected virtual IEnumerable<KeyValuePair<string, string?>> ExtraSettings(string connectionString)
        => Array.Empty<KeyValuePair<string, string?>>();

    public async ValueTask InitializeAsync()
    {
        // 1. Explicit env override (CI bring-your-own-service lane): Koan_<ENGINE>__CONNECTION_STRING.
        var env = Environment.GetEnvironmentVariable($"Koan_{Engine.ToUpperInvariant()}__CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(env))
        {
            ConnectionString = env;
            MarkAvailable();
            return;
        }

        // 2. Module container (Testcontainers 4.x native Docker auto-discovery).
        try
        {
            ConnectionString = await StartContainerAsync().ConfigureAwait(false);
            MarkAvailable();
        }
        catch (Exception startFailure)
        {
            Reason = $"{Engine} container unavailable: {startFailure.GetType().Name}: {startFailure.Message}";
            try
            {
                await StopContainerAsync().ConfigureAwait(false);
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(
                    $"{Engine} container startup and cleanup both failed.",
                    startFailure,
                    cleanupFailure);
            }

            ExceptionDispatchInfo.Capture(startFailure).Throw();
        }
    }

    private void MarkAvailable()
    {
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Environment"] = "Test",
            ["Koan:Data:Sources:Default:Adapter"] = Adapter,
        };
        // Dockerless file adapters (e.g. JSON) key off an engine-specific path supplied via
        // ExtraSettings, not a source connection string — omit the empty value so their config
        // matches the legacy support fixtures exactly.
        if (!string.IsNullOrEmpty(ConnectionString))
        {
            settings["Koan:Data:Sources:Default:ConnectionString"] = ConnectionString;
        }
        foreach (var kvp in ExtraSettings(ConnectionString))
        {
            settings[kvp.Key] = kvp.Value;
        }
        Settings = settings;
        IsAvailable = true;
        Reason = null;
    }

    public ValueTask DisposeAsync() => StopContainerAsync();
}
