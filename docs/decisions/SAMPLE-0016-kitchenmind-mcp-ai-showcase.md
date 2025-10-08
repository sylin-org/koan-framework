# SAMPLE-0016 - PantryPal: AI-Powered Meal Planning Sample

Status: Approved
Date: 2025-10-07
Owners: Koan AI Platform, Koan.Mcp Team, Sample Applications

## Context

**Problem**: While AI-0014 delivered MCP Code Mode technical capabilities, we need a compelling sample application that demonstrates:
- **Natural AI integration** where AI solves problems that humans can't (vision, semantic search, personalization)
- **MCP code mode orchestration** handling multi-entity workflows in single roundtrips
- **Premium UX/DX** showing best practices for Koan + AI applications
- **Realistic domain** that's immediately understandable and valuable

**Why Traditional Samples Fall Short**:
- Task boards/sprint trackers: Enterprise-focused, don't leverage AI naturally
- Generic CRUD demos: Miss the "AI magic" that makes modern apps compelling
- Toy examples: Don't demonstrate real-world complexity

**Target Audience**:
- Developers evaluating Koan Framework for AI-powered applications
- Teams building consumer-facing AI products
- Architects designing multi-provider, entity-first systems with AI integration

## Decision

Create **S16.PantryPal**: An AI-powered meal planning and pantry management application showcasing:

### Core Features

**1. Vision-Powered Pantry Management**
- Upload photos of grocery items
- AI detects items, quantities, expiration dates via computer vision + OCR
- Multi-candidate selection with natural language input parsing
- Interactive bounding box UI for user confirmation
- Automatic duplicate detection and inventory updates

**2. Intelligent Meal Planning**
- AI suggests recipes based on pantry contents, nutrition goals, preferences
- Multi-day planning with budget optimization
- Waste reduction (use items expiring soon)
- Batch cooking and meal prep workflows
- Learning from user ratings and corrections

**3. Natural Language Understanding**
- Parse inputs like "5 lbs, expires in a week" or "expires next month"
- Flexible date parsing (ISO dates, relative dates, month names)
- Real-time preview of parsed data with warnings
- Smart defaults based on item category

**4. MCP Code Mode Orchestration**
- Complex workflows in single executions (photo → detection → pantry update → recipe suggestions)
- Multi-entity aggregations (pantry + meals + nutrition + budget)
- Conditional logic (if expiring soon, suggest recipes; if over budget, flag)
- Learning feedback loops (user corrections improve AI)

## Architecture

### Entity Model

```csharp
// Core entities demonstrating entity-first patterns
[McpEntity] Recipe           // 50+ seeded recipes with nutrition, instructions
[McpEntity] PantryItem       // Inventory with vision metadata
[McpEntity] MealPlan         // Scheduled/cooked meals with feedback
[McpEntity] ShoppingList     // Auto-generated from meal plans
[McpEntity] UserProfile      // Preferences, goals, dietary restrictions

// AI-specific entities
[McpEntity] PantryPhoto      // Photos with AI processing results
[McpEntity] VisionSettings   // User vision preferences and learning
```

### AI Integration Points

**1. Vision Pipeline**
```
Photo Upload → AI Detection (Ollama llava / GPT-4 Vision)
            → OCR (Tesseract)
            → Barcode Scanning (ZXing)
            → Post-processing & Normalization
            → Multi-candidate results
```

**2. Natural Language Parser**
```csharp
IPantryInputParser.ParseInput("5 lbs, expires in a week")
→ { quantity: 5, unit: "lbs", expiresAt: DateTime(+7days) }

Supports:
- ISO dates: "2025-10-10"
- Relative: "in 3 days", "next month", "tomorrow"
- Month names: "March 15", "Oct 10"
- Flexible units: "lb", "pounds", "can", "jar", "whole"
```

**3. Meal Suggestions** (AI + MCP Orchestration)
```javascript
// MCP code mode gathers context in one execution
const pantry = SDK.Entities.PantryItem.collection();
const recentMeals = SDK.Entities.MealPlan.collection({ sort: '-cookedAt', pageSize: 7 });
const profile = SDK.Entities.UserProfile.getById(userId);
const todaysNutrition = /* aggregate from today's meals */;

// AI receives rich context and suggests optimized recipes
// Code mode then validates, checks duplicates, creates meal plan + shopping list
```

### User Experience Layers

**Level 1: Basic** - "What should I cook?" → One suggestion
**Level 2: Smart** - Context-aware (expiring items, macros, recent meals)
**Level 3: Planning** - Week planning with budget/nutrition optimization
**Level 4: Advanced** - Meal prep workflows, batch cooking timelines

### Developer Experience

**Seeded Data**:
- 50+ recipes across cuisines (Italian, Mexican, Thai, American)
- Categorized by difficulty, meal type, dietary tags
- Realistic nutrition data and prep times
- Sample pantry items with expiration dates

