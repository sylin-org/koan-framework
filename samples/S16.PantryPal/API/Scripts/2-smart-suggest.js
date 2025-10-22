/**
 * Smart Meal Suggestion with Waste Reduction
 *
 * Intelligent suggestion that prioritizes items expiring soon.
 * Demonstrates: Date filtering, complex scoring, conditional logic
 */

const now = new Date();
const oneWeekFromNow = new Date(now.getTime() + 7 * 24 * 60 * 60 * 1000);

// Get pantry items, highlighting those expiring soon
const pantry = SDK.Entities.PantryItem.collection({
  filter: { status: 'available' }
});

const expiringSoon = [];
const normalItems = [];

pantry.items.forEach(item => {
  if (item.expiresAt) {
    const expiresDate = new Date(item.expiresAt);
    if (expiresDate <= oneWeekFromNow) {
      expiringSoon.push(item);
    } else {
      normalItems.push(item);
    }
  } else {
    normalItems.push(item);
  }
});

// Get recipes
const recipes = SDK.Entities.Recipe.collection({ pageSize: 50 });

// Score recipes: bonus points for using expiring ingredients
let bestMatch = null;
let bestScore = 0;

recipes.items.forEach(recipe => {
  let availableCount = 0;
  let expiringUsed = 0;

  recipe.ingredients.forEach(ingredient => {
    // Check if we have this ingredient
    const hasNormal = normalItems.some(item =>
      item.name.toLowerCase().includes(ingredient.name.toLowerCase()) ||
      ingredient.name.toLowerCase().includes(item.name.toLowerCase())
    );

    const hasExpiring = expiringSoon.some(item =>
      item.name.toLowerCase().includes(ingredient.name.toLowerCase()) ||
      ingredient.name.toLowerCase().includes(item.name.toLowerCase())
    );

    if (hasNormal || hasExpiring) {
      availableCount++;
    }

    if (hasExpiring) {
      expiringUsed++;
    }
  });

  // Base score: ingredient availability
  const availabilityScore = recipe.ingredients.length > 0
    ? availableCount / recipe.ingredients.length
    : 0;

  // Bonus for using expiring items (up to 50% boost)
  const wasteReductionBonus = expiringUsed * 0.1;

  // Rating factor
  const ratingFactor = recipe.averageRating / 5;

  const totalScore = (availabilityScore * 0.6) + (wasteReductionBonus * 0.3) + (ratingFactor * 0.1);

  if (totalScore > bestScore) {
    bestScore = totalScore;
    bestMatch = recipe;
  }
});

if (bestMatch) {
  let message = `🍽️  Smart Suggestion: ${bestMatch.name}

📊 Ingredient match: ${Math.round((bestMatch.ingredients.length > 0 ? (bestMatch.ingredients.filter(ing =>
    pantry.items.some(item =>
      item.name.toLowerCase().includes(ing.name.toLowerCase()) ||
      ing.name.toLowerCase().includes(item.name.toLowerCase())
    )).length / bestMatch.ingredients.length : 0) * 100)}%
⏱️  Cook time: ${bestMatch.totalTimeMinutes} minutes
⭐ Rating: ${bestMatch.averageRating}/5`;

  if (expiringSoon.length > 0) {
    message += `\n\n⚠️  This recipe helps use ${expiringSoon.length} item(s) expiring soon:`;
    expiringSoon.slice(0, 3).forEach(item => {
      const daysLeft = Math.ceil((new Date(item.expiresAt).getTime() - now.getTime()) / (24 * 60 * 60 * 1000));
      message += `\n  • ${item.name} (${daysLeft} days left)`;
    });
  }

  SDK.Out.answer(message);
} else {
  SDK.Out.warn("No suitable recipes found. Try adding more ingredients!");
}
