using System;
using System.Collections.Generic;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Logging;
using Xunit;
using Koan.Core.Diagnostics;
using Koan.Core.Infrastructure;

namespace Koan.Tests.Integration.Bootstrap.Specs;

/// <summary>
/// (f) Unit cover for the MODULES-FAILED render block in
/// <see cref="KoanConsoleBlocks.BuildStartupOverviewBlock"/> (Track F · fail-fast.json). The recorded
/// failure channel is only useful if the boot report actually RENDERS it, so this pins:
/// (1) a snapshot carrying a <see cref="ModuleFailure"/> renders a <c>MODULES-FAILED</c> divider naming the
///     failing module, its phase, and its error; and
/// (2) the NEGATIVE — a snapshot with zero failures renders NO <c>MODULES-FAILED</c> divider (no false
///     positives, the no-failure boot report stays clean).
/// </summary>
/// <remarks>
/// A pure render unit test (no host boot): <see cref="KoanConsoleBlocks"/>, <see cref="RegistrySummarySnapshot"/>
/// and <see cref="ModuleFailure"/> are all internal to Koan.Core and reached via the suite's existing
/// InternalsVisibleTo. It is deliberately in the Bootstrap suite (not a separate unit project) because that is
/// where the InternalsVisibleTo grant already lives.
/// </remarks>
public sealed class ModulesFailedRenderSpec
{
    private const string FailingModule = "Demo.Failing.WidgetInitializer";
    private const string FailingPhase = "manifest-invoker(scanning 'Demo.Failing')";
    private const string FailingError = "widget factory exploded during scan";

    [Fact]
    public void Renders_MODULES_FAILED_block_with_module_phase_and_error()
    {
        var snapshot = SampleEnvironment();
        var registry = RegistryWithFailures(new ModuleFailure(FailingModule, "Demo.Failing", FailingPhase, FailingError));

        var block = KoanConsoleBlocks.BuildStartupOverviewBlock(
            snapshot,
            hostDescription: "test-host",
            modules: Array.Empty<(string, string)>(),
            runtimeVersion: "0.0.0-test",
            registry: registry,
            health: null);

        block.Should().Contain("MODULES-FAILED");
        block.Should().Contain(FailingModule);
        block.Should().Contain(FailingPhase);
        block.Should().Contain(FailingError);
    }

    [Fact]
    public void Renders_no_MODULES_FAILED_block_when_there_are_zero_failures()
    {
        var snapshot = SampleEnvironment();
        var registry = RegistryWithFailures(/* none */);

        var block = KoanConsoleBlocks.BuildStartupOverviewBlock(
            snapshot,
            hostDescription: "test-host",
            modules: Array.Empty<(string, string)>(),
            runtimeVersion: "0.0.0-test",
            registry: registry,
            health: null);

        block.Should().NotContain("MODULES-FAILED");
    }

    [Fact]
    public void Runtime_fact_projection_renders_the_same_redacted_failure_and_correction()
    {
        var fact = KoanFact.Create(
            Constants.Diagnostics.Codes.ModuleRejected,
            KoanFactKind.Rejection,
            KoanFactState.Rejected,
            FailingModule,
            "Koan rejected a module during activation.",
            Constants.Diagnostics.Reasons.ModuleActivationFailed,
            "Fix the module activation failure or remove the module reference.",
            "Demo.Failing",
            "bootstrap:demo");
        var envelope = new KoanFactEnvelope(Constants.Diagnostics.FactSchemaVersion, 1, "test", DateTimeOffset.UtcNow, true, [fact]);

        var block = KoanConsoleBlocks.BuildStartupOverviewBlock(
            SampleEnvironment(),
            hostDescription: "test-host",
            modules: Array.Empty<(string, string)>(),
            runtimeVersion: "0.0.0-test",
            registry: RegistryWithFailures(),
            health: null,
            runtimeFacts: envelope);

        block.Should().Contain("MODULES-FAILED");
        block.Should().Contain(FailingModule);
        block.Should().Contain(fact.Summary);
        block.Should().Contain("Fix the module activation failure",
            "human rendering may wrap corrective prose to the console width");
        block.Should().NotContain(FailingError, "raw exception text is not part of the shared fact");
    }

    private static RegistrySummarySnapshot RegistryWithFailures(params ModuleFailure[] failures)
        => new RegistrySummarySnapshot(
            Modules: 0,
            ModuleBreakdown: Array.Empty<(string, int)>(),
            BackgroundServices: 0,
            StartupBackgroundServices: 0,
            PeriodicBackgroundServices: 0,
            ServiceDiscoveryAdapters: 0,
            ModuleFailures: failures);

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
