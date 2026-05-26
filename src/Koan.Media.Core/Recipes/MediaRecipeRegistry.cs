using System.Reflection;
using Koan.Media.Abstractions.Recipes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Koan.Media.Core.Recipes;

/// <summary>
/// Default <see cref="IMediaRecipeRegistry"/>. Per MEDIA-0004 §3:
/// <list type="bullet">
///   <item>Discovers <c>[MediaRecipe]</c> attribute methods in scanned assemblies</item>
///   <item>Binds <see cref="RecipesOptions"/> from <c>Koan:Media:Recipes</c></item>
///   <item>Config wins over code on name collision (single Information log line)</item>
///   <item>Reserved format-shortcut names cannot collide with recipe names — fail-fast at boot</item>
///   <item>Hot reload via <see cref="IOptionsMonitor{TOptions}"/> on the config side</item>
/// </list>
/// </summary>
public sealed class MediaRecipeRegistry : IMediaRecipeRegistry, IDisposable
{
    /// <summary>Reserved format shortcut names — recipes cannot use them.</summary>
    public static readonly IReadOnlyList<string> ReservedFormatShortcuts =
        new[] { "jpeg", "jpg", "png", "webp", "gif", "bmp", "tiff", "avif" };

    private readonly Dictionary<string, MediaRecipe> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyDictionary<string, MediaRecipe> _codeRecipes;
    private readonly IOptionsMonitor<RecipesOptions>? _monitor;
    private readonly ILogger<MediaRecipeRegistry> _logger;
    private readonly IDisposable? _changeSubscription;
    private readonly object _gate = new();

    public MediaRecipeRegistry(
        IEnumerable<Assembly> scanAssemblies,
        IOptionsMonitor<RecipesOptions>? options,
        ILogger<MediaRecipeRegistry>? logger = null)
    {
        _logger = logger ?? NullLogger<MediaRecipeRegistry>.Instance;
        _codeRecipes = DiscoverCodeRecipes(scanAssemblies);
        _monitor = options;

        Rebuild(options?.CurrentValue);

        if (options is not null)
        {
            _changeSubscription = options.OnChange(opts => Rebuild(opts));
        }
    }

    public IReadOnlyList<MediaRecipe> All
    {
        get { lock (_gate) return _byName.Values.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList(); }
    }

    public IReadOnlyList<string> FormatShortcuts => ReservedFormatShortcuts;

    public bool TryResolve(string seed, out MediaRecipe recipe)
    {
        if (string.IsNullOrWhiteSpace(seed))
        {
            recipe = null!;
            return false;
        }

        // 1) Named recipe
        var named = Find(seed);
        if (named is not null)
        {
            recipe = named;
            return true;
        }

        // 2) Format shortcut — synthesise an EncodeAs recipe with sensible mutators
        var lower = seed.Trim().ToLowerInvariant();
        if (ReservedFormatShortcuts.Contains(lower, StringComparer.OrdinalIgnoreCase))
        {
            var canonical = lower switch { "jpg" => "jpeg", _ => lower };
            recipe = MediaRecipe.New()
                .WithName(canonical)
                .WithDescription($"Format shortcut: re-encode source as {canonical} at Quality.Web (q={Quality.Web}).")
                .EncodeAs(canonical, Quality.Web)
                .Mutators(MutatorKind.Common | MutatorKind.Strip)
                .Build() with { Source = RecipeSource.AdHoc };
            return true;
        }

        recipe = null!;
        return false;
    }

    public MediaRecipe? Find(string name)
    {
        lock (_gate) return _byName.TryGetValue(name, out var r) ? r : null;
    }

    public void Dispose() => _changeSubscription?.Dispose();

