using System.Linq;
using AwesomeAssertions;
using Koan.Core.Composition;
using Xunit;

namespace Koan.Core.Tests;

/// <summary>
/// P1.1 — schema serialization + the build-time/C# schema-consistency guard. Pins that the build
/// target's hand-written JSON (see build/Sylin.Koan.Core.targets) round-trips through the single
/// serialization authority, so the two emitters cannot silently diverge.
/// </summary>
public class KoanLockfileSpec
{
    private static readonly KoanLockApp App = new("S5.Recs", "0.17", "net10.0");

    [Fact]
    public void Round_trips_a_full_lockfile()
    {
        var original = new KoanLockfile(
            1, App,
            Modules: new[] { new KoanLockModule("Koan.Core", "0.17"), new KoanLockModule("Koan.Data.Core", "0.17") },
            Elections: new System.Collections.Generic.Dictionary<string, KoanLockElection>
            {
                ["data:default"] = new("postgres", "reference-priority", 14),
            },
            Capabilities: new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IReadOnlyList<string>>
            {
                ["data:postgres"] = new[] { "query.linq", "write.bulkUpsert" },
            },
            ConfigKeys: new[] { "Koan:Data:Postgres:ConnectionString" },
            Entities: new[] { new KoanLockEntity("Anime", new[] { "Embedding" }) });

        var json = KoanLockfileSerializer.Serialize(original);
        var back = KoanLockfileSerializer.Deserialize(json);

        back.Should().NotBeNull();
        back!.Schema.Should().Be(1);
        back.App.Should().Be(App);
        back.Modules.Should().HaveCount(2);
        back.Elections!["data:default"].Adapter.Should().Be("postgres");
        back.Capabilities!["data:postgres"].Should().Contain("write.bulkUpsert");
        back.ConfigKeys.Should().ContainSingle().Which.Should().Be("Koan:Data:Postgres:ConnectionString");
        back.Entities!.Single().Traits.Should().Contain("Embedding");
    }

    [Fact]
    public void Omits_runtime_sections_for_a_build_time_lockfile()
    {
        // The build-time file carries app + modules only; null sections must not appear.
        var buildTime = new KoanLockfile(1, App, new[] { new KoanLockModule("Koan.Core", "0.17") });

        var json = KoanLockfileSerializer.Serialize(buildTime);

        json.Should().Contain("\"schema\": 1").And.Contain("\"modules\"");
        json.Should().NotContain("elections").And.NotContain("capabilities")
            .And.NotContain("configKeys").And.NotContain("entities");
    }

    [Fact]
    public void Build_target_json_shape_round_trips_through_the_serializer()
    {
        // GOLDEN: the exact shape build/Sylin.Koan.Core.targets hand-writes. If the target's schema
        // drifts from the C# model, this deserialization breaks — the cross-emitter consistency guard.
        const string golden = """
            {
              "schema": 1,
              "app": {
                "name": "S0.ConsoleJsonRepo",
                "koan": "0.17",
                "tfm": "net10.0"
              },
              "modules": [
                {
                  "id": "Koan.Core",
                  "version": "0.17"
                },
                {
                  "id": "Koan.Data.Core",
                  "version": "0.17"
                }
              ]
            }
            """;

        var parsed = KoanLockfileSerializer.Deserialize(golden);

        parsed.Should().NotBeNull();
        parsed!.Schema.Should().Be(KoanLockfile.CurrentSchema);
        parsed.App.Should().Be(new KoanLockApp("S0.ConsoleJsonRepo", "0.17", "net10.0"));
        parsed.Modules.Select(m => m.Id).Should().Equal("Koan.Core", "Koan.Data.Core");
        parsed.Elections.Should().BeNull();
    }

    [Fact]
    public void MajorMinor_normalizes_versions_and_passes_non_versions_through()
    {
        KoanCompositionSnapshot.MajorMinor("0.17.43.0").Should().Be("0.17");
        KoanCompositionSnapshot.MajorMinor("0.17.43-g1a2b3c").Should().Be("0.17");
        KoanCompositionSnapshot.MajorMinor("1.2.3+build").Should().Be("1.2");
        KoanCompositionSnapshot.MajorMinor("unknown").Should().Be("unknown");
        KoanCompositionSnapshot.MajorMinor(null).Should().Be("unknown");
    }
}