**Code Mode Scripts** (5 examples):
1. Simple dinner suggestion (basic)
2. Smart suggestion with waste reduction (intermediate)
3. Week planning with optimization (advanced)
4. Meal prep Sunday timeline (expert)
5. Grocery haul processing (vision integration)

**Documentation**:
- Progressive complexity README
- API endpoint documentation
- Vision pipeline architecture diagrams
- Natural language parser examples
- MCP orchestration patterns

## Technical Decisions

### Why This Domain?

**AI is Essential (Not Bolted On)**:
- Vision: Humans can't parse photos into structured data at scale
- Personalization: AI learns preferences from rating history
- Planning: Multi-constraint optimization (nutrition + budget + time + variety)
- Semantic search: "something creamy and comforting" → recipe matching

**MCP Code Mode is Essential**:
- Photo processing: Check duplicates, update inventory, suggest recipes (4+ entity operations)
- Meal planning: Aggregate pantry, check history, calculate nutrition, generate shopping list (6+ operations)
- Learning: User corrections need to update VisionSettings, create feedback records, trigger retraining

**Real-World Value**:
- Universal problem: Everyone asks "what's for dinner?"
- Immediate utility: Reduces food waste, saves money, improves nutrition
- Engaging: Visual interface, personalized results, learning over time

### Technology Stack

**AI Services**:
- Vision: Ollama (llava) for local dev, OpenAI Vision for production
- OCR: Tesseract for expiration date reading
- Barcode: ZXing for product lookup
- Embeddings: Recipe semantic search (future: vector DB integration)

**Data Storage**:
- SQLite for local dev (simple setup)
- Multi-provider ready (Postgres, MongoDB, etc.)
- Koan.Storage for photo persistence

**Frontend** (Minimal for Sample):
- Photo upload component
- Bounding box visualization
- Detection editor modal
- Recipe cards and meal plan calendar

## Implementation Phases

### Phase 1: Core Entities & Basic Features ✅
- [ ] Entity model (Recipe, PantryItem, MealPlan, UserProfile, ShoppingList)
- [ ] Seed data (50+ recipes, sample pantry)
- [ ] Basic meal suggestion (no AI)
- [ ] Manual pantry management

### Phase 2: Vision Integration 🎯
- [ ] PantryPhoto entity
- [ ] Upload API + storage
- [ ] Ollama llava integration for item detection
- [ ] Multi-candidate detection results
- [ ] Bounding box UI component
- [ ] Detection editor with selection

### Phase 3: Natural Language Parser 🎯
- [ ] IPantryInputParser implementation
- [ ] Quantity parsing (various units)
- [ ] Expiration parsing (ISO, relative, month names)
- [ ] Real-time preview API
- [ ] Parse warnings and confidence scoring

### Phase 4: MCP Orchestration Scripts
- [ ] Photo processing workflow
- [ ] Smart meal suggestion with context
- [ ] Week planning optimization
- [ ] Meal prep timeline generation
- [ ] Grocery haul batch processing

### Phase 5: Learning & Polish
- [ ] User correction tracking
- [ ] AI model fine-tuning from feedback
- [ ] Recipe rating influence
- [ ] Pantry value tracking
- [ ] Nutrition goal monitoring

## Success Metrics

**Developer Experience**:
- ✅ Working sample in <5 minutes (`dotnet run`)
- ✅ Clear progression from basic → advanced usage
- ✅ Copy-pasteable code mode scripts
- ✅ Well-documented AI integration points

**Technical Demonstration**:
- ✅ Vision AI processing with <2s latency
- ✅ 4+ entity operations in single MCP execution
- ✅ Natural language parsing with 90%+ accuracy
- ✅ Multi-provider data patterns (SQLite → Postgres migration path)

**User Experience**:
- ✅ Intuitive photo upload flow
- ✅ Interactive bounding box selection
- ✅ Flexible natural language input
- ✅ Helpful warnings and suggestions

## Consequences

### Positive

**Demonstrates Framework Capabilities**:
- Entity-first patterns with real-world complexity
- Multi-provider data architecture
- AI service abstraction (Koan.AI)
- Storage integration (Koan.Storage)
- MCP code mode orchestration at scale

**Provides Developer Templates**:
- Vision pipeline implementation
- Natural language parsing patterns
- Multi-entity workflow orchestration
- Learning feedback loops
- Progressive complexity UX

**Marketing & Education**:
- Compelling demo for sales/evangelism
- Tutorial for AI + Koan integration
- Reference for premium UX patterns
- Showcase for open-source promotion

### Negative

**Complexity**:
- More sophisticated than simple CRUD samples
- Requires AI service configuration (Ollama/OpenAI)
- Vision processing adds latency considerations
- Learning curves: vision, NLP, multi-entity orchestration

**Maintenance**:
- AI models evolve (llava updates, GPT-4 Vision changes)
- Vision accuracy depends on model quality
- Seed data maintenance (recipe database)
- UI component updates as frontend frameworks evolve

