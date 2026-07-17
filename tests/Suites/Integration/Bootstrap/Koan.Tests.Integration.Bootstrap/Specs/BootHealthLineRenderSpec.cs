using System;
using System.Collections.Generic;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Logging;
using Koan.Core.Observability.Health;
using Xunit;

namespace Koan.Tests.Integration.Bootstrap.Specs;

/// <summary>
/// (H9) Unit cover for the boot-report Health line in
/// <see cref="KoanConsoleBlocks.BuildStartupOverviewBlock"/>. The report renders synchronously during host
/// startup, BEFORE <c>StartupProbeService</c>'s background task seeds the snapshot, so an empty snapshot at
/// that instant is a RACE — not a verdict. This pins that the line:
/// (1) says <c>probes pending</c> (and names how many are registered) when probes ARE registered but have
///     not reported yet — never the old racy <c>overall=Unknown</c> / fabricated <c>overall=Healthy</c>;
/// (2) says <c>none registered</c> when there is genuinely nothing to probe; and
/// (3) STILL renders a real <c>overall=</c> verdict once samples are present (no regression).
/// </summary>
/// <remarks>
/// A pure render unit test (no host boot): <see cref="KoanConsoleBlocks"/> and
/// <see cref="RegistrySummarySnapshot"/> are internal to Koan.Core and reached via the suite's existing
/// InternalsVisibleTo. Mirrors <see cref="ModulesFailedRenderSpec"/> (same directory, same grant).
/// </remarks>
public sealed class BootHealthLineRenderSpec
{
    [Fact]
    public void Renders_probes_pending_when_registered_but_no_samples_yet()
    {
        // Empty snapshot (the StartupProbeService background task has not pushed anything yet) + 3 registered.
        var emptySnapshot = new HealthSnapshot(HealthStatus.Healthy, Array.Empty<HealthSample>(), DateTimeOffset.UtcNow);

        var block = Render(health: emptySnapshot, registeredProbes: 3);

        block.Should().Contain("probes pending (registered=3)");
        // The whole point of the fix: no fabricated verdict from an empty aggregate.
        block.Should().NotContain("overall=Unknown");
        block.Should().NotContain("overall=Healthy");
    }

    [Fact]
    public void Renders_probes_pending_when_snapshot_is_null_but_probes_registered()
    {
        // The aggregator may not even be resolvable yet at boot-report time → null snapshot, probes still
        // registered. Must not collapse to the old "probes=0 overall=Unknown".
        var block = Render(health: null, registeredProbes: 5);

        block.Should().Contain("probes pending (registered=5)");
        block.Should().NotContain("overall=Unknown");
    }

    [Fact]
    public void Renders_none_registered_when_there_are_no_probes()
    {
        var block = Render(health: null, registeredProbes: 0);

        block.Should().Contain("none registered");
        block.Should().NotContain("overall=Unknown");
        block.Should().NotContain("probes pending");
    }

    [Fact]
    public void Still_renders_overall_verdict_once_samples_are_present()
    {
        // Once a probe has reported, the report must show the real aggregate — the pending path is only for
        // the boot-instant empty snapshot, not a permanent suppression.
        var sample = new HealthSample(
            Component: "db",
            Status: HealthStatus.Healthy,
            Message: "ok",
            TimestampUtc: DateTimeOffset.UtcNow,
            Ttl: null,
            Facts: new Dictionary<string, string> { ["critical"] = "true" });
        var snapshot = new HealthSnapshot(HealthStatus.Healthy, new[] { sample }, DateTimeOffset.UtcNow);

        var block = Render(health: snapshot, registeredProbes: 1);

        block.Should().Contain("overall=Healthy");
        block.Should().Contain("probes=1");
        block.Should().NotContain("probes pending");
    }

    private static string Render(HealthSnapshot? health, int registeredProbes)
        => KoanConsoleBlocks.BuildStartupOverviewBlock(
            SampleEnvironment(),
            hostDescription: "test-host",
            modules: Array.Empty<(string, string)>(),
            runtimeVersion: "0.0.0-test",
            registry: NoFailures(),
            health: health,
            registeredProbes: registeredProbes);

    private static RegistrySummarySnapshot NoFailures()
        => new RegistrySummarySnapshot(
            Modules: 0,
            ModuleBreakdown: Array.Empty<(string, int)>(),
            BackgroundServices: 0,
            StartupBackgroundServices: 0,
            PeriodicBackgroundServices: 0,
            ServiceDiscoveryAdapters: 0,
            ModuleFailures: Array.Empty<ModuleFailure>());

    private static KoanEnvironmentSnapshot SampleEnvironment()
        => new KoanEnvironmentSnapshot(
            EnvironmentName: "Test",
            IsDevelopment: false,
            IsProduction: false,
            IsStaging: false,
            InContainer: false,
            IsCi: false,
            AllowMagicInProduction: false,
            ProcessStart: DateTimeOffset.UtcNow,
            OrchestrationMode: OrchestrationMode.Standalone,
            SessionId: "test-session",
            AssemblyCount: 0,
            Application: ApplicationIdentitySnapshot.Empty);
}
