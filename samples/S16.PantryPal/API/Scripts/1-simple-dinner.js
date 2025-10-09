/**
 * Simple Dinner Suggestion
 *
 * Basic MCP code mode script that suggests a single recipe based on available pantry items.
 * Demonstrates: Entity collection, basic filtering, simple output
 */

// Get available pantry items
const pantry = SDK.Entities.PantryItem.collection({
  filter: { status: 'available' }
});

// Get all recipes
const recipes = SDK.Entities.Recipe.collection({ pageSize: 20 });

// Score recipes by ingredient availability
let bestMatch = null;
let bestScore = 0;

recipes.items.forEach(recipe => {
  let availableCount = 0;

  recipe.ingredients.forEach(ingredient => {
    const hasIngredient = pantry.items.some(item =>
      item.name.toLowerCase().includes(ingredient.name.toLowerCase()) ||
      ingredient.name.toLowerCase().includes(item.name.toLowerCase())
    );

    if (hasIngredient) {
      availableCount++;
    }
  });

  const score = recipe.ingredients.length > 0 ? availableCount / recipe.ingredients.length : 0;

  if (score > bestScore) {
    bestScore = score;
    bestMatch = recipe;
  }
});

if (bestMatch) {
  SDK.Out.answer(`Tonight's suggestion: ${bestMatch.name}

📊 Ingredient match: ${Math.round(bestScore * 100)}%
⏱️  Cook time: ${bestMatch.totalTimeMinutes} minutes
🔥 Difficulty: ${bestMatch.difficulty}
⭐ Rating: ${bestMatch.averageRating}/5

You have ${pantry.totalCount} items in your pantry.`);
} else {
  SDK.Out.warn("No recipes found. Try adding more ingredients to your pantry!");
}
