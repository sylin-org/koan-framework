using AwesomeAssertions;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Relational.Npgsql;
using Xunit;

namespace Koan.Data.Relational.Tests.Connectors;

public sealed class NpgsqlWriteCapabilitiesSpec
{
    /// <summary>
    /// NK-057: PostgreSQL implements and declares insert-only and conditional delete, which owner-stamped HTTP creates
    /// and guarded deletes require. The live proof is PostgresInsertOnlySpec, which needs a server.
    /// </summary>
    [Fact]
    public void Postgres_declares_insert_only_and_conditional_delete()
    {
        typeof(IInsertOnlyRepository<Probe, string>).IsAssignableFrom(typeof(NpgsqlRepository<Probe, string>)).Should().BeTrue();
        typeof(IConditionalDeleteRepository<Probe, string>).IsAssignableFrom(typeof(NpgsqlRepository<Probe, string>)).Should().BeTrue();
        var capabilities = new CapabilitySet();
        NpgsqlFeatures.Describe(capabilities);
        capabilities.Has(DataCaps.Write.InsertOnly).Should().BeTrue();
        capabilities.Has(DataCaps.Write.ConditionalDelete).Should().BeTrue();
    }

    public sealed class Probe : IEntity<string>
    {
        public string Id { get; set; } = "";
    }
}
