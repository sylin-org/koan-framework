using Koan.Cache.Abstractions.Policies;
using Koan.Core;
using Koan.Core.Diagnostics;
using Koan.Data.Core.Model;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Tests.Cache.Topology.Specs;

public sealed class EntityCacheCompositionSpec
{
    [Fact]
    public async Task Startup_reports_the_effective_entity_cache_plan()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await KoanIntegrationHost.Configure()
            .ConfigureServices(services => services.AddKoan())
            .StartAsync(ct);

        var facts = host.Services.GetRequiredService<IKoanRuntimeFacts>().Current.Facts;

        facts.Should().Contain(fact =>
            fact.Code == "koan.cache.policies.discovered"
            && fact.Summary.Contains("Entity entry plan", StringComparison.Ordinal)
            && !fact.Summary.StartsWith("Koan materialized 0 ", StringComparison.Ordinal));
        facts.Should().Contain(fact =>
            fact.Code == "koan.cache.entity-plan.resolved"
            && fact.Subject.EndsWith(typeof(ReportedCacheEntity).FullName!, StringComparison.Ordinal)
            && fact.Summary.Contains(CacheableAttribute.DefaultKeyTemplate, StringComparison.Ordinal));
        facts.Should().Contain(fact =>
            fact.Code == "koan.cache.local.selected"
            && fact.Subject == "cache:local"
            && fact.ReasonCode == "priority-selection");
    }
}

[Cacheable]
public sealed class ReportedCacheEntity : Entity<ReportedCacheEntity>;
