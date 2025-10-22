using System;
using System.Collections.Generic;
using FluentAssertions;
using Koan.Admin.Contracts;
using Koan.Core.Modules.Pillars;
using Koan.Web.Admin.Infrastructure;
using Xunit;

namespace Koan.Web.Admin.Tests.Specs;

public class ModuleStyleResolverSpec
{
    [Fact]
    public void Resolve_UsesCatalogDescriptorWhenNamespaceMatches()
    {
        var code = $"test-{Guid.NewGuid():N}";
        var descriptor = new KoanPillarCatalog.PillarDescriptor(code, "Diagnostics", "#445566", "🧭", new[] { "Koan.Diagnostics." });
        KoanPillarCatalog.RegisterDescriptor(descriptor);

        var module = new KoanAdminModuleManifest(
            "Koan.Diagnostics.Telemetry",
            "1.0.0",
            Array.Empty<KoanAdminModuleSetting>(),
            Array.Empty<string>(),
            Array.Empty<KoanAdminModuleTool>());

        var style = KoanAdminModuleStyleResolver.Resolve(module);

        style.Pillar.Should().Be("Diagnostics");
        style.ColorHex.Should().Be("#445566");
        style.Icon.Should().Be("🧭");
    }

    [Fact]
    public void Resolve_HonorsPillarNoteOverride()
    {
        var code = $"note-{Guid.NewGuid():N}";
        var descriptor = new KoanPillarCatalog.PillarDescriptor(code, "Overrides", "#778899", "🗒", new[] { "Koan.Overrides." });
        KoanPillarCatalog.RegisterDescriptor(descriptor);

        var notes = new List<string>
        {
            $"[admin-style]: pillar-code={code} pillar-label='Override Pillar' icon=🧪"
        };

        var module = new KoanAdminModuleManifest(
            "Koan.Overrides.Plugin",
            "1.0.0",
            Array.Empty<KoanAdminModuleSetting>(),
            notes,
            Array.Empty<KoanAdminModuleTool>());

        var style = KoanAdminModuleStyleResolver.Resolve(module);

        style.Pillar.Should().Be("Override Pillar");
        style.Icon.Should().Be("🧪");
        style.ColorHex.Should().Be("#778899");
    }
}