### Mitigation Strategies

**Complexity Management**:
- Progressive disclosure: Start with basic features, layer advanced
- Excellent documentation with diagrams
- Pre-configured defaults (works with Ollama out of box)
- Graceful degradation (manual input if vision fails)

**Maintenance**:
- Abstract AI providers (IVisionService, IOcrService)
- Versioned seed data with migration scripts
- Minimal UI dependencies (vanilla JS where possible)
- Automated tests for NLP parser and orchestration logic

## Related Decisions

- **AI-0014**: MCP Code Mode - Technical foundation for orchestration
- **DATA-0078**: Vector Export for Migration - Future recipe similarity search
- **S12**: MedTrials - Prior MCP sample (traditional tools only)
- **S13**: DocMind - AI integration patterns

## Examples

### Vision Processing with MCP Orchestration

```javascript
// After photo upload and AI processing
const photo = SDK.Entities.PantryPhoto.getById(photoId);
const settings = SDK.Entities.VisionSettings.getById(userId);
const existingPantry = SDK.Entities.PantryItem.collection();

// Check each detection for duplicates
const updates = [];
const newItems = [];

photo.detections.forEach(detection => {
  const existing = existingPantry.items.find(item =>
    item.name.toLowerCase() === detection.itemName.toLowerCase()
  );

  if (existing) {
    // Update quantity
    SDK.Entities.PantryItem.upsert({
      id: existing.id,
      quantity: existing.quantity + (detection.detectedQuantity || 1)
    });
    updates.push(existing.name);
  } else {
    // Create new item
    SDK.Entities.PantryItem.upsert({
      name: detection.itemName,
      quantity: detection.detectedQuantity || 1,
      unit: detection.detectedUnit || 'whole',
      expiresAt: detection.detectedExpiration,
      source: 'photo',
      sourcePhotoId: photoId,
      detectionConfidence: detection.confidence
    });
    newItems.push(detection.itemName);
  }
});

// Suggest recipes using new items
const recipes = SDK.Entities.Recipe.collection({
  filter: { ingredients_contains_any: newItems },
  pageSize: 3
});

SDK.Out.answer(JSON.stringify({
  added: newItems.length,
  updated: updates.length,
  suggestions: recipes.items.map(r => r.name)
}));
```

### Natural Language Input

```javascript
// User types: "5 lbs, expires in a week"
const parsed = parseInput("5 lbs, expires in a week");

// Result:
{
  quantity: 5,
  unit: "lbs",
  expiresAt: "2025-10-14T00:00:00Z",
  confidence: "High",
  preview: "5 lbs, expires Oct 14, 2025"
}

// User types: "2 cans, best by next month"
const parsed = parseInput("2 cans, best by next month");

// Result:
{
  quantity: 2,
  unit: "whole",
  expiresAt: "2025-11-07T00:00:00Z",
  confidence: "Medium",
  preview: "2 whole, expires Nov 07, 2025",
  warnings: ["Assumed start of next month"]
}
```

### Interactive Detection UI

```
┌─────────────────────────────────────────┐
│  📸 Photo Analysis                      │
├─────────────────────────────────────────┤
│  [PHOTO WITH BOUNDING BOXES]            │
│   ┌──────────┐                          │
│   │ Chicken  │ 95% ← Click to edit      │
│   │ Breast   │                          │
│   └──────────┘                          │
│                                         │
│        ┌─────────┐                      │
│        │ Canned  │ 67% ← Review needed  │
│        │ Beans   │                      │
│        └─────────┘                      │
└─────────────────────────────────────────┘
📦 2 items detected | ✅ 0 confirmed | ⏳ 2 pending

[Click box] →

┌─────────────────────────────────────────┐
│  ✏️ Edit Detection                      │
├─────────────────────────────────────────┤
│  Item Type:                             │
│  ● Chicken Breast (95%) ← AI top pick  │
│  ○ Chicken Thigh (78%)                  │
│  ○ Chicken Wings (45%)                  │
│                                         │
│  Quantity & Expiration:                 │
│  ┌─────────────────────────────────┐    │
│  │ 2 lbs, expires in a week        │    │
│  └─────────────────────────────────┘    │
│  ✓ 2 lbs, expires Oct 14, 2025          │
│                                         │
│  [✗ Reject] [💾 Save] [✓ Add to Pantry]│
└─────────────────────────────────────────┘
```

## References

- [Cloudflare Code Mode](https://blog.cloudflare.com/building-ai-agents-with-workers-ai)
- [Anthropic Extended Thinking](https://docs.anthropic.com/en/docs/build-with-claude/extended-thinking)
- [Ollama Vision Models](https://ollama.com/library/llava)
- [Tesseract OCR](https://github.com/tesseract-ocr/tesseract)
- [ZXing Barcode Scanner](https://github.com/zxing/zxing)
- [Koan AI Integration Patterns](../../samples/S13.DocMind/)
