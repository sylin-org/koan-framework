using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Koan.Media.Core.Tests.Specs.Registry;

public sealed class MediaRecipeRegistrySpec
{
    [Fact]
    public void Format_shortcuts_resolve_to_synthetic_encode_recipes()
    {
        var registry = NewRegistry();
        foreach (var f in new[] { "png", "jpeg", "webp", "gif" })
        {
            registry.TryResolve(f, out var r).Should().BeTrue($"shortcut '{f}' must resolve");
            var encode = r.Steps.OfType<EncodeStep>().Single();
            encode.Format.Should().Be(f);
        }
    }

    [Fact]
    public void Jpg_shortcut_canonicalises_to_jpeg()
    {
        var registry = NewRegistry();
        registry.TryResolve("jpg", out var r).Should().BeTrue();
        r.Steps.OfType<EncodeStep>().Single().Format.Should().Be("jpeg");
    }

    [Fact]
    public void Unknown_seed_returns_false()
    {
        var registry = NewRegistry();
        registry.TryResolve("missing", out _).Should().BeFalse();
    }

    [Fact]
    public void Unproducible_reserved_shortcut_does_not_resolve()
    {
        // avif is a reserved shortcut name, but EncoderSelector cannot produce it yet — so it must
        // NOT synthesise a recipe (which would pin avif and 500 in EncoderSelector.For). It resolves
        // as "unknown" so the controller returns an honest 404. MEDIA-0009 split-brain guard: the
        // shortcut resolver derives producibility from EncoderSelector (single source of truth).
        var registry = NewRegistry();
        registry.TryResolve("avif", out _).Should().BeFalse(
            "avif has no concrete encoder yet, so the shortcut must not resolve");
    }

    [Fact]
    public void FormatShortcuts_advertises_only_producible_formats()
    {
        var registry = NewRegistry();
        registry.FormatShortcuts.Should().Contain(new[] { "jpeg", "jpg", "png", "webp", "gif", "bmp", "tiff" });
        registry.FormatShortcuts.Should().NotContain("avif",
            "avif is declared/reserved but not producible, so it must not be advertised as a usable shortcut");
    }

    [Fact]
    public void Avif_remains_a_reserved_name_even_though_it_does_not_resolve()
    {
        // The reservation is independent of producibility: a recipe still cannot CLAIM the avif name
        // (forward-compat — so wiring the encoder later cannot collide with a host recipe), even though
        // the shortcut does not resolve today.
        var act = () => NewRegistry(
            options: new RecipesOptions
            {
                Recipes = new Dictionary<string, ConfiguredRecipe>
                {
                    ["avif"] = new ConfiguredRecipe
                    {
                        Steps = new List<ConfiguredStep>
                        {
                            new() { Op = "encodeAs", Format = "webp" },
                        },
                    },
                },
            });
        act.Should().Throw<MediaRecipeBindingException>();
    }

    [Fact]
    public void Code_recipe_attribute_is_discovered()
    {
        var registry = NewRegistry(typeof(TestCodeRecipes));
        var recipe = registry.Find("test-poster");
        recipe.Should().NotBeNull();
        recipe!.Source.Should().Be(RecipeSource.Code);
        recipe.AllowedMutators.Should().Be(MutatorKind.Common | MutatorKind.Frame);
    }

    // Note: a code-defined recipe with a reserved name is a developer
    // error caught the same way at startup; the config-path equivalent
    // is verified by Config_recipe_with_reserved_name_throws below.
    // Inlining a [MediaRecipe("png")] decorated method in the test
    // assembly would crash every assembly-wide scan, so we exercise
    // the reservation gate via the config path only.

    [Fact]
    public void Config_recipe_is_bound_at_construction()
    {
        var registry = NewRegistry(
            options: new RecipesOptions
            {
                Recipes = new Dictionary<string, ConfiguredRecipe>
                {
                    ["thumb"] = new ConfiguredRecipe
                    {
                        Description = "config-defined thumb",
                        Steps = new List<ConfiguredStep>
                        {
                            new() { Op = "resize", Width = 400 },
                            new() { Op = "encodeAs", Format = "webp", Quality = 70 },
                        },
                        Mutators = new List<string> { "common" },
                    },
                },
            });

        var recipe = registry.Find("thumb");
        recipe.Should().NotBeNull();
        recipe!.Source.Should().Be(RecipeSource.Config);
        recipe.Description.Should().Be("config-defined thumb");
        recipe.Steps.OfType<EncodeStep>().Single().Format.Should().Be("webp");
    }

    [Fact]
    public void Config_overrides_code_recipe_with_same_name()
    {
        var registry = NewRegistry(
            typeof(TestCodeRecipes),
            options: new RecipesOptions
            {
                Recipes = new Dictionary<string, ConfiguredRecipe>
                {
                    ["test-poster"] = new ConfiguredRecipe
                    {
                        Description = "ops override",
                        Steps = new List<ConfiguredStep>
                        {
                            new() { Op = "encodeAs", Format = "jpeg", Quality = 75 },
                        },
                        Mutators = new List<string> { "common" },
                    },
                },
            });

        var recipe = registry.Find("test-poster");
        recipe.Should().NotBeNull();
        recipe!.Source.Should().Be(RecipeSource.ConfigOverride);
        recipe.Description.Should().Be("ops override");
        recipe.Steps.OfType<EncodeStep>().Single().Format.Should().Be("jpeg");
    }

