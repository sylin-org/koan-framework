namespace Koan.Media.Core.Recipes;

/// <summary>
/// Options bound from <c>Koan:Media:Recipes</c>. Each value is a
/// <see cref="ConfiguredRecipe"/> describing one recipe in
/// appsettings form — same shape <c>/media/recipes/{name}</c>
/// emits, round-trippable per MEDIA-0004 §3.
/// </summary>
public sealed class RecipesOptions
{
    public const string SectionPath = "Koan:Media:Recipes";

    public Dictionary<string, ConfiguredRecipe> Recipes { get; set; } = new();
}

public sealed class ConfiguredRecipe
{
    public string? Description { get; set; }
    public int Version { get; set; } = 1;
    public List<ConfiguredStep> Steps { get; set; } = new();
    public List<string> Mutators { get; set; } = new();
    public bool Eager { get; set; }
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
}
