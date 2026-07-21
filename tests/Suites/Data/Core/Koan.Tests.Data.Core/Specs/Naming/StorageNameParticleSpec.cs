using System;
using System.Threading;
using AwesomeAssertions;
using Koan.Core.Naming;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Core.Model;
using Xunit;

namespace Koan.Tests.Data.Core.Specs.Naming;

/// <summary>
/// ARCH-0101 §3 — the container-name particle plane. A separate-container axis (tenancy) registers an
/// <see cref="IStorageNameParticleContributor"/> that folds a LEADING particle around the anchor (<c>T1-base</c>),
/// the partition still TRAILS (<c>#alpha</c>), and "the axis is never in the spine" (the anchor <c>base</c> is
/// untouched). The decisive security pin: the ambient axis value is in the name cache key, so two axis values get
/// DISTINCT names — a per-container name can never cache and serve across tenants. A non-applicable type stays
/// byte-identical even with the contributor registered.
/// </summary>
[Collection("storage-name-particle-registry")]   // serialize: the registry is process-global static state
public sealed class StorageNameParticleSpec : IDisposable
{
    private static readonly AsyncLocal<string?> _tenant = new();

    public StorageNameParticleSpec() => StorageNameParticleRegistry.Reset();
    public void Dispose() { _tenant.Value = null; StorageNameParticleRegistry.Reset(); }

    private static IDisposable Tenant(string id)
    {
        var prev = _tenant.Value;
        _tenant.Value = id;
        return new Pop(() => _tenant.Value = prev);
    }

    private sealed class Pop(Action undo) : IDisposable { public void Dispose() => undo(); }

    public sealed class NamedDoc : Entity<NamedDoc> { }
    public sealed class OtherDoc : Entity<OtherDoc> { }

    // The separate-container tenant axis: a LEADING particle joined with '-', only for NamedDoc, only when a tenant
    // is in scope (host / no-tenant ⇒ null ⇒ byte-identical name).
    private sealed class TenantNameParticle : IStorageNameParticleContributor
    {
        public string Axis => "tenant";
        public Particle? GetParticle(Type entityType)
        {
            if (entityType != typeof(NamedDoc)) return null;
            var t = _tenant.Value;
            return t is null ? null : new Particle(100, "tenant", t, ParticlePosition.Leading, "-");
        }
    }

    private static Func<StorageNamingCapability> Cap() => static () => new StorageNamingCapability { NameOverride = _ => "base" };
    // A case-folding adapter (e.g. Postgres lowercases identifiers) — proves the policy-aware injectivity guard.
    private static StorageNamingCapability LowercaseCap() => new() { NameOverride = _ => "base", Partition = new PartitionTokenPolicy { Lowercase = true } };

    [Fact]
    public void A_leading_axis_particle_wraps_the_anchor_and_the_partition_still_trails()
    {
        StorageNameParticleRegistry.Register(new TenantNameParticle());
        using (Tenant("T1"))
            StorageNameGenerator.Generate(typeof(NamedDoc), "alpha", Cap()()).Should().Be("T1-base#alpha");
    }

    [Fact]
    public void No_axis_in_scope_is_byte_identical()
    {
        StorageNameParticleRegistry.Register(new TenantNameParticle());
        // NamedDoc with no tenant in scope ⇒ no particle ⇒ the bare anchor + partition.
        StorageNameGenerator.Generate(typeof(NamedDoc), "alpha", Cap()()).Should().Be("base#alpha");
    }

    [Fact]
    public void A_non_applicable_type_is_byte_identical_even_with_a_contributor_registered()
    {
        StorageNameParticleRegistry.Register(new TenantNameParticle());
        using (Tenant("T1"))   // active tenant, but the contributor returns null for OtherDoc
            StorageNameGenerator.Generate(typeof(OtherDoc), "alpha", Cap()()).Should().Be("base#alpha");
    }

    [Fact]
    public void Empty_registry_is_byte_identical()
    {
        // No contributor at all — the pre-ARCH-0101 path.
        StorageNameGenerator.Generate(typeof(NamedDoc), "alpha", Cap()()).Should().Be("base#alpha");
    }

    [Fact]
    public void The_axis_value_is_in_the_name_cache_key_so_two_tenants_get_distinct_names()
    {
        // The decisive security pin: a per-container name (T1-base) must NEVER cache and serve to another tenant.
        StorageNameParticleRegistry.Register(new TenantNameParticle());
        const string provider = "name-particle-spec"; // unique provider isolates the static cache

        string n1, n2, host;
        using (Tenant("T1")) n1 = StorageNameGenerator.Resolve(provider, typeof(NamedDoc), "alpha", Cap());
        using (Tenant("T2")) n2 = StorageNameGenerator.Resolve(provider, typeof(NamedDoc), "alpha", Cap());
        host = StorageNameGenerator.Resolve(provider, typeof(NamedDoc), "alpha", Cap());

        n1.Should().Be("T1-base#alpha");
        n2.Should().Be("T2-base#alpha");   // NOT T1's cached name
        host.Should().Be("base#alpha");    // no tenant ⇒ bare anchor
    }

    [Fact]
    public void A_lossy_axis_value_fails_closed_so_two_scopes_can_never_share_a_container()
    {
        // ARCH-0101 §8 + the adversarial-review finding: 'acme/east' and 'acme_east' both sanitize to 'acme_east' — a
        // silent cross-scope container share. The seam fails closed on the non-injective value (parity with the
        // partition front-door, PartitionNameValidator).
        StorageNameParticleRegistry.Register(new TenantNameParticle());
        using (Tenant("acme/east"))
        {
            var act = () => StorageNameGenerator.Generate(typeof(NamedDoc), "alpha", Cap()());
            act.Should().Throw<ArgumentException>().WithMessage("*not identifier-injective*");
        }
        using (Tenant("acme_east"))   // the canonical token is fine
            StorageNameGenerator.Generate(typeof(NamedDoc), "alpha", Cap()()).Should().Be("acme_east-base#alpha");
    }

    [Fact]
    public void On_a_case_folding_adapter_a_mixed_case_axis_value_fails_closed()
    {
        // 'Acme' and 'acme' both fold to 'acme' on a Lowercase adapter — the policy-aware guard rejects the non-canonical one.
        StorageNameParticleRegistry.Register(new TenantNameParticle());
        using (Tenant("Acme"))
        {
            var act = () => StorageNameGenerator.Generate(typeof(NamedDoc), "alpha", LowercaseCap());
            act.Should().Throw<ArgumentException>().WithMessage("*not identifier-injective*");
        }
        using (Tenant("acme"))   // already lower-case ⇒ canonical ⇒ accepted
            StorageNameGenerator.Generate(typeof(NamedDoc), "alpha", LowercaseCap()).Should().Be("acme-base#alpha");
    }
}
