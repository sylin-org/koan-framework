using System;
using Koan.Core.Modules.Pillars;
using FluentAssertions;
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

    [Fact]
    public void AssociateNamespace_ForUnknownPillar_Throws()
    {
        var code = $"pillar-{Guid.NewGuid():N}";
        var act = () => KoanPillarCatalog.AssociateNamespace(code, "Koan.Unknown.");
        act.Should().Throw<InvalidOperationException>().WithMessage("*has not been registered*");
    }
}
