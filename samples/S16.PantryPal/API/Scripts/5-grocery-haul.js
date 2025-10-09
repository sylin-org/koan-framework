/**
 * Grocery Haul Processing
 *
 * Full workflow: Process photo detections → Update pantry → Suggest recipes
 * Demonstrates: Multi-step orchestration, duplicate detection, conditional updates
 *
 * Usage: Replace 'PHOTO_ID_HERE' with actual photo ID from upload
 */

const photoId = 'PHOTO_ID_HERE'; // Replace with actual photo ID

// Get the processed photo
const photo = SDK.Entities.PantryPhoto.getById(photoId);

if (!photo) {
  SDK.Out.warn(`Photo ${photoId} not found. Upload a photo first using POST /api/pantry/upload`);
} else if (!photo.detections || photo.detections.length === 0) {
  SDK.Out.warn('No detections found in this photo. Try uploading a clearer image.');
} else {
  // Get existing pantry items
  const pantry = SDK.Entities.PantryItem.collection();

  const added = [];
  const updated = [];
  const skipped = [];

  // Process each detection
  photo.detections.forEach(detection => {
    if (detection.status === 'confirmed' || detection.candidates.length === 0) {
      skipped.push('Already processed or no candidates');
      return;
    }

    // Use top candidate
    const candidate = detection.candidates[0];

    // Check for duplicate
    const existing = pantry.items.find(item =>
      item.name.toLowerCase() === candidate.name.toLowerCase()
    );

    if (existing) {
      // Update quantity
      const newQuantity = existing.quantity + (detection.parsedData?.quantity || 1);

      SDK.Entities.PantryItem.upsert({
        id: existing.id,
        quantity: newQuantity
      });

      updated.push({
        name: candidate.name,
        oldQty: existing.quantity,
        newQty: newQuantity,
        unit: existing.unit
      });
    } else {
      // Create new item
      const newItem = SDK.Entities.PantryItem.upsert({
        name: candidate.name,
        category: candidate.category || 'uncategorized',
        quantity: detection.parsedData?.quantity || 1,
        unit: detection.parsedData?.unit || candidate.defaultUnit || 'whole',
        expiresAt: detection.parsedData?.expiresAt || null,
        location: candidate.category === 'produce' || candidate.category === 'meat' || candidate.category === 'dairy'
          ? 'fridge'
          : 'pantry',
        status: 'available',
        addedAt: new Date().toISOString(),
        source: 'photo',
        sourcePhotoId: photoId,
        visionMetadata: {
          sourcePhotoId: photoId,
          detectionId: detection.id,
          confidence: candidate.confidence,
          wasUserCorrected: false
        }
      });

      added.push({
        name: candidate.name,
        quantity: newItem.quantity,
        unit: newItem.unit,
        confidence: candidate.confidence
      });
    }
  });

  // Get updated pantry count
  const updatedPantry = SDK.Entities.PantryItem.collection();

  // Find recipes using newly added items
  const newItemNames = added.map(item => item.name.toLowerCase());
  const recipes = SDK.Entities.Recipe.collection({ pageSize: 100 });

  const suggestedRecipes = recipes.items
    .filter(recipe =>
      recipe.ingredients.some(ing =>
        newItemNames.some(itemName =>
          ing.name.toLowerCase().includes(itemName) ||
          itemName.includes(ing.name.toLowerCase())
        )
      )
    )
    .slice(0, 3);

  // Build response
  let message = `🛒 Grocery Haul Processed!\n\n`;

  message += `📦 Items Added (${added.length}):\n`;
  added.forEach(item => {
    message += `  ✅ ${item.name}: ${item.quantity} ${item.unit} (${Math.round(item.confidence * 100)}% confident)\n`;
  });

  if (updated.length > 0) {
    message += `\n🔄 Items Updated (${updated.length}):\n`;
    updated.forEach(item => {
      message += `  📈 ${item.name}: ${item.oldQty} → ${item.newQty} ${item.unit}\n`;
    });
  }

  message += `\n📊 Pantry Status:\n`;
  message += `  Total items: ${updatedPantry.totalCount}\n`;

  // Check for expiring items
  const now = new Date();
  const oneWeekFromNow = new Date(now.getTime() + 7 * 24 * 60 * 60 * 1000);
  const expiring = updatedPantry.items.filter(item =>
    item.expiresAt && new Date(item.expiresAt) <= oneWeekFromNow
  );

  if (expiring.length > 0) {
    message += `  ⚠️  ${expiring.length} item(s) expiring soon\n`;
  }

  if (suggestedRecipes.length > 0) {
    message += `\n🍽️  Recipe Suggestions:\n`;
    suggestedRecipes.forEach((recipe, i) => {
      message += `  ${i + 1}. ${recipe.name} (${recipe.totalTimeMinutes}min, ⭐${recipe.averageRating})\n`;
    });
  }

  SDK.Out.answer(message);
}
