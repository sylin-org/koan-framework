using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Koan.Core.Composition;
using Xunit;

namespace Koan.Core.Tests;

/// <summary>
/// P1.1 — drift comparison cases (the card's named cases: module added / capability removed / key
/// added) plus the boot-line formatting. App + modules always compare; richer sections only when both
/// files carry them.
/// </summary>
public class KoanLockfileComparerSpec
{
    private static readonly KoanLockApp App = new("S5.Recs", "0.17", "net10.0");

    private static KoanLockfile Lock(params (string id, string ver)[] modules)
        => new(1, App, modules.Select(m => new KoanLockModule(m.id, m.ver)).ToArray());

    [Fact]
    public void No_lockfile_yields_not_found()
    {
        var cmp = KoanLockfileComparer.Compare(null, Lock(("Koan.Core", "0.17")));
        cmp.Status.Should().Be(KoanLockStatus.NoLockfile);
        cmp.Format(1).Should().Be("1 module · lockfile not found");
    }

    [Fact]
    public void Identical_composition_is_ok()
    {
        var locked = Lock(("Koan.Core", "0.17"), ("Koan.Data.Core", "0.17"));
        var resolved = Lock(("Koan.Core", "0.17"), ("Koan.Data.Core", "0.17"));
        var cmp = KoanLockfileComparer.Compare(locked, resolved);
        cmp.Status.Should().Be(KoanLockStatus.Ok);
        cmp.Format(2).Should().Be("2 modules · lockfile ok");
    }

    [Fact]
    public void Module_added_is_drift()
    {
        var locked = Lock(("Koan.Core", "0.17"));
        var resolved = Lock(("Koan.Core", "0.17"), ("Koan.Data.Connector.Postgres", "0.17"));
        var cmp = KoanLockfileComparer.Compare(locked, resolved);
        cmp.Status.Should().Be(KoanLockStatus.Drift);
        cmp.DriftKeys.Should().Contain("+Koan.Data.Connector.Postgres");
    }

    [Fact]
    public void Module_removed_is_drift()
    {
        var locked = Lock(("Koan.Core", "0.17"), ("Koan.Data.Connector.Sqlite", "0.17"));
        var resolved = Lock(("Koan.Core", "0.17"));
        var cmp = KoanLockfileComparer.Compare(locked, resolved);
        cmp.DriftKeys.Should().Contain("-Koan.Data.Connector.Sqlite");
    }

    [Fact]
    public void Module_version_change_is_drift()
    {
        var locked = Lock(("Koan.Core", "0.17"));
        var resolved = Lock(("Koan.Core", "0.18"));
        var cmp = KoanLockfileComparer.Compare(locked, resolved);
        cmp.DriftKeys.Should().Contain("Koan.Core@0.18");
    }

    [Fact]
    public void App_identity_change_is_drift()
    {
        var locked = new KoanLockfile(1, App, new[] { new KoanLockModule("Koan.Core", "0.17") });
        var resolved = new KoanLockfile(1, new KoanLockApp("S5.Recs", "0.18", "net10.0"),
            new[] { new KoanLockModule("Koan.Core", "0.17") });
        var cmp = KoanLockfileComparer.Compare(locked, resolved);
        cmp.DriftKeys.Should().Contain("app");
    }

    [Fact]
    public void Capability_owner_removed_is_drift_when_both_declare_capabilities()
    {
        var modules = new[] { new KoanLockModule("Koan.Core", "0.17") };
        IReadOnlyDictionary<string, IReadOnlyList<string>> Caps(params string[] owners)
            => owners.ToDictionary(o => o, o => (IReadOnlyList<string>)new[] { "query.linq" });

        var locked = new KoanLockfile(1, App, modules, Capabilities: Caps("data:postgres", "data:redis"));
        var resolved = new KoanLockfile(1, App, modules, Capabilities: Caps("data:postgres"));

        var cmp = KoanLockfileComparer.Compare(locked, resolved);
        cmp.DriftKeys.Should().Contain("-capability:data:redis");
    }

    [Fact]
    public void Config_key_added_is_drift_when_both_declare_keys()
    {
        var modules = new[] { new KoanLockModule("Koan.Core", "0.17") };
        var locked = new KoanLockfile(1, App, modules, ConfigKeys: new[] { "Koan:Data:Sqlite:Path" });
        var resolved = new KoanLockfile(1, App, modules,
            ConfigKeys: new[] { "Koan:Data:Sqlite:Path", "Koan:Ai:Ollama:BaseUrl" });

        var cmp = KoanLockfileComparer.Compare(locked, resolved);
        cmp.DriftKeys.Should().Contain("+configKey:Koan:Ai:Ollama:BaseUrl");
    }

    [Fact]
    public void Runtime_only_sections_do_not_drift_against_a_build_time_lockfile()
    {
        // Build-time file = app + modules only; resolved twin adds elections/keys. Those extra
        // sections must NOT register as drift (different tiers) — the boot-line stays clean.
        var buildTime = Lock(("Koan.Core", "0.17"));
        var resolved = new KoanLockfile(1, App, new[] { new KoanLockModule("Koan.Core", "0.17") },
            Elections: new Dictionary<string, KoanLockElection> { ["data:default"] = new("sqlite", "reference-priority", 10) },
            ConfigKeys: new[] { "Koan:Data:Sqlite:Path" });

        var cmp = KoanLockfileComparer.Compare(buildTime, resolved);
        cmp.Status.Should().Be(KoanLockStatus.Ok);
    }
}
