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

    private static async Task<IHost> Boot(Action<KoanApplicationBuilder> compose)
    {
        // In a shipping application the [Embedding] registry is source-generated. This assembly deliberately
        // does not run that generator — it also hosts fixtures that are invalid on purpose — so these three
        // entities register themselves, which is the same input the generator would supply.
        EmbeddingRegistry.RegisterTypes([typeof(DerivedDoc), typeof(DeclaredDoc), typeof(WidthlessDoc)]);

        var host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Koan:Data:Sources:Default:Adapter"] = "inmemory",
                ["Koan:Data:VectorDefaults:DefaultProvider"] = "inmemory",
                ["Koan:Data:AI:EmbeddingWorker:Enabled"] = "false"
            }))
            .ConfigureServices(services => services.AddKoan(compose))
            .Build();
        await host.StartAsync();
        return host;
    }
}
