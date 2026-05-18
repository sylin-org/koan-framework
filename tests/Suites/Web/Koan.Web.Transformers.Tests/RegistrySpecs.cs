using FluentAssertions;
using Koan.Web.Transformers.Tests.Fixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Web.Transformers.Tests;

/// <summary>
/// Unit-level coverage of <see cref="ITransformerRegistry"/>: predicate-driven pipeline activation,
/// composition with the terminal stage, Accept negotiation tie-breaks, and the wildcard-only
/// fallthrough that preserves anonymous JSON cacheability.
/// </summary>
public sealed class RegistrySpecs
{
    private static (ITransformerRegistry Registry, IServiceProvider Sp) BuildRegistry(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        register(services);
        var sp = services.BuildServiceProvider();
        return (sp.GetRequiredService<ITransformerRegistry>(), sp);
    }

    private static HttpContext Context(params (string Name, string Value)[] headers)
    {
        var ctx = new DefaultHttpContext();
        foreach (var (n, v) in headers)
        {
            ctx.Request.Headers[n] = v;
        }
        return ctx;
    }

    // === Pipeline activation ===

    [Fact]
    public void Enricher_with_no_predicate_is_always_in_the_pipeline()
    {
        var (reg, _) = BuildRegistry(s => s.AddEntityEnricher<Widget, AlwaysEnricher>());

        var selection = reg.ResolveOutput(typeof(Widget), new[] { "*/*" }, Context());

        selection.Pipeline.Should().HaveCount(1);
        selection.Terminal.Should().BeNull();
    }

    [Fact]
    public void Enricher_with_predicate_excluded_when_predicate_false()
    {
        var (reg, _) = BuildRegistry(s => s.AddEntityEnricher<Widget, HeaderGatedEnricher>());

        var selection = reg.ResolveOutput(typeof(Widget), new[] { "*/*" }, Context());

        selection.Pipeline.Should().BeEmpty();
        selection.HasAny.Should().BeFalse();
    }

    [Fact]
    public void Enricher_with_predicate_included_when_predicate_true()
    {
        var (reg, _) = BuildRegistry(s => s.AddEntityEnricher<Widget, HeaderGatedEnricher>());

        var selection = reg.ResolveOutput(typeof(Widget), new[] { "*/*" }, Context(("X-Activate-Enrich", "true")));

        selection.Pipeline.Should().HaveCount(1);
    }

    [Fact]
    public void Multiple_enrichers_stack_when_all_predicates_pass()
    {
        var (reg, _) = BuildRegistry(s =>
        {
            s.AddEntityEnricher<Widget, HeaderGatedEnricher>();
            s.AddEntityEnricher<Widget, AdminTagEnricher>();
        });

        var selection = reg.ResolveOutput(
            typeof(Widget),
            new[] { "*/*" },
            Context(("X-Activate-Enrich", "true"), ("X-Activate-Admin", "true")));

        selection.Pipeline.Should().HaveCount(2);
    }

    [Fact]
    public void Mutually_exclusive_predicates_select_only_the_matching_enricher()
    {
        var (reg, _) = BuildRegistry(s =>
        {
            s.AddEntityEnricher<Widget, HeaderGatedEnricher>();
            s.AddEntityEnricher<Widget, AdminTagEnricher>();
        });

        var selection = reg.ResolveOutput(
            typeof(Widget),
            new[] { "*/*" },
            Context(("X-Activate-Admin", "true")));

        selection.Pipeline.Should().HaveCount(1);
    }

    // === Terminal negotiation ===

    [Fact]
    public void Terminal_is_selected_when_explicit_accept_matches()
    {
        var (reg, _) = BuildRegistry(s => s.AddEntityTransformer<Widget, string, WidgetCsvTransformer>("text/csv"));

        var selection = reg.ResolveOutput(typeof(Widget), new[] { "text/csv" }, Context());

        selection.Terminal.Should().NotBeNull();
        selection.Terminal!.ContentType.Should().Be("text/csv");
    }

