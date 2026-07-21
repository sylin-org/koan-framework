using System.IO;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Composition;
using Koan.Core.Providers;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Core.Routing;
using Koan.Data.Core;

namespace Koan.Tests.Data.Core.Specs.Routing;

public sealed class DataProviderCatalogSpec
{
    [Fact]
    public void Direct_connector_intent_beats_a_higher_priority_bundle_floor()
    {
        var floor = new HighFactory("floor", floor: true);
        var direct = new LowFactory(
            "direct",
            references: ["Koan.Data.Connector.Direct", "Sylin.Koan.Data.Connector.Direct"]);
        var catalog = new DataProviderCatalog(
            [floor, direct],
            Manifest("package", "Sylin.Koan.Data.Connector.Direct"));

        var selected = catalog.SelectAutomatic();

        selected.Factory.Should().BeSameAs(direct);
        selected.Via.Should().Be("direct-reference-intent");
        selected.DirectIntent.Should().BeTrue();
    }

    [Fact]
    public void Transitive_factory_presence_does_not_displace_the_automatic_floor()
    {
        var floor = new LowFactory("floor", floor: true);
        var transitive = new HighFactory(
            "transitive",
            references: ["Koan.Data.Connector.Transitive", "Sylin.Koan.Data.Connector.Transitive"]);
        var catalog = new DataProviderCatalog(
            [transitive, floor],
            Manifest("package", "Sylin.Koan"));

        var selected = catalog.SelectAutomatic();

        selected.Factory.Should().BeSameAs(floor);
        selected.Via.Should().Be("built-in-floor");
        selected.DirectIntent.Should().BeFalse();
    }

    [Fact]
    public void Unknown_build_provenance_uses_a_deterministic_degraded_priority_fallback()
    {
        var low = new LowFactory("low");
        var high = new HighFactory("high");
        var catalog = new DataProviderCatalog([low, high], UnknownManifest());

        var selected = catalog.SelectAutomatic();

        selected.Factory.Should().BeSameAs(high);
        selected.Via.Should().Be("unknown-provenance-priority");
    }

    [Fact]
    public void Exact_provider_lookup_honors_aliases_and_never_substitutes_an_unrelated_factory()
    {
        var mongo = new LowFactory("mongo", aliases: ["mongodb"]);
        var json = new HighFactory("json", floor: true);
        var catalog = new DataProviderCatalog([json, mongo], Manifest("package", "Sylin.Koan"));

        catalog.Find("mongodb").Should().BeSameAs(mongo);
        catalog.Find("missing").Should().BeNull();
    }

    [Fact]
    public void Duplicate_aliases_reject_before_a_provider_can_be_selected()
    {
        var first = new LowFactory("first", aliases: ["shared"]);
        var second = new HighFactory("second", aliases: ["SHARED"]);

        var act = () => new DataProviderCatalog([second, first], UnknownManifest());

        act.Should().Throw<InvalidOperationException>().WithMessage("*provider alias 'shared'*");
    }

    [Fact]
    public void Automatic_selection_is_compiled_once_and_reuses_the_same_receipt()
    {
        var catalog = new DataProviderCatalog(
            [new LowFactory("json", floor: true)],
            Manifest("package", "Sylin.Koan"));

        catalog.SelectAutomatic().Should().BeSameAs(catalog.SelectAutomatic());
        catalog.SelectAutomatic().Receipt.Should().BeSameAs(catalog.SelectAutomatic().Receipt);
    }

    [Fact]
    public void Configured_default_plan_is_required_and_is_the_canonical_host_decision()
    {
        var mongo = new LowFactory("mongo", aliases: ["mongodb"]);
        var catalog = new DataProviderCatalog([mongo], UnknownManifest());
        var sources = new DataSourceRegistry();
        sources.RegisterSource(new DataSourceRegistry.SourceDefinition(
            "Default",
            "mongodb",
            "",
            new Dictionary<string, string>()));

        var plan = new DataDefaultProviderPlan(catalog, sources);

        plan.Decision.Adapter.Should().Be("mongo");
        plan.Decision.Receipt.Intent.Should().Be(ProviderIntentPosture.Required);
        plan.Decision.Receipt.Subject.Should().Be("data:default");
        plan.Decision.Should().BeSameAs(plan.Decision);
    }

    private static KoanApplicationReferenceManifest Manifest(string kind, string rawIdentity)
    {
        var canonical = rawIdentity.StartsWith("Sylin.", StringComparison.Ordinal)
            ? rawIdentity
            : $"Sylin.{rawIdentity}";
        return KoanApplicationReferenceManifest.Parse(new StringReader(
            $"schema|1{Environment.NewLine}reference|{kind}|{rawIdentity}|{canonical}"));
    }

    private static KoanApplicationReferenceManifest UnknownManifest() =>
        KoanApplicationReferenceManifest.Load(applicationAssembly: null);

    private abstract class FakeFactory(
        string provider,
        bool floor = false,
        IReadOnlyList<string>? aliases = null,
        IReadOnlyList<string>? references = null) : IDataAdapterFactory
    {
        public string Provider { get; } = provider;
        public IReadOnlyCollection<string> Aliases { get; } = aliases ?? [];
        public IReadOnlyCollection<string> ReferenceIdentities { get; } = references ?? [];
        public bool IsAutomaticFloor { get; } = floor;

        public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp, string source = "Default")
            where TEntity : class, IEntity<TKey>
            where TKey : notnull => throw new NotSupportedException();

        public StorageNamingCapability GetNamingCapability(IServiceProvider services) => new();
    }

    [ProviderPriority(100)]
    private sealed class HighFactory(
        string provider,
        bool floor = false,
        IReadOnlyList<string>? aliases = null,
        IReadOnlyList<string>? references = null)
        : FakeFactory(provider, floor, aliases, references);

    [ProviderPriority(-100)]
    private sealed class LowFactory(
        string provider,
        bool floor = false,
        IReadOnlyList<string>? aliases = null,
        IReadOnlyList<string>? references = null)
        : FakeFactory(provider, floor, aliases, references);
}
