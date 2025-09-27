using FluentAssertions;
using S13.DocMind.Infrastructure;
using Xunit;

namespace S13.DocMind.UnitTests;

public class DocMindVectorHealthTests
{
    [Fact]
    public void SnapshotReflectsAuditAndTelemetry()
    {
        var health = new DocMindVectorHealth();

        health.RecordAudit(adapterAvailable: false, new[] { "profile-1" }, error: "adapter missing");
        health.RecordSearch(TimeSpan.FromMilliseconds(250), fallback: true);
        health.RecordGeneration(TimeSpan.FromMilliseconds(150), model: "test-model", succeeded: false);

        var snapshot = health.Snapshot();

        snapshot.AdapterAvailable.Should().BeFalse();
        snapshot.FallbackActive.Should().BeTrue();
        snapshot.MissingProfiles.Should().ContainSingle().Which.Should().Be("profile-1");
        snapshot.LastAuditError.Should().Be("adapter missing");
        snapshot.LastSearchLatencyMs.Should().Be(250);
        snapshot.LastGenerationDurationMs.Should().Be(150);
        snapshot.LastAdapterModel.Should().Be("test-model");
    }

    [Fact]
    public void RecordGenerationClearsFallbackWhenSuccessful()
    {
        var health = new DocMindVectorHealth();

        health.RecordAudit(adapterAvailable: true, Array.Empty<string>(), error: null);
        health.RecordSearch(TimeSpan.FromMilliseconds(100), fallback: false);
        health.RecordGeneration(TimeSpan.FromMilliseconds(90), model: "first", succeeded: true);

        var snapshot = health.Snapshot();

        snapshot.AdapterAvailable.Should().BeTrue();
        snapshot.FallbackActive.Should().BeFalse();
        snapshot.LastGenerationDurationMs.Should().Be(90);
        snapshot.LastAdapterModel.Should().Be("first");
    }
}
