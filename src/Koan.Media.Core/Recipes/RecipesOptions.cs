namespace Koan.Media.Core.Recipes;

/// <summary>
/// Options bound from <c>Koan:Media:Recipes</c>. Each value is a
/// <see cref="ConfiguredRecipe"/> describing one recipe in
/// appsettings form — same shape <c>/media/recipes/{name}</c>
/// emits, round-trippable per MEDIA-0004 §3.
/// </summary>
public sealed class RecipesOptions
{
    public const string RootSectionPath = "Koan:Media";
    public const string SectionPath = RootSectionPath + ":Recipes";

    public Dictionary<string, ConfiguredRecipe> Recipes { get; set; } = new();
}

public sealed class ConfiguredRecipe
{
    public string? Description { get; set; }
    public int Version { get; set; } = 1;
    public List<ConfiguredStep> Steps { get; set; } = new();
    public List<string> Mutators { get; set; } = new();
}

/// <summary>
/// JSON-shape mirror of a <see cref="Koan.Media.Abstractions.Recipes.MediaStep"/>.
/// The <c>Op</c> discriminator maps to the step record kind; remaining
/// properties carry parameters in the canonical lowercase form.
/// </summary>
public sealed class ConfiguredStep
{
    public string Op { get; set; } = "";
    public string? Name { get; set; }
    public bool Primary { get; set; }

    // resize / shape
    public int? Width { get; set; }
    public int? Height { get; set; }
    public double Dpr { get; set; } = 1.0;
    public string? Crop { get; set; }
    public string? Aspect { get; set; }
    public string? Mode { get; set; }      // fit mode
    public string? Position { get; set; }
    public string? Bg { get; set; }

    // frame
    public int Index { get; set; }

    // rotate / flip
    public int Degrees { get; set; }
    public string? Axis { get; set; }

    // encode / flatten
    public string? Format { get; set; }
    public int Quality { get; set; } = Koan.Media.Abstractions.Recipes.Quality.Web;

    // strip
    public string? Kinds { get; set; }

    // orient
    public bool Keep { get; set; }

    // overlay
    public List<ConfiguredOverlayLayer>? Layers { get; set; }
}

/// <summary>
/// Per-layer overlay config. Mirrors the URL grammar from MEDIA-0004 §7;
/// every field is optional except <c>Source</c>.
/// </summary>
public sealed class ConfiguredOverlayLayer
{
    public ConfiguredOverlaySource Source { get; set; } = new();
    public string? Size { get; set; }
    public string? Position { get; set; }
    public string? Padding { get; set; }
    public double Opacity { get; set; } = 1.0;
    public int Rotate { get; set; }
}

/// <summary>
/// Discriminated config-side overlay source. <c>Kind</c> selects between
/// <c>media</c> (uses <see cref="MediaId"/> + optional <see cref="Recipe"/>)
/// and <c>text</c> (uses <see cref="Text"/> + font/color/size).
/// </summary>
public sealed class ConfiguredOverlaySource
{
    public string Kind { get; set; } = "media";

    // Media-source fields
    public string? MediaId { get; set; }
    public string? Recipe { get; set; }

    // Text-source fields
    public string? Text { get; set; }
    public string? Font { get; set; }
    public string? Color { get; set; }
    public int FontSize { get; set; } = 32;
}

