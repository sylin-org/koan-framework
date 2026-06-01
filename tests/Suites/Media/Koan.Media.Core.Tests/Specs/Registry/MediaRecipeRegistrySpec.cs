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
