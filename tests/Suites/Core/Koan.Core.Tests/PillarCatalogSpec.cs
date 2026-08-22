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

    /// <summary>
    /// A namespace root has to claim two spellings, because matching is longest-prefix <c>StartsWith</c>:
    /// <c>Koan.Data.</c> claims everything beneath it, and <c>Koan.Data</c> claims the assembly carrying the
    /// root's own name. Six manifests each authored both by hand and nothing checked either.
    /// </summary>
    [Fact]
    public void A_manifest_claims_both_its_namespace_and_the_assembly_of_the_same_name()
    {
        var code = $"pillar-{Guid.NewGuid():N}";
        var root = $"Koan.Ledger{Guid.NewGuid():N}";
        var manifest = new PillarManifest(code, $"Ledger {code}", "#123456", "📒", root);

        manifest.EnsureRegistered();

        manifest.Descriptor.ColorHex.Should().Be("#123456");
        manifest.Descriptor.Icon.Should().Be("📒");

        KoanPillarCatalog.TryMatchByModuleName($"{root}.Core", out var nested).Should().BeTrue();
        nested!.Code.Should().Be(code);
        KoanPillarCatalog.TryMatchByModuleName(root, out var exact).Should().BeTrue("the assembly named for the root belongs to the pillar too");
        exact!.Code.Should().Be(code);
    }

    /// <summary>
    /// Where one pillar's root sits inside another's, the longer root owns what is beneath it. This is the
    /// live arrangement between <c>Koan.Web</c> and <c>Koan.Web.Auth</c>, and it is what makes the second,
    /// dotted spelling every manifest used to register unnecessary.
    /// </summary>
    [Fact]
    public void The_longer_root_owns_what_sits_beneath_it()
    {
        var outer = $"pillar-{Guid.NewGuid():N}";
        var inner = $"pillar-{Guid.NewGuid():N}";
        var root = $"Koan.Ledger{Guid.NewGuid():N}";

        new PillarManifest(outer, $"Outer {outer}", "#111111", "O", root).EnsureRegistered();
        new PillarManifest(inner, $"Inner {inner}", "#222222", "I", root + ".Auth").EnsureRegistered();

        KoanPillarCatalog.TryMatchByModuleName($"{root}.Auth.Server", out var beneath).Should().BeTrue();
        beneath!.Code.Should().Be(inner);
        KoanPillarCatalog.TryMatchByModuleName($"{root}.Core", out var elsewhere).Should().BeTrue();
        elsewhere!.Code.Should().Be(outer);
    }

    [Fact]
    public void The_core_pillar_declares_itself_and_claims_its_namespace()
    {
        CorePillarManifest.EnsureRegistered();

        var descriptor = CorePillarManifest.Descriptor;
        descriptor.Code.Should().Be("core");
        descriptor.Label.Should().Be("Core");
        descriptor.ColorHex.Should().Be("#64748b");
        descriptor.Icon.Should().Be("⚙️");

        KoanPillarCatalog.TryMatchByModuleName("Koan.Core.Hosting", out var matched).Should().BeTrue();
        matched!.Code.Should().Be("core");
    }

    [Fact]
    public void AssociateNamespace_ForUnknownPillar_Throws()
    {
        var code = $"pillar-{Guid.NewGuid():N}";
        var act = () => KoanPillarCatalog.AssociateNamespace(code, "Koan.Unknown.");
        act.Should().Throw<InvalidOperationException>().WithMessage("*has not been registered*");
    }
}
