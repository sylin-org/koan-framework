using Koan.Data.Backup.Core;
using Koan.Data.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Backup.Tests.Specs;

public sealed class AggregateDiscoveryTests
{
    [Fact]
    public void Registered_entities_resolve_provider_metadata_against_the_supplied_host()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        _ = AggregateConfigs.Get<BackupDiscoveryEntity, string>(services);

        var entity = AggregateConfigsExtensions.GetAllRegisteredEntities(services)
            .Single(candidate => candidate.EntityType == typeof(BackupDiscoveryEntity));

        entity.KeyType.Should().Be(typeof(string));
        entity.Provider.Should().Be(ProviderId);
    }

    private const string ProviderId = "backup-discovery-owner";

    [Koan.Data.Abstractions.DataAdapter(ProviderId)]
    private sealed class BackupDiscoveryEntity : Entity<BackupDiscoveryEntity>;
}
