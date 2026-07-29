using Koan.Testing;
using Xunit;

namespace Koan.Testing.Tests;

public sealed class DataScenarioAndBenchmarkTests
{
    [Fact]
    public void Standard_scenarios_are_complete_unique_and_reference_stable_cells()
    {
        var expected = Enum.GetValues<DataScenarioKind>();

        Assert.Equal(expected.Length, DataScenarioCatalog.All.Count);
        Assert.Equal(expected, DataScenarioCatalog.All.Select(definition => definition.Kind).Order().ToArray());
        Assert.Equal(
            DataScenarioCatalog.All.Count,
            DataScenarioCatalog.All.Select(definition => definition.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(DataScenarioCatalog.All, definition =>
        {
            Assert.NotEmpty(definition.AcceptanceIds);
            Assert.True(definition.MinimumOperations > 0);
            Assert.All(definition.AcceptanceIds, id => Assert.Equal(id, DataConformanceCatalog.Acceptance(id).Id));
            Assert.Same(definition, DataScenarioCatalog.Require(definition.Kind));
        });
    }

    [Fact]
    public async Task Benchmark_observation_captures_pinned_fixture_and_all_required_metrics()
    {
        var fixture = new DataBenchmarkFixture("sample", "1.2.3", "4.5.6", "fixture-a");

        var observation = await DataBenchmarkRunner.Observe(
            fixture,
            "P-01/warm",
            DataBenchmarkPhase.Warm,
            (probe, _) =>
            {
                probe.Dispatch(7);
                probe.AddProviderWork(5);
                return ValueTask.CompletedTask;
            });

        Assert.Same(fixture, observation.Fixture);
        Assert.Equal("P-01/warm", observation.Cell);
        Assert.Equal(DataBenchmarkPhase.Warm, observation.Phase);
        Assert.True(observation.Elapsed >= TimeSpan.Zero);
        Assert.True(observation.AllocatedBytes >= 0);
        Assert.Equal(1, observation.ProviderDispatches);
        Assert.Equal(12, observation.ProviderWork);
    }

    [Fact]
    public void Benchmark_runner_exposes_observations_without_global_thresholds()
    {
        var publicStatic = typeof(DataBenchmarkRunner).GetMembers(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        Assert.DoesNotContain(publicStatic, member =>
            member.MemberType is System.Reflection.MemberTypes.Field or System.Reflection.MemberTypes.Property);
        Assert.DoesNotContain(publicStatic, member =>
            member.Name.Contains("Threshold", StringComparison.OrdinalIgnoreCase) ||
            member.Name.Contains("Expectation", StringComparison.OrdinalIgnoreCase));
    }
}
