# S16 PantryPal: AI-Powered Meal Planning

**Vision-based pantry management + intelligent meal suggestions + MCP code mode orchestration**

PantryPal demonstrates how AI naturally enhances applications when solving problems humans can't:
- **Computer Vision**: Detects grocery items from photos, extracting quantities and expiration dates
- **Natural Language**: Parses flexible input like "5 lbs, expires in a week"
- **Personalization**: Learns from user ratings to improve recipe suggestions
- **Multi-Entity Orchestration**: MCP Code Mode handles complex workflows in single roundtrips

## Quick Start

```bash
# From repository root
dotnet run --project samples/S16.PantryPal

# Or use the start script
./samples/S16.PantryPal/start.bat
```

The app starts with:
- 50+ seeded recipes (Italian, Mexican, Thai, American)
- Sample pantry items
- Demo user profile
- Mock vision service (no AI setup required)

Access:
- **API**: http://localhost:5000
- **MCP SDK**: http://localhost:5000/mcp/sdk/definitions
- **Swagger**: http://localhost:5000/swagger

## Core Features

### 1. Vision-Powered Pantry Management

Upload grocery photos → AI detects items with bounding boxes → Confirm and add to pantry

**Multi-Candidate Detection**: AI provides top 3 alternatives for each item, you pick the right one.

```http
POST /api/pantry/upload
Content-Type: multipart/form-data

photo=@groceries.jpg
```

**Response**: Detections with bounding boxes and confidence scores
```json
{
  "photoId": "abc123",
  "detections": [
    {
      "boundingBox": { "x": 50, "y": 100, "width": 200, "height": 150 },
      "candidates": [
        { "name": "chicken breast", "confidence": 0.95 },
        { "name": "chicken thigh", "confidence": 0.78 }
      ]
    }
  ]
}
```

### 2. Natural Language Input Parsing

Flexible quantity and expiration parsing:

- **ISO dates**: `"2025-10-15"`
- **Relative**: `"in 3 days"`, `"next month"`, `"tomorrow"`
- **Month names**: `"March 15"`, `"Oct 10"`
- **Fractions**: `"1/2 cup"`, `"3 1/2 lbs"`
- **Flexible units**: `"lb"`, `"pounds"`, `"can"`, `"jar"`, `"whole"`

```http
POST /api/pantry/confirm/{photoId}
{
  "confirmations": [
    {
      "detectionId": "det1",
      "selectedCandidateId": "cand1",
      "userInput": "2 lbs, expires next week"
    }
  ]
}
```

Parser extracts:
```json
{
  "quantity": 2,
  "unit": "lbs",
  "expiresAt": "2025-10-14T00:00:00Z",
  "confidence": "High"
}
```

### 3. Intelligent Meal Suggestions

AI suggests recipes based on:
- Available pantry items
- Dietary restrictions
- Cooking time constraints
- User ratings and history

```http
POST /api/meals/suggest
{
  "userId": "user1",
  "dietaryRestrictions": ["vegetarian"],
  "maxCookingMinutes": 45
}
```

**Response**: Scored recipes with availability analysis
```json
{
  "recipe": {
    "name": "Penne Arrabbiata",
    "availabilityScore": 0.85,
    "missingIngredients": ["red chili flakes"]
  },
  "score": 0.78
}
```

### 4. MCP Code Mode Orchestration

Complex workflows execute in single roundtrip:

**Example: Photo Upload → Inventory Update → Recipe Suggestions**

```javascript
// MCP Code Mode script
const photo = SDK.Entities.PantryPhoto.getById(photoId);
const pantry = SDK.Entities.PantryItem.collection();

// Process each detection
photo.detections.forEach(detection => {
  const item = detection.candidates[0]; // Top AI pick

  // Check for duplicates
  const existing = pantry.items.find(p =>
    p.name.toLowerCase() === item.name.toLowerCase()
  );

  if (existing) {
    // Update quantity
    SDK.Entities.PantryItem.upsert({
      id: existing.id,
      quantity: existing.quantity + 1
    });
  } else {
    // Create new item
    SDK.Entities.PantryItem.upsert({
      name: item.name,
      category: item.category,
      quantity: 1,
      unit: item.defaultUnit,
      status: "available"
    });
  }
});

// Get updated pantry
const updated = SDK.Entities.PantryItem.collection();

// Suggest recipes using new items
const recipes = SDK.Entities.Recipe.collection({
  pageSize: 5
});

SDK.Out.answer(JSON.stringify({
  itemsAdded: photo.detections.length,
  totalPantryItems: updated.totalCount,
  suggestedRecipes: recipes.items.map(r => r.name)
}));
```

**Traditional MCP**: 10+ roundtrips (get photo, check duplicates per item, upsert items, get pantry, get recipes)

**Code Mode**: 1 roundtrip with full logic

## Entity Model

```csharp
[McpEntity] Recipe           // 50+ recipes with nutrition, instructions
[McpEntity] PantryItem       // Inventory with vision metadata
[McpEntity] MealPlan         // Scheduled meals with user feedback
[McpEntity] ShoppingList     // Auto-generated from meal plans
[McpEntity] UserProfile      // Preferences, goals, dietary restrictions
[McpEntity] PantryPhoto      // Photos with AI detection results
[McpEntity] VisionSettings   // User vision preferences
```

