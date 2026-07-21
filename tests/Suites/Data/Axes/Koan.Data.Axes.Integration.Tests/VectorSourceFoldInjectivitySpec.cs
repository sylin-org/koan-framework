using System;
using AwesomeAssertions;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Core.Model;
using Xunit;

namespace Koan.Data.Axes.Integration.Tests;

/// <summary>
/// ARCH-0103 — the Database-mode routed source is folded into the vector collection name as a leading particle (the
/// name-mangling Database floor). Like the partition and the container-name axis particles, the source MUST be
/// identifier-injective under the adapter's naming policy: on a case-folding / lossy-char store two distinct sources
/// could otherwise collapse to ONE physical collection (a cross-source leak). <see cref="StorageNameGenerator"/>
/// fail-closes on a non-injective source, exactly as it does for a container-name axis particle.
/// </summary>
public sealed class VectorSourceFoldInjectivitySpec
{
    private sealed class Doc : Entity<Doc> { }

    // A case-folding adapter (Lowercase=true) — Qdrant / Milvus shape.
    private static StorageNamingCapability CaseFolding() => new()
    {
        Style = StorageNamingStyle.EntityType,
        Casing = NameCasing.Lower,
        PartitionSeparator = '_',
        Partition = new PartitionTokenPolicy { GuidFormat = "N", Lowercase = true, AllowedExtraChars = "-_" },
    };

    [Fact(DisplayName = "vector Database source-fold: a non-injective (case-folding-collision) source fails closed, not a silent collapse")]
    public void Non_injective_source_throws()
    {
        var act = () => StorageNameGenerator.Resolve("qdrant", typeof(Doc), partition: null, source: "Mixed.Case", CaseFolding);
        act.Should().Throw<ArgumentException>().WithMessage("*not identifier-injective*");
    }

    [Fact(DisplayName = "vector Database source-fold: canonical sources resolve to DISTINCT collection names (Database isolation)")]
    public void Canonical_sources_resolve_distinct_names()
    {
        var a = StorageNameGenerator.Resolve("qdrant", typeof(Doc), partition: null, source: "tenant_a", CaseFolding);
        var b = StorageNameGenerator.Resolve("qdrant", typeof(Doc), partition: null, source: "tenant_b", CaseFolding);
        var def = StorageNameGenerator.Resolve("qdrant", typeof(Doc), partition: null, source: "Default", CaseFolding);
        var none = StorageNameGenerator.Resolve("qdrant", typeof(Doc), partition: null, CaseFolding);

        a.Should().NotBe(b);                 // distinct sources ⇒ distinct physical collections
        a.Should().Contain("tenant_a");
        def.Should().Be(none);               // "Default" contributes nothing ⇒ byte-identical to the source-free name
    }
}
