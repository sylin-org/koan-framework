using System.Collections.Generic;
using System.Reflection;
using AwesomeAssertions;
using Koan.Core.Hosting.Bootstrap;
using Xunit;

namespace Koan.Tests.Integration.Bootstrap.Specs;

/// <summary>
/// X-aot-substrate. Under a single-file publish, Reference=Intent connectors leave no loose Koan.*.dll to scan
/// and aren't in the app's compiled metadata, so boot loads them from an embedded <c>koan.modules.manifest</c>
/// (emitted by the Koan build targets). This pins the read/parse/load contract: only <c>Koan.*</c> lines are
/// loaded, blanks and non-Koan lines are skipped, and each module is handed to the assembly accumulator. The
/// manifest is planted as an embedded resource on THIS test assembly (see the .csproj) and read via the
/// injectable source overload — the real path uses <see cref="Assembly.GetEntryAssembly"/>.
/// </summary>
public sealed class EmbeddedModuleManifestSpec
{
    [Fact]
    public void Loads_only_Koan_modules_named_in_the_embedded_manifest()
    {
        var loaded = new List<string>();
        bool Spy(Assembly asm, bool _)
        {
            loaded.Add(asm.GetName().Name ?? "");
            return true;
        }

        var skips = AppBootstrapper.LoadIntentModulesFromManifest(Spy, typeof(EmbeddedModuleManifestSpec).Assembly);

        skips.Should().Be(1, "the deliberately unavailable Koan module is reported without failing the manifest pass");
        loaded.Should().ContainSingle().Which.Should().Be("Koan.Core",
            "only referenced, loadable Koan modules are handed to the accumulator");
        loaded.Should().NotContain("System.Text.Json",
            "non-Koan lines are filtered out (the manifest scope mirrors the legacy Koan.*.dll scan)");
    }

    [Fact]
    public void Missing_manifest_resource_is_a_no_op()
    {
        var loaded = new List<string>();
        // Koan.Core has no embedded koan.modules.manifest, so the read yields nothing without throwing.
        var skips = AppBootstrapper.LoadIntentModulesFromManifest((a, _) => { loaded.Add(a.GetName().Name ?? ""); return true; },
            typeof(AppBootstrapper).Assembly);

        skips.Should().Be(0);
        loaded.Should().BeEmpty("a consumer that didn't import the Koan build targets simply has no manifest");
    }
}