All entities support:
- Full CRUD via `EntityController<T>`
- MCP Code Mode via `SDK.Entities.{Name}`
- Multi-tenant routing via `set` parameter
- Relationship expansion via `with` parameter

## API Endpoints

### Pantry Management
- `POST /api/pantry/upload` - Upload photo for vision processing
- `POST /api/pantry/confirm/{photoId}` - Confirm detections, add to pantry
- `GET /api/pantry/search` - Search by name, category, expiring soon
- `GET /api/pantry/stats` - Pantry statistics and insights

### Meal Planning
- `POST /api/meals/suggest` - Get recipe suggestions
- `POST /api/meals/plan` - Create multi-day meal plan
- `POST /api/meals/shopping/{planId}` - Generate shopping list

### Entity CRUD
- `GET /api/data/recipes` - List all recipes
- `GET /api/data/recipes/{id}` - Get recipe by ID
- `POST /api/data/recipes` - Create/update recipe
- Similar for: `/pantry`, `/mealplans`, `/shopping`, `/profiles`

## MCP Code Mode Scripts

Located in `Scripts/` folder:

1. **simple-dinner.js** - Basic meal suggestion
2. **smart-suggest.js** - Context-aware with waste reduction
3. **week-planning.js** - Week planning with optimization
4. **meal-prep.js** - Sunday meal prep timeline
5. **grocery-haul.js** - Photo processing workflow

Execute via:
```http
POST /mcp/rpc
{
  "method": "tools/call",
  "params": {
    "name": "koan.code.execute",
    "arguments": {
      "code": "/* script content */"
    }
  }
}
```

## Vision Processing Pipeline

```
Photo Upload
    ↓
AI Detection (Ollama llava / Mock)
    ↓
Bounding Box Generation
    ↓
Multi-Candidate Results (top 3 per item)
    ↓
User Selection & Natural Language Input
    ↓
Parser → Structured Data
    ↓
Pantry Item Created
```

**Mock Vision Service** (default): Returns realistic detections without AI infrastructure

**Production**: Replace with `OllamaVisionService` or `OpenAIVisionService`

```csharp
// In Initialization/KoanAutoRegistrar.cs
services.AddSingleton<IPantryVisionService, OllamaVisionService>();
```

## Configuration

### Vision Service
```json
{
  "Vision": {
    "Provider": "mock",  // or "ollama", "openai"
    "MinConfidence": 0.5,
    "MaxDetectionsPerPhoto": 20
  }
}
```

### MCP Code Mode
```json
{
  "Koan": {
    "Mcp": {
      "CodeMode": {
        "Enabled": true,
        "CpuMilliseconds": 3000,
        "MemoryMegabytes": 128
      }
    }
  }
}
```

## Progressive Complexity

**Level 1: Basic** - Manual pantry, simple suggestions

**Level 2: Vision** - Photo upload, AI detection, confirmation UI

**Level 3: Smart** - Context-aware suggestions (expiring items, macros)

**Level 4: Planning** - Week planning with budget/nutrition optimization

**Level 5: Advanced** - Meal prep workflows, batch cooking

## Development Tips

### Adding New Recipes

```csharp
// In Data/RecipeSeedData.cs
new Recipe
{
    Name = "Your Recipe",
    Cuisines = new[] { "Italian" },
    PrepTimeMinutes = 20,
    Ingredients = new[] {
        new RecipeIngredient { Name = "pasta", Amount = 1, Unit = "lbs" }
    },
    Steps = new[] { "Step 1...", "Step 2..." }
}
```

### Custom Vision Provider

```csharp
public class CustomVisionService : IPantryVisionService
{
    public async Task<VisionProcessingResult> ProcessPhotoAsync(...)
    {
        // Your AI logic here
        return new VisionProcessingResult
        {
            Success = true,
            Detections = detections,
            ProcessingTimeMs = elapsedMs
        };
    }
}
```

### Natural Language Parser Extensions

```csharp
// Extend PantryInputParser for new patterns
private bool TryParseCustomFormat(string input, out ParsedItemData result)
{
    // Add your parsing logic
}
```

## Architecture Highlights

**Entity-First Development**: `Todo.Get(id)`, `todo.Save()` patterns

**Multi-Provider Ready**: SQLite → Postgres/MongoDB with zero code changes

**Auto-Registration**: Adding package reference automatically enables features

**Self-Reporting**: Bootstrap reports show provider elections and capabilities

## Related Samples

- **S12.MedTrials**: Traditional MCP entity tools
- **S13.DocMind**: Vector search + AI integration
- **S14.AdapterBench**: Multi-provider benchmarking

## Related Decisions

- **AI-0014**: MCP Code Mode - Technical foundation
- **DATA-0078**: Vector Export for Migration
- **SAMPLE-0016**: This sample's architecture decision

## License

Part of Koan Framework - see root LICENSE file