    [Fact]
    public void Config_recipe_with_reserved_name_throws()
    {
        var act = () => NewRegistry(
            options: new RecipesOptions
            {
                Recipes = new Dictionary<string, ConfiguredRecipe>
                {
                    ["png"] = new ConfiguredRecipe
                    {
                        Steps = new List<ConfiguredStep>
                        {
                            new() { Op = "encodeAs", Format = "jpeg" },
                        },
                    },
                },
            });
        act.Should().Throw<MediaRecipeBindingException>();
    }

    [Fact]
    public void Config_recipe_pinning_unproducible_format_fails_fast()
    {
        // A recipe that explicitly pins a non-producible format (avif before its encoder is wired)
        // must fail at boot — not 500 per-request. The binder validates EncodeAs/FlattenTo formats
        // against EncoderSelector (the single producibility authority). MEDIA-0009 residual closed.
        var act = () => NewRegistry(
            options: new RecipesOptions
            {
                Recipes = new Dictionary<string, ConfiguredRecipe>
                {
                    ["hero"] = new ConfiguredRecipe
                    {
                        Steps = new List<ConfiguredStep>
                        {
                            new() { Op = "encodeAs", Format = "avif" },
                        },
                    },
                },
            });
        act.Should().Throw<MediaRecipeBindingException>().WithMessage("*avif*not producible*");
    }

    [Fact]
    public void Config_recipe_with_jpg_alias_binds_as_jpeg()
    {
        // "jpg" is an alias for the producible "jpeg" — it must bind rather than be rejected as
        // unknown. The canonicalizer is shared with EncoderSelector (no second alias table).
        var registry = NewRegistry(
            options: new RecipesOptions
            {
                Recipes = new Dictionary<string, ConfiguredRecipe>
                {
                    ["thumb"] = new ConfiguredRecipe
                    {
                        Steps = new List<ConfiguredStep>
                        {
                            new() { Op = "encodeAs", Format = "jpg" },
                        },
                    },
                },
            });
        registry.Find("thumb").Should().NotBeNull();
    }

    [Fact]
    public void Config_recipe_with_unknown_op_fails_fast_at_boot()
    {
        var act = () => NewRegistry(
            options: new RecipesOptions
            {
                Recipes = new Dictionary<string, ConfiguredRecipe>
                {
                    ["bad"] = new ConfiguredRecipe
                    {
                        Steps = new List<ConfiguredStep>
                        {
                            new() { Op = "unknownOp" },
                        },
                    },
                },
            });
        act.Should().Throw<MediaRecipeBindingException>()
            .WithMessage("*Unknown op*unknownOp*");
    }

    [Fact]
    public void Config_recipe_with_unknown_mutator_fails_fast()
    {
        var act = () => NewRegistry(
            options: new RecipesOptions
            {
                Recipes = new Dictionary<string, ConfiguredRecipe>
                {
                    ["bad"] = new ConfiguredRecipe
                    {
                        Steps = new List<ConfiguredStep>
                        {
                            new() { Op = "encodeAs", Format = "webp" },
                        },
                        Mutators = new List<string> { "telepathy" },
                    },
                },
            });
        act.Should().Throw<MediaRecipeBindingException>()
            .WithMessage("*telepathy*");
    }

    // Duplicate-name protection on the code-attribute path is a
    // developer-error guard. Like reserved-name above, inlining two
    // [MediaRecipe("dup")]-decorated methods in this test assembly
    // would crash every assembly-wide scan, so the contract is
    // verified by inspection: MediaRecipeRegistry.DiscoverCodeRecipes
    // throws MediaRecipeBindingException when a duplicate is found.

    [Fact]
    public void Registry_All_returns_alphabetically_sorted()
    {
        var registry = NewRegistry(
            options: new RecipesOptions
            {
                Recipes = new Dictionary<string, ConfiguredRecipe>
                {
                    ["zebra"] = new() { Steps = new() { new() { Op = "encodeAs" } } },
                    ["alpha"] = new() { Steps = new() { new() { Op = "encodeAs" } } },
                    ["mango"] = new() { Steps = new() { new() { Op = "encodeAs" } } },
                },
            });
        registry.All.Select(r => r.Name).Should().Equal("alpha", "mango", "zebra");
    }

    private static MediaRecipeRegistry NewRegistry(Type? scanFrom = null, RecipesOptions? options = null)
    {
        var assemblies = scanFrom is null
            ? Array.Empty<System.Reflection.Assembly>()
            : new[] { scanFrom.Assembly };
        var monitor = options is null ? null : new ImmediateOptionsMonitor<RecipesOptions>(options);
        return new MediaRecipeRegistry(assemblies, monitor, NullLogger<MediaRecipeRegistry>.Instance);
    }
}

// ----- attribute-bearing classes scanned by the registry -----

internal static class TestCodeRecipes
{
    [MediaRecipe("test-poster",
        Description = "test poster",
        Mutators = MutatorKind.Common | MutatorKind.Frame)]
    public static MediaRecipe Poster() => MediaRecipe.New()
        .Sample(new FrameSelector.Index(0))
        .Resize(width: 800).Name("size").Primary()
        .EncodeAs("webp", Quality.Web);
}

/// <summary>
/// Minimal IOptionsMonitor for the registry tests — no hot reload, no change tokens.
/// </summary>
internal sealed class ImmediateOptionsMonitor<T> : IOptionsMonitor<T>
{
    public ImmediateOptionsMonitor(T value) { CurrentValue = value; }
    public T CurrentValue { get; }
    public T Get(string? name) => CurrentValue;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
