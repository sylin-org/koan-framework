/**
 * Sunday Meal Prep Timeline
 *
 * Generates a batch cooking timeline for Sunday meal prep.
 * Demonstrates: Batch optimization, timeline generation, parallelization logic
 */

const recipes = SDK.Entities.Recipe.collection();

// Filter for batch-friendly recipes
const batchRecipes = recipes.items.filter(r => r.isBatchFriendly);

if (batchRecipes.length === 0) {
  SDK.Out.warn("No batch-friendly recipes found!");
} else {
  // Select top 3 for variety
  const selected = batchRecipes
    .sort((a, b) => b.averageRating - a.averageRating)
    .slice(0, 3);

  // Group by cooking method for parallel prep
  const ovenRecipes = selected.filter(r =>
    r.requiredEquipment.some(e => e.includes('oven'))
  );
  const stovetopRecipes = selected.filter(r =>
    !r.requiredEquipment.some(e => e.includes('oven'))
  );

  // Generate timeline
  let timeline = `🍳 Sunday Meal Prep Timeline\n\n`;
  timeline += `Selected Recipes:\n`;
  selected.forEach((r, i) => {
    timeline += `${i + 1}. ${r.name} (${r.totalTimeMinutes}min)\n`;
  });

  timeline += `\n⏰ Prep Schedule:\n\n`;

  let currentTime = 0;

  // Phase 1: Parallel prep
  timeline += `⏱️  0:00 - START\n`;
  timeline += `  Wash hands, set out all ingredients\n\n`;

  currentTime = 15;
  timeline += `⏱️  0:15 - PREP PHASE\n`;
  selected.forEach(r => {
    timeline += `  • Prep ingredients for ${r.name}\n`;
  });

  // Phase 2: Cooking (oven first, then stovetop)
  currentTime = 45;
  timeline += `\n⏱️  0:45 - COOKING PHASE\n`;

  if (ovenRecipes.length > 0) {
    timeline += `  🔥 Preheat oven to 375°F\n`;
    ovenRecipes.forEach(r => {
      timeline += `  • Start ${r.name} (${r.cookTimeMinutes}min)\n`;
    });

    currentTime += Math.max(...ovenRecipes.map(r => r.cookTimeMinutes || 30));
  }

  if (stovetopRecipes.length > 0) {
    const ovenTime = ovenRecipes.length > 0
      ? Math.max(...ovenRecipes.map(r => r.cookTimeMinutes || 30))
      : 0;

    timeline += `\n⏱️  ${Math.floor(currentTime / 60)}:${(currentTime % 60).toString().padStart(2, '0')} - STOVETOP\n`;

    stovetopRecipes.forEach(r => {
      timeline += `  • Cook ${r.name}\n`;
      currentTime += r.cookTimeMinutes || 20;
    });
  }

  // Phase 3: Cooling & Storage
  currentTime += 15;
  timeline += `\n⏱️  ${Math.floor(currentTime / 60)}:${(currentTime % 60).toString().padStart(2, '0')} - COOL & STORE\n`;
  timeline += `  • Let food cool 15 minutes\n`;
  timeline += `  • Portion into containers\n`;
  timeline += `  • Label with date and reheat instructions\n`;

  currentTime += 15;
  timeline += `\n⏱️  ${Math.floor(currentTime / 60)}:${(currentTime % 60).toString().padStart(2, '0')} - DONE! ✅\n`;

  // Calculate nutrition totals
  const totalCalories = selected.reduce((sum, r) => sum + r.calories * r.servings, 0);
  const totalProtein = selected.reduce((sum, r) => sum + r.proteinGrams * r.servings, 0);

  timeline += `\n📊 Batch Results:\n`;
  timeline += `  • Total prep time: ${Math.floor(currentTime / 60)}h ${currentTime % 60}m\n`;
  timeline += `  • Total servings: ${selected.reduce((sum, r) => sum + r.servings, 0)}\n`;
  timeline += `  • Avg calories/serving: ${Math.round(totalCalories / selected.reduce((sum, r) => sum + r.servings, 0))}\n`;
  timeline += `  • Total protein: ${Math.round(totalProtein)}g\n`;

  SDK.Out.answer(timeline);
}
