using System;
using Koan.Core.Modules.Pillars;
using Koan.Core.Provenance;
using AwesomeAssertions;
using Xunit;

namespace Koan.Core.Tests;

public class PillarCatalogSpec
{
    [Fact]
    public void RegisterDescriptor_AllowsLookupByCodeLabelAndNamespace()
    {
        var code = $"pillar-{Guid.NewGuid():N}";
        var descriptor = new KoanPillarCatalog.PillarDescriptor(code, "Test Pillar", "#123456", "🧪", new[] { "Koan.Test." });

        KoanPillarCatalog.RegisterDescriptor(descriptor);

        KoanPillarCatalog.TryGetByCode(code, out var byCode).Should().BeTrue();
        byCode!.ColorHex.Should().Be("#123456");

        KoanPillarCatalog.TryGetByLabel("Test Pillar", out var byLabel).Should().BeTrue();
        byLabel!.Code.Should().Be(code);

        KoanPillarCatalog.AssociateNamespace(code, "Koan.Test.Component");
        KoanPillarCatalog.TryMatchByModuleName("Koan.Test.Component.Service", out var byNamespace).Should().BeTrue();
        byNamespace!.Icon.Should().Be("🧪");
    }

    [Fact]
    public void RegisterDescriptor_WithConflictingMetadata_Throws()
    {
        var code = $"pillar-{Guid.NewGuid():N}";
        var descriptor = new KoanPillarCatalog.PillarDescriptor(code, "Primary", "#abcdef", "🧪");
        KoanPillarCatalog.RegisterDescriptor(descriptor);

        var conflicting = new KoanPillarCatalog.PillarDescriptor(code, "Secondary", "#000000", "❌");

        var act = () => KoanPillarCatalog.RegisterDescriptor(conflicting);
        act.Should().Throw<InvalidOperationException>().WithMessage("*already registered*");
    }

    /// <summary>
    /// Provenance needs something to show a module under when no manifest has declared its pillar, so it
    /// describes one. It must not register that description.
    ///
    /// <para>This is the defect behind three investigations of an "intermittent" SQLite suite. Provenance
    /// registered an invented <c>data</c> pillar with a placeholder colour and icon; when
    /// <c>Koan.Data.Core</c> declared the real one the catalog refused it as a conflicting registration, its
    /// module threw during register, and every host boot in that process failed from there — 48 of 49 specs in
    /// one run. Whether it happened depended on which module reported provenance first, so it looked like
    /// flakiness and passed on re-run.</para>
    ///
    /// <para>The catalog's refusal was correct; writing a guess into it was not. This pins the cause.</para>
    /// </summary>
    [Fact]
    public void Provenance_for_an_undeclared_pillar_does_not_register_it()
    {
        var code = $"pillar-{Guid.NewGuid():N}";

        ProvenanceRegistry.Instance.GetOrCreateModule(code, $"Ledger.{code}");

        KoanPillarCatalog.IsRegistered(code).Should().BeFalse(
            "describing a pillar is not declaring it, and only a manifest declares");
    }

    [Fact]
    public void AssociateNamespace_ForUnknownPillar_Throws()
    {
        var code = $"pillar-{Guid.NewGuid():N}";
        var act = () => KoanPillarCatalog.AssociateNamespace(code, "Koan.Unknown.");
        act.Should().Throw<InvalidOperationException>().WithMessage("*has not been registered*");
    }
}
