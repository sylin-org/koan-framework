using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Diagnostics;
using Koan.Core.Hosting.App;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Infrastructure;
using Koan.Core.Logging;
using Xunit;

namespace Koan.Tests.Integration.Bootstrap.Specs;

public sealed class SegmentationGuaranteeRenderSpec
{
    [Fact]
    public void Startup_renders_value_free_segmentation_guarantees()
    {
        var fact = KoanFact.Create(
            "spec.guarantee.storage",
            KoanFactKind.Guarantee,
            KoanFactState.Observed,
            "segmentation:storage",
            "Hard segmentation is enforced-or-rejected through 'path-prefix' for: list, typed.key.",
            Constants.Diagnostics.Reasons.SegmentationRealizationInstalled,
            null,
            "Koan.Core",
            "segmentation:storage");
        var envelope = new KoanFactEnvelope(Constants.Diagnostics.FactSchemaVersion, 1, "test", DateTimeOffset.UnixEpoch, true, [fact]);

        var block = KoanConsoleBlocks.BuildStartupOverviewBlock(
            SampleEnvironment(),
            "test-host",
            [],
            "0.0-test",
            registry: null,
            health: null,
            runtimeFacts: envelope);

        block.Should().Contain("Guarantees");
        block.Should().Contain("segmentation:storage");
        block.Should().Contain("enforced-or-rejected");
        block.Should().Contain("list, typed.key");
    }

    [Fact]
    public void Startup_does_not_infer_guarantee_meaning_from_a_fact_code()
    {
        var fact = KoanFact.Create(
            Constants.Diagnostics.Codes.SegmentationRealizationActive,
            KoanFactKind.Discovery,
            KoanFactState.Observed,
            "segmentation:storage",
            "This is discovery, not a guarantee.",
            Constants.Diagnostics.Reasons.SegmentationRealizationInstalled,
            null,
            "Koan.Core",
            "segmentation:storage");
        var envelope = new KoanFactEnvelope(2, 1, "test", DateTimeOffset.UnixEpoch, true, [fact]);

        var block = KoanConsoleBlocks.BuildStartupOverviewBlock(
            SampleEnvironment(),
            "test-host",
            [],
            "0.0-test",
            registry: null,
            health: null,
            runtimeFacts: envelope);

        block.Should().NotContain("Guarantees");
        block.Should().NotContain("This is discovery, not a guarantee.");
    }

    private static KoanEnvironmentSnapshot SampleEnvironment()
        => new(
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