    [Fact]
    public void Terminal_is_not_selected_for_wildcard_only_accept()
    {
        // This is the load-bearing rule: anon JSON requests don't get hijacked by Terminal
        // transformers just because one is registered.
        var (reg, _) = BuildRegistry(s => s.AddEntityTransformer<Widget, string, WidgetCsvTransformer>("text/csv"));

        var selection = reg.ResolveOutput(typeof(Widget), new[] { "*/*" }, Context());

        selection.Terminal.Should().BeNull();
        selection.HasAny.Should().BeFalse();
    }

    [Fact]
    public void Pipeline_and_terminal_compose_when_both_match()
    {
        // Authed user requesting CSV: enricher runs first, then CSV serialization.
        var (reg, _) = BuildRegistry(s =>
        {
            s.AddEntityEnricher<Widget, HeaderGatedEnricher>();
            s.AddEntityTransformer<Widget, string, WidgetCsvTransformer>("text/csv");
        });

        var selection = reg.ResolveOutput(
            typeof(Widget),
            new[] { "text/csv" },
            Context(("X-Activate-Enrich", "true")));

        selection.Pipeline.Should().HaveCount(1);
        selection.Terminal.Should().NotBeNull();
    }

    [Fact]
    public void Pipeline_runs_alone_when_terminal_does_not_match()
    {
        var (reg, _) = BuildRegistry(s =>
        {
            s.AddEntityEnricher<Widget, AlwaysEnricher>();
            s.AddEntityTransformer<Widget, string, WidgetCsvTransformer>("text/csv");
        });

        var selection = reg.ResolveOutput(typeof(Widget), new[] { "*/*" }, Context());

        selection.Pipeline.Should().HaveCount(1);
        selection.Terminal.Should().BeNull();
    }

    [Fact]
    public void Registry_returns_no_selection_for_entities_with_no_registrations()
    {
        // Register an enricher for a different entity, then ask the registry about Widget. Proves
        // the registry doesn't cross-contaminate entity types and exercises the EnsureInitialized
        // path without requiring the test project to reach the internal registry type directly.
        var (reg, _) = BuildRegistry(s => s.AddEntityEnricher<UnrelatedEntity, UnrelatedEnricher>());

        var selection = reg.ResolveOutput(typeof(Widget), new[] { "application/json" }, Context());

        selection.HasAny.Should().BeFalse();
        selection.Pipeline.Should().BeEmpty();
        selection.Terminal.Should().BeNull();
    }

    public sealed record UnrelatedEntity(string Id);

    public sealed class UnrelatedEnricher : IEntityEnricher<UnrelatedEntity>
    {
        public Task<UnrelatedEntity> Enrich(UnrelatedEntity model, HttpContext context) => Task.FromResult(model);
        public Task<IReadOnlyList<UnrelatedEntity>> EnrichMany(IReadOnlyList<UnrelatedEntity> models, HttpContext context) => Task.FromResult(models);
    }

    // === GetContentTypes (OpenAPI surface) ===

    [Fact]
    public void Get_content_types_lists_terminal_registrations_only()
    {
        var (reg, _) = BuildRegistry(s =>
        {
            s.AddEntityEnricher<Widget, AlwaysEnricher>();
            s.AddEntityTransformer<Widget, string, WidgetCsvTransformer>("text/csv");
        });

        // Force initialization by hitting any resolve path; GetContentTypes also triggers it.
        reg.GetContentTypes(typeof(Widget)).Should().BeEquivalentTo(new[] { "text/csv" });
    }

    // === Input resolution ===

    [Fact]
    public void Input_resolution_finds_terminal_by_content_type()
    {
        var (reg, _) = BuildRegistry(s => s.AddEntityTransformer<Widget, string, WidgetCsvTransformer>("text/csv"));

        var selection = reg.ResolveForInput(typeof(Widget), "text/csv", Context());

        selection.Should().NotBeNull();
        selection!.ContentType.Should().Be("text/csv");
    }

    [Fact]
    public void Input_resolution_ignores_enrichers()
    {
        var (reg, _) = BuildRegistry(s => s.AddEntityEnricher<Widget, AlwaysEnricher>());

        var selection = reg.ResolveForInput(typeof(Widget), "application/json", Context());

        selection.Should().BeNull();
    }
}