    private void Rebuild(RecipesOptions? options)
    {
        var merged = new Dictionary<string, MediaRecipe>(StringComparer.OrdinalIgnoreCase);

        // Code recipes first
        foreach (var (key, recipe) in _codeRecipes)
        {
            EnsureNotReserved(key);
            merged[key] = recipe;
        }

        // Config overrides — log once per override
        if (options?.Recipes is { Count: > 0 } configRecipes)
        {
            foreach (var (key, configured) in configRecipes)
            {
                EnsureNotReserved(key);
                var isOverride = merged.ContainsKey(key);
                MediaRecipe bound;
                try
                {
                    bound = ConfiguredRecipeBinder.Bind(
                        key,
                        configured,
                        isOverride ? RecipeSource.ConfigOverride : RecipeSource.Config);
                }
                catch (MediaRecipeBindingException ex)
                {
                    // Fail-fast at boot per MEDIA-0004 §3
                    throw new MediaRecipeBindingException(
                        $"Boot-time recipe validation failed at Koan:Media:Recipes:{key}. {ex.Message}", ex);
                }

                if (isOverride)
                {
                    _logger.LogInformation(
                        "Media recipe '{Name}' overridden by Koan:Media:Recipes config (was: code).",
                        key);
                }
                merged[key] = bound;
            }
        }

        lock (_gate)
        {
            _byName.Clear();
            foreach (var (k, v) in merged) _byName[k] = v;
        }
    }

    private static IReadOnlyDictionary<string, MediaRecipe> DiscoverCodeRecipes(IEnumerable<Assembly> assemblies)
    {
        var result = new Dictionary<string, MediaRecipe>(StringComparer.OrdinalIgnoreCase);
        foreach (var asm in assemblies.Distinct())
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).ToArray()!; }

            foreach (var type in types)
            {
                MethodInfo[] methods;
                try { methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static); }
                catch { continue; }

                foreach (var m in methods)
                {
                    var attr = m.GetCustomAttribute<MediaRecipeAttribute>();
                    if (attr is null) continue;
                    if (!typeof(MediaRecipe).IsAssignableFrom(m.ReturnType) &&
                        !typeof(MediaRecipeBuilder).IsAssignableFrom(m.ReturnType))
                    {
                        throw new MediaRecipeBindingException(
                            $"[MediaRecipe] method '{type.FullName}.{m.Name}' must return MediaRecipe (got {m.ReturnType.Name}).");
                    }
                    if (m.GetParameters().Length != 0)
                    {
                        throw new MediaRecipeBindingException(
                            $"[MediaRecipe] method '{type.FullName}.{m.Name}' must be parameterless.");
                    }

                    object? produced;
                    try { produced = m.Invoke(null, null); }
                    catch (Exception ex)
                    {
                        throw new MediaRecipeBindingException(
                            $"[MediaRecipe] method '{type.FullName}.{m.Name}' threw during invocation.", ex);
                    }

                    var recipe = produced switch
                    {
                        MediaRecipeBuilder b => b.Build(),
                        MediaRecipe r => r,
                        _ => throw new MediaRecipeBindingException(
                            $"[MediaRecipe] method '{type.FullName}.{m.Name}' returned null."),
                    };

                    EnsureNotReserved(attr.Name);
                    recipe = recipe with
                    {
                        Name = attr.Name,
                        Description = attr.Description ?? recipe.Description,
                        Version = attr.Version,
                        AllowedMutators = attr.Mutators,
                        Eager = attr.Eager,
                        Source = RecipeSource.Code,
                    };

                    if (result.ContainsKey(attr.Name))
                    {
                        throw new MediaRecipeBindingException(
                            $"[MediaRecipe] duplicate name '{attr.Name}' declared by '{type.FullName}.{m.Name}'. " +
                            $"Recipe names must be unique across the application.");
                    }
                    result[attr.Name] = recipe;
                }
            }
        }
        return result;
    }

    private static void EnsureNotReserved(string name)
    {
        if (ReservedFormatShortcuts.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            throw new MediaRecipeBindingException(
                $"Recipe name '{name}' collides with a reserved format shortcut " +
                $"({string.Join(", ", ReservedFormatShortcuts)}). Rename the recipe.");
        }
    }
}
