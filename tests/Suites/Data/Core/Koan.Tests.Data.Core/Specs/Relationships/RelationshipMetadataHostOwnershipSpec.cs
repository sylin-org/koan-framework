using AwesomeAssertions;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;
using Koan.Tests.Data.Core.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Koan.Tests.Data.Core.Specs.Relationships;

[Collection(nameof(RelationshipMetadataHostOwnershipSpec))]
[CollectionDefinition(nameof(RelationshipMetadataHostOwnershipSpec), DisableParallelization = true)]
public sealed class RelationshipMetadataHostOwnershipSpec
{
    [Fact]
    public async Task Sequential_hosts_resolve_relationship_metadata_from_the_active_provider()
    {
        const string FirstOwner = "relationship-host-a";
        const string SecondOwner = "relationship-host-b";

        OwnedRelationshipMetadata firstMetadata;
        await using (var first = await StartHost(FirstOwner))
        {
            firstMetadata = (OwnedRelationshipMetadata)first.Services.GetRequiredService<IRelationshipMetadata>();

            new RelationshipOwnershipEntity().GetRelationshipService()
                .Should().BeSameAs(firstMetadata);
        }

        firstMetadata.IsDisposed.Should().BeTrue("host A owns and disposes its metadata singleton");

        await using var second = await StartHost(SecondOwner);
        var secondMetadata = (OwnedRelationshipMetadata)second.Services.GetRequiredService<IRelationshipMetadata>();

        secondMetadata.Owner.Should().Be(SecondOwner);
        new RelationshipOwnershipEntity().GetRelationshipService()
            .Should().BeSameAs(secondMetadata,
                "a closed Entity type must resolve relationship metadata from the active host");
    }

    private static Task<DataCoreRuntimeFixture> StartHost(string owner)
        => DataCoreRuntimeFixture.CreateAsync(configureServices: services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IRelationshipMetadata>(
                _ => new OwnedRelationshipMetadata(owner)));
        });

    private sealed class RelationshipOwnershipEntity : Entity<RelationshipOwnershipEntity>;

    private sealed class OwnedRelationshipMetadata(string owner) : RelationshipMetadataService, IDisposable
    {
        public string Owner { get; } = owner;

        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }
}
