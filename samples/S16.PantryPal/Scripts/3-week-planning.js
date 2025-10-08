/**
 * Week Meal Planning with Optimization
 *
 * Creates a 7-day meal plan optimizing for nutrition, budget, and variety.
 * Demonstrates: Multi-entity operations, aggregation, plan creation
 */

const pantry = SDK.Entities.PantryItem.collection({
  filter: { status: 'available' }
});

const recipes = SDK.Entities.Recipe.collection({ pageSize: 100 });

// Get user profile for preferences
const profiles = SDK.Entities.UserProfile.collection({ pageSize: 1 });
const profile = profiles.items[0] || {
  weeklyFoodBudget: 150,
  targetCalories: 2000,
  householdSize: 2
};

const dailyBudget = profile.weeklyFoodBudget / 7;
const daysToPlан = 7;
const selectedRecipes = [];
const usedCuisines = [];

// Select diverse recipes within budget
for (let day = 0; day < daysToPlан; day++) {
  let bestRecipe = null;
  let bestScore = -1;

  recipes.items.forEach(recipe => {
    // Skip if cuisine already used recently (encourage variety)
    const cuisineUsedRecently = recipe.cuisines.some(c =>
      usedCuisines.slice(-3).includes(c)
    );

    // Skip if already selected
    const alreadySelected = selectedRecipes.some(r => r.id === recipe.id);

    if (cuisineUsedRecently || alreadySelected) {
      return;
    }

    // Check pantry availability
    let availableCount = 0;
    recipe.ingredients.forEach(ingredient => {
      const hasIt = pantry.items.some(item =>
        item.name.toLowerCase().includes(ingredient.name.toLowerCase()) ||
        ingredient.name.toLowerCase().includes(item.name.toLowerCase())
      );
      if (hasIt) availableCount++;
    });

    const availabilityScore = recipe.ingredients.length > 0
      ? availableCount / recipe.ingredients.length
      : 0;

    // Check if within budget
    const budgetScore = recipe.estimatedCost <= dailyBudget ? 1 : dailyBudget / recipe.estimatedCost;

    // Calorie target match (prefer recipes close to daily target / 3 for dinner)
    const targetMealCalories = profile.targetCalories / 3;
    const calorieDiff = Math.abs(recipe.calories - targetMealCalories);
    const calorieScore = 1 - (calorieDiff / targetMealCalories);

    // Rating factor
    const ratingScore = recipe.averageRating / 5;

    const totalScore = (availabilityScore * 0.4) + (budgetScore * 0.2) +
                      (calorieScore * 0.2) + (ratingScore * 0.2);

    if (totalScore > bestScore) {
      bestScore = totalScore;
      bestRecipe = recipe;
    }
  });

  if (bestRecipe) {
    selectedRecipes.push(bestRecipe);
    if (bestRecipe.cuisines && bestRecipe.cuisines.length > 0) {
      usedCuisines.push(bestRecipe.cuisines[0]);
    }
  }
}

// Create meal plan
const startDate = new Date();
const endDate = new Date(startDate.getTime() + 7 * 24 * 60 * 60 * 1000);

const plannedMeals = selectedRecipes.map((recipe, index) => {
  const scheduledDate = new Date(startDate.getTime() + index * 24 * 60 * 60 * 1000);

  return {
    recipeId: recipe.id,
    recipeName: recipe.name,
    scheduledFor: scheduledDate.toISOString(),
    mealType: 'dinner',
    servings: profile.householdSize || 2,
    status: 'planned',
    actualServings: 0
  };
});

const totalCost = selectedRecipes.reduce((sum, r) => sum + r.estimatedCost, 0);
const avgCalories = selectedRecipes.reduce((sum, r) => sum + r.calories, 0) / selectedRecipes.length;

const mealPlan = SDK.Entities.MealPlan.upsert({
  userId: profile.id || 'demo-user',
  startDate: startDate.toISOString(),
  endDate: endDate.toISOString(),
  plannedMeals: plannedMeals,
  estimatedCost: totalCost,
  status: 'active'
});

SDK.Out.answer(`✅ Week Meal Plan Created!

📅 ${selectedRecipes.length} dinners planned
💰 Estimated cost: $${totalCost.toFixed(2)} (Budget: $${profile.weeklyFoodBudget})
🔥 Avg calories: ${Math.round(avgCalories)}/meal

Meals:
${selectedRecipes.map((r, i) => `${i + 1}. ${r.name} (${r.cuisines[0] || 'Mixed'})`).join('\n')}

Plan ID: ${mealPlan.id}`);
