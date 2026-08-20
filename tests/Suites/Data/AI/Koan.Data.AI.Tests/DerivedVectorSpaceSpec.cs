using AwesomeAssertions;
using Koan.Core;
using Koan.Data.AI.Attributes;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Koan.Data.AI.Tests;

/// <summary>
/// An Entity that declares its embedding model and width has already said everything a vector space needs.
/// Requiring the application to restate it in a composition callback is ceremony, and it cost the SnapVault
/// sample its bare <c>AddKoan()</c> — the canonical grammar — for no information gain.
///
/// <para>Proved through the application-facing surface rather than by inspecting the plan, because the property
/// that matters is that a vector Entity <i>works</i> from a bare boot.</para>
/// </summary>
public sealed class DerivedVectorSpaceSpec
{
    [Embedding(Model = "test-embed", Dimensions = 4)]
    public sealed class DerivedDoc : Entity<DerivedDoc> { public string Title { get; set; } = ""; }

    [Embedding(Model = "test-embed", Dimensions = 4)]
    public sealed class DeclaredDoc : Entity<DeclaredDoc> { public string Title { get; set; } = ""; }

    [Embedding(Model = "test-embed")]
    public sealed class WidthlessDoc : Entity<WidthlessDoc> { public string Title { get; set; } = ""; }

    [Embedding(Model = "test-embed")]
    public sealed class GlobalWidthDoc : Entity<GlobalWidthDoc> { public string Title { get; set; } = ""; }

    [Embedding(Model = "test-embed", Dimensions = 4)]
    public sealed class LocalBeatsGlobalDoc : Entity<LocalBeatsGlobalDoc> { public string Title { get; set; } = ""; }

    [Fact(DisplayName = "an [Embedding] width lets a vector Entity work from a bare AddKoan()")]
    public async Task Width_derives_the_space()
    {
        using var host = await Boot(_ => { });

        // No koan.Data.Source(...).Vector<T>(...) anywhere: the space came from the Entity's own declaration.
        await Vector<DerivedDoc>.Save("a", new[] { 1f, 0f, 0f, 0f });
        await Vector<DerivedDoc>.Save("b", new[] { 0f, 1f, 0f, 0f });

        var hits = await Vector<DerivedDoc>.Search(new VectorQueryOptions(Query: new[] { 0.9f, 0.1f, 0f, 0f }, TopK: 2));
        hits.Matches.Select(m => m.Id).First().Should().Be("a");
    }

    [Fact(DisplayName = "an explicit declaration outranks the derived space")]
    public async Task Explicit_declaration_wins()
    {
        using var host = await Boot(koan => koan.Data.Source("Default").Vector<DeclaredDoc>(space => space
            .Name("explicit-space")
            .Dimensions(6)));

        // The derivation is a floor, never an override. Six is the declared width, so a four-wide point — the
        // width the attribute would have derived — must be rejected.
        var derivedWidth = () => Vector<DeclaredDoc>.Save("x", new[] { 1f, 0f, 0f, 0f });
        await derivedWidth.Should().ThrowAsync<Exception>("the explicit declaration owns the width");

        await Vector<DeclaredDoc>.Save("x", new[] { 1f, 0f, 0f, 0f, 0f, 0f });
    }

    [Fact(DisplayName = "a model without a width still asks, rather than guessing a number")]
    public async Task Without_a_width_the_correction_stands()
    {
        using var host = await Boot(_ => { });

        // A model name alone does not imply a width. Inventing one would fail on the first real embed, so the
        // framework keeps its corrective instead.
        var save = () => Vector<WidthlessDoc>.Save("x", new[] { 1f, 0f, 0f, 0f });

        (await save.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*has no declared space*");
    }

    [Fact(DisplayName = "Koan:Ai:Embed:Dimensions supplies the width when the Entity does not")]
    public async Task Global_layer_supplies_the_width()
    {
        using var host = await Boot(_ => { }, new() { ["Koan:Ai:Embed:Dimensions"] = "5" });

        // GlobalWidthDoc declares a model but no width, so the global layer answers.
        await Vector<GlobalWidthDoc>.Save("a", new[] { 1f, 0f, 0f, 0f, 0f });

        var tooNarrow = () => Vector<GlobalWidthDoc>.Save("b", new[] { 1f, 0f, 0f, 0f });
        await tooNarrow.Should().ThrowAsync<Exception>("five is the configured width");
    }

    [Fact(DisplayName = "the Entity's own width outranks the global default")]
    public async Task Local_outranks_global()
    {
        using var host = await Boot(_ => { }, new() { ["Koan:Ai:Embed:Dimensions"] = "5" });

        // Most local wins: the attribute says four, so four it is, global default notwithstanding.
        await Vector<LocalBeatsGlobalDoc>.Save("a", new[] { 1f, 0f, 0f, 0f });

        var globalWidth = () => Vector<LocalBeatsGlobalDoc>.Save("b", new[] { 1f, 0f, 0f, 0f, 0f });
        await globalWidth.Should().ThrowAsync<Exception>("the Entity's declaration is the most local layer");
    }

    private static async Task<IHost> Boot(Action<KoanApplicationBuilder> compose)
    {
        // In a shipping application the [Embedding] registry is source-generated. This assembly deliberately
        // does not run that generator — it also hosts fixtures that are invalid on purpose — so these three
        // entities register themselves, which is the same input the generator would supply.
        EmbeddingRegistry.RegisterTypes([
            typeof(DerivedDoc), typeof(DeclaredDoc), typeof(WidthlessDoc),
            typeof(GlobalWidthDoc), typeof(LocalBeatsGlobalDoc)]);

        return await Boot(compose, new Dictionary<string, string?>());
    }

    private static async Task<IHost> Boot(Action<KoanApplicationBuilder> compose, Dictionary<string, string?> settings)
    {
        EmbeddingRegistry.RegisterTypes([
            typeof(DerivedDoc), typeof(DeclaredDoc), typeof(WidthlessDoc),
            typeof(GlobalWidthDoc), typeof(LocalBeatsGlobalDoc)]);

        settings["Koan:Data:Sources:Default:Adapter"] = "inmemory";
        settings["Koan:Data:VectorDefaults:DefaultProvider"] = "inmemory";
        settings["Koan:Data:AI:EmbeddingWorker:Enabled"] = "false";

        var host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(settings))
            .ConfigureServices(services => services.AddKoan(compose))
            .Build();
        await host.StartAsync();
        return host;
    }
}
