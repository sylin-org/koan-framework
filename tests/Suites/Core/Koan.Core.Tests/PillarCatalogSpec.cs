using System;
using Koan.Core.Modules.Pillars;
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
    /// A pillar no manifest has declared gets a placeholder so a module reporting under it still has something
    /// to be shown under. The declaration replaces the placeholder whenever it arrives, in either order.
    ///
    /// <para>This is the defect behind three investigations of an "intermittent" SQLite suite. Provenance would
    /// infer <c>data</c> with a placeholder colour and icon before <c>Koan.Data.Core</c> declared the real one;
    /// the real registration was then refused as a conflicting one, its module threw during register, and every
    /// host boot in that process failed from there — 48 of 49 specs in one run. Whether it happened at all
    /// depended on which module reported provenance first, so it looked like flakiness and passed on re-run.</para>
    /// </summary>
    [Fact]
    public void An_inferred_pillar_gives_way_to_the_manifest_that_declares_it()
    {
        var code = $"pillar-{Guid.NewGuid():N}";
        var label = $"Ledger {code}";
        KoanPillarCatalog.RegisterInferredDescriptor(
            new KoanPillarCatalog.PillarDescriptor(code, label, "#2563eb", "📦", new[] { "Koan.Ledger." }));

        KoanPillarCatalog.RegisterDescriptor(
            new KoanPillarCatalog.PillarDescriptor(code, label, "#38bdf8", "🗄️"));

        KoanPillarCatalog.TryGetByCode(code, out var stored).Should().BeTrue();
        stored!.ColorHex.Should().Be("#38bdf8");
        stored.Icon.Should().Be("🗄️");
        stored.NamespacePrefixes.Should().Contain(
            "Koan.Ledger.", "namespaces the placeholder collected still resolve to the pillar");
    }

    [Fact]
    public void A_manifest_is_not_displaced_by_a_guess_that_arrives_after_it()
    {
        var code = $"pillar-{Guid.NewGuid():N}";
        var label = $"Ledger {code}";
        KoanPillarCatalog.RegisterDescriptor(
            new KoanPillarCatalog.PillarDescriptor(code, label, "#38bdf8", "🗄️"));

        KoanPillarCatalog.RegisterInferredDescriptor(
            new KoanPillarCatalog.PillarDescriptor(code, label, "#2563eb", "📦"));

        KoanPillarCatalog.TryGetByCode(code, out var stored).Should().BeTrue();
        stored!.Icon.Should().Be("🗄️", "a guess never outranks the module that owns the pillar");
    }

    [Fact]
    public void AssociateNamespace_ForUnknownPillar_Throws()
    {
        var code = $"pillar-{Guid.NewGuid():N}";
        var act = () => KoanPillarCatalog.AssociateNamespace(code, "Koan.Unknown.");
        act.Should().Throw<InvalidOperationException>().WithMessage("*has not been registered*");
    }
}
