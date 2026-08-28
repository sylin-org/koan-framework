using Koan.Data.Core.Model;

namespace RecipeApi.Domain;

public sealed class Recipe : Entity<Recipe>
{
    public string Title { get; set; } = "";
    public string[] Ingredients { get; set; } = [];
    public string Instructions { get; set; } = "";
}
