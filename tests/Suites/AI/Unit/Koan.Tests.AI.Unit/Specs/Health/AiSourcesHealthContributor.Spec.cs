using AwesomeAssertions;
using Koan.AI;
using Koan.AI.Contracts.Sources;
using Koan.AI.Health;
using Koan.Core;
using Koan.Core.Observability.Health;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Tests.AI.Unit.Specs.Health;

/// <summary>
/// Tests for AiSourcesHealthContributor: AI subsystem health aggregation from source members.
/// </summary>
[Trait("ADR", "AI-0015")]
[Trait("Category", "Unit")]
public sealed class AiSourcesHealthContributorSpec
{
    [Fact]
    public async Task No_sources_reports_healthy()
    {
        var registry = new FakeSourceRegistry();
        var contributor = new AiSourcesHealthContributor(registry);

        var report = await contributor.Check();

        report.State.Should().Be(HealthState.Healthy);
        report.Description.Should().Contain("No AI sources registered");
    }

    [Fact]
    public async Task Disabled_sources_leave_runtime_health_immediately()
    {
        var registry = new FakeSourceRegistry();
        registry.RegisterSource(CreateSource("maintenance", members:
        [
            CreateMember("maintenance::one", MemberHealthState.Unhealthy)
        ]) with { IsEnabled = false });

        var report = await new AiSourcesHealthContributor(registry).Check();

        report.State.Should().Be(HealthState.Healthy);
        report.Description.Should().Contain("No enabled AI sources");
    }

    [Fact]
    public async Task All_members_healthy_reports_healthy()
    {
        var registry = new FakeSourceRegistry();
        registry.RegisterSource(CreateSource("ollama", members:
        [
            CreateMember("ollama::gpu-0", MemberHealthState.Healthy),
            CreateMember("ollama::gpu-1", MemberHealthState.Healthy)
        ]));

        var contributor = new AiSourcesHealthContributor(registry);

        var report = await contributor.Check();

        report.State.Should().Be(HealthState.Healthy);
        report.Description.Should().Be("2/2 members healthy");
    }

    [Fact]
    public async Task Some_members_unhealthy_reports_degraded()
    {
        var registry = new FakeSourceRegistry();
        registry.RegisterSource(CreateSource("ollama", members:
        [
            CreateMember("ollama::gpu-0", MemberHealthState.Healthy),
            CreateMember("ollama::gpu-1", MemberHealthState.Unhealthy)
        ]));

        var contributor = new AiSourcesHealthContributor(registry);

        var report = await contributor.Check();

        report.State.Should().Be(HealthState.Degraded);
        report.Description.Should().Contain("1/2 members healthy");
    }

    [Fact]
    public async Task All_members_unhealthy_reports_unhealthy()
    {
        var registry = new FakeSourceRegistry();
        registry.RegisterSource(CreateSource("ollama", members:
        [
            CreateMember("ollama::gpu-0", MemberHealthState.Unhealthy),
            CreateMember("ollama::gpu-1", MemberHealthState.Unhealthy)
        ]));

        var contributor = new AiSourcesHealthContributor(registry);

        var report = await contributor.Check();

        report.State.Should().Be(HealthState.Unhealthy);
        report.Description.Should().Contain("0/2 members healthy");
    }

    [Fact]
    public async Task Unknown_members_report_unknown_instead_of_false_green()
    {
        var registry = new FakeSourceRegistry();
        registry.RegisterSource(CreateSource("ollama", members:
        [
            CreateMember("ollama::gpu-0", MemberHealthState.Unknown),
            CreateMember("ollama::gpu-1", MemberHealthState.Unknown)
        ]));

        var contributor = new AiSourcesHealthContributor(registry);

        var report = await contributor.Check();

        report.State.Should().Be(HealthState.Unknown);
        report.Description.Should().Contain("health has not been established");
        report.Data!["unknownMembers"].Should().Be(2);
    }

    [Fact]
    public async Task Multiple_sources_aggregated()
    {
        var registry = new FakeSourceRegistry();

        // Source 1: all healthy
        registry.RegisterSource(CreateSource("ollama", members:
        [
            CreateMember("ollama::host", MemberHealthState.Healthy)
        ]));

        // Source 2: degraded (1 healthy, 1 unhealthy)
        registry.RegisterSource(CreateSource("enterprise", members:
        [
            CreateMember("enterprise::node-1", MemberHealthState.Healthy),
            CreateMember("enterprise::node-2", MemberHealthState.Unhealthy)
        ]));

        var contributor = new AiSourcesHealthContributor(registry);

        var report = await contributor.Check();

        report.State.Should().Be(HealthState.Degraded, "one unhealthy member across sources degrades overall health");
        report.Description.Should().Contain("2/3 members healthy");
        report.Data.Should().NotBeNull();
        report.Data!["totalMembers"].Should().Be(3);
        report.Data["healthyMembers"].Should().Be(2);
        report.Data["sources"].Should().Be(2);
    }

    [Fact]
    public void AddAi_composes_with_an_existing_health_contributor_exactly_once()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IHealthContributor, ExistingHealthContributor>();

        services.AddAi();
        services.AddAi();

        using var provider = services.BuildServiceProvider();
        var contributors = provider.GetServices<IHealthContributor>().ToArray();
        contributors.Should().ContainSingle(contributor => contributor is ExistingHealthContributor);
        contributors.Should().ContainSingle(contributor => contributor.Name == "Koan.AI");
    }

    // ========================================================================
    // Helpers
    // ========================================================================

    private static AiMemberDefinition CreateMember(string name, MemberHealthState healthState) =>
        new()
        {
            Name = name,
            ConnectionString = $"test://{name}",
            Order = 0,
            HealthState = healthState
        };

    private static AiSourceDefinition CreateSource(string name, List<AiMemberDefinition> members) =>
        new()
        {
            Name = name,
            Provider = name,
            Priority = 50,
            Members = members
        };

    // ========================================================================
    // Fake Source Registry
    // ========================================================================

    private sealed class FakeSourceRegistry : IAiSourceRegistry
    {
        private readonly Dictionary<string, AiSourceDefinition> _sources = new(StringComparer.OrdinalIgnoreCase);

        public void RegisterSource(AiSourceDefinition source) => _sources[source.Name] = source;
        public AiSourceDefinition? GetSource(string name) => _sources.TryGetValue(name, out var s) ? s : null;
        public bool TryGetSource(string name, out AiSourceDefinition? source) { source = GetSource(name); return source is not null; }
        public IReadOnlyCollection<string> GetSourceNames() => _sources.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();

        public IReadOnlyCollection<AiSourceDefinition> GetAllSources()
            => _sources.Values.OrderByDescending(s => s.Priority).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToArray();

        public bool HasSource(string name) => _sources.ContainsKey(name);

        public IReadOnlyCollection<AiSourceDefinition> GetSourcesWithCapability(string capabilityName)
            => _sources.Values
                .Where(s => s.Capabilities.ContainsKey(capabilityName) ||
                           s.Members.Any(m => m.Capabilities?.ContainsKey(capabilityName) == true))
                .OrderByDescending(s => s.Priority)
                .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
    }

    private sealed class ExistingHealthContributor : IHealthContributor
    {
        public string Name => "existing";
        public bool IsCritical => true;
        public Task<HealthReport> Check(CancellationToken ct = default) =>
            Task.FromResult(new HealthReport(Name, HealthState.Healthy, "healthy", null, null));
    }
}
