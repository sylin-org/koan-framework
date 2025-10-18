# Analysis Styles Architecture

## Overview

This document describes the architecture for customizable AI analysis styles in SnapVault, implementing a **Prompt Factory Pattern** that separates prompt assembly logic from user-configurable style parameters.

## Design Principles

1. **Single Source of Truth**: Base prompt lives in code (version-controlled), not duplicated in database
2. **Separation of Concerns**: Data (style parameters) vs Logic (prompt assembly) vs Content (base prompt)
3. **Safe Extensibility**: Users customize via parameters, not raw prompts (prevents prompt injection)
4. **Koan Framework Alignment**: Entity-first pattern with `AnalysisStyle : Entity<AnalysisStyle>`
5. **Performance**: Base prompt cached in code, no DB hit for default analysis

## Architecture Components

### 1. Prompt Factory Pattern

```
IAnalysisPromptFactory (interface)
  └── AnalysisPromptFactory (implementation)
        ├── BASE_PROMPT (constant) - 200-line rigorous JSON prompt
        ├── RenderPrompt() → Returns base prompt verbatim
        ├── RenderPromptFor(style) → Base + style customizations
        └── GetClassificationPrompt(styles) → Smart mode classifier
```

**Responsibilities**:
- Store and protect base prompt
- Assemble customized prompts from style parameters
- Apply variable substitution ({{width}}, {{camera}}, etc.)
- Generate classification prompts for smart mode

### 2. AnalysisStyle Entity

Lightweight entity storing **parameters**, not full prompts:

```csharp
public class AnalysisStyle : Entity<AnalysisStyle>
{
    // Metadata
    public string Name { get; set; }              // "Portrait & People"
    public string Icon { get; set; }              // "👤"
    public string Description { get; set; }       // "Focus on faces..."
    public int Priority { get; set; }             // Display order

    // Factory Parameters (Safe Customization)
    public string? FocusInstructions { get; set; }         // Prepended to base
    public List<string> EnhanceExamples { get; set; }      // ["subject clothing"]
    public List<string> RequiredOptionalFacts { get; set; } // ["subject 1"]
    public List<string> OmittedOptionalFacts { get; set; }  // ["atmospherics"]

    // Smart Classification
    public bool IsSmartStyle { get; set; }        // True for "smart" mode
    public string? ClassificationKeywords { get; set; }  // For classifier

    // System Management
    public bool IsSystemStyle { get; set; }       // Can't be deleted
    public bool IsActive { get; set; } = true;    // Soft delete

    // Escape Hatch (Advanced Users)
    public string? FullPromptOverride { get; set; } // Bypasses factory
}
```

### 3. PhotoAsset Cache Enhancement

Caches smart mode classification to avoid repeated API calls:

```csharp
public class PhotoAsset : MediaEntity<PhotoAsset>
{
    // Existing fields...

    // Smart mode cache
    public string? InferredStyleId { get; set; }  // FK to AnalysisStyle
    public DateTime? InferredAt { get; set; }     // When inference ran
}
```

## Prompt Assembly Flow

### Default Analysis (No Style)
```
User clicks "Regenerate Description"
  → PhotoProcessingService calls factory.RenderPrompt()
  → Returns base prompt verbatim
  → Apply variable substitution ({{width}}, {{camera}})
  → Send to AI
```

### Direct Style Selection
```
User selects "Portrait & People" from dropdown
  → PhotoProcessingService calls factory.RenderPromptFor(portraitStyle)
  → Factory assembles:
      1. Add FocusInstructions at top
      2. Enhance examples (subject 1 with clothing details)
      3. Add required facts reminder
      4. Omit irrelevant facts from examples
  → Apply variable substitution
  → Send to AI
```

### Smart Mode (Two-Stage)
```
User selects "Smart Analysis"
  → Check PhotoAsset.InferredStyleId cache
  → If cached: Use that style
  → If not cached:
      1. Call factory.GetClassificationPrompt(availableStyles)
      2. Send classification prompt to AI (quick call)
      3. Parse detected style name
      4. Cache result in PhotoAsset.InferredStyleId
      5. Call factory.RenderPromptFor(detectedStyle)
  → Apply variable substitution
  → Send to AI
```

## Example: Portrait Style Configuration

### Database Record
```json
{
  "name": "Portrait & People",
  "icon": "👤",
  "description": "Focus on faces, emotion, and human subjects",
  "priority": 1,
  "focusInstructions": "ANALYSIS FOCUS:\nPay special attention to:\n- Facial expressions and emotional content\n- Clothing details and fashion choices\n- Body language, pose, and gaze direction\n- Interpersonal relationships between subjects",
  "enhanceExamples": ["subject clothing", "facial expressions"],
  "requiredOptionalFacts": ["subject 1", "subject 2"],
  "omittedOptionalFacts": ["atmospherics", "locale cues"],
  "isSystemStyle": true,
  "isActive": true
}
```

### Assembled Prompt
```
ANALYSIS FOCUS:
Pay special attention to:
- Facial expressions and emotional content
- Clothing details and fashion choices
- Body language, pose, and gaze direction
- Interpersonal relationships between subjects

Analyze the image and output ONLY valid JSON (no markdown, no comments)...
[Base prompt with enhanced examples:]
"subject 1": ["person", "joyful-expression", "black-hoodie", "red-headphones", "relaxed-pose", "looking-left", "confident-demeanor", "..."]

[Required facts reminder added:]
IMPORTANT: For this image type, ensure these optional facts are included if visible: subject 1, subject 2
```

## Seeding Strategy

### Default Styles
System seeds 7 default styles on first startup:

1. **🤖 Smart Analysis** - Two-stage classification + detected style
2. **👤 Portrait & People** - Facial expressions, clothing, pose
3. **📦 Product & Commercial** - Features, materials, marketing
4. **🏔️ Landscape & Nature** - Composition, lighting, atmosphere
5. **🏛️ Architecture & Interior** - Lines, symmetry, materials
6. **⚡ Action & Moment** - Motion, timing, dynamics
7. **🔍 Detail & Macro** - Texture, pattern, close-up

### Seeder Implementation
```csharp
public static class AnalysisStyleSeeder
{
    public static async Task SeedDefaultStylesAsync()
    {
        var existing = await AnalysisStyle.All();
        if (existing.Any()) return; // Already seeded

        var styles = new[]
        {
            CreateSmartStyle(),
            CreatePortraitStyle(),
            CreateProductStyle(),
            // ... etc
        };

        foreach (var style in styles)
        {
            await style.Save();
        }
    }
}

// Called from Program.cs after AddKoan()
await AnalysisStyleSeeder.SeedDefaultStylesAsync();
```

## API Endpoints

### Get Available Styles
```http
GET /api/analysis-styles
```
Returns all active styles ordered by priority.

### Get Style by ID
```http
GET /api/analysis-styles/{id}
```
Returns specific style configuration.

### Create Custom Style (Future)
```http
POST /api/analysis-styles
{
  "name": "Fashion Editorial",
  "icon": "👗",
  "description": "High fashion and editorial photography",
  "focusInstructions": "Focus on fashion trends, styling choices...",
  "enhanceExamples": ["subject clothing", "accessories"],
  "requiredOptionalFacts": ["subject 1", "themes"],
  "isSystemStyle": false
}
```

### Regenerate with Style
```http
POST /api/photos/{id}/regenerate-ai-analysis
{
  "analysisStyle": "portrait"  // or "smart", or custom style ID
}
```

## Performance Optimizations

### 1. Classification Caching
- First analysis: Run classification, cache in `PhotoAsset.InferredStyleId`
- Subsequent "smart" analyses: Use cached result (no extra API call)
- Invalidate cache: Never (user can override by selecting specific style)

### 2. Base Prompt Caching
- Base prompt is constant in code (no DB query)
- Factory methods are stateless (no instance state)
- Registered as Singleton in DI

### 3. Style Query Optimization
```csharp
// Bad: Query DB every analysis
var style = await AnalysisStyle.Get(styleId);

// Good: Cache styles in memory (singleton factory)
private readonly Dictionary<string, AnalysisStyle> _styleCache;
```

## User Extensibility

### Phase 1 (Current): Parameter-Based
Users create styles by configuring safe parameters:
- Focus instructions (free text)
- Example enhancements (predefined list)
- Required/omitted facts (predefined list)

### Phase 2 (Future): Advanced Customization
- JSON-based example modifications
- Conditional logic (if width > 2000, add "high-resolution")
- Multi-language support

### Phase 3 (Future): Full Override
- `FullPromptOverride` field for power users
- Validation: Must return valid JSON structure
- Sandboxing: Run in isolated context
- Preview/test functionality before saving

## Security Considerations

### Prompt Injection Prevention
1. **Parameter-based customization**: Users don't control full prompt
2. **String sanitization**: Focus instructions are plain text (no code execution)
3. **Validation**: Ensure enhanceExamples reference valid categories
4. **Sandboxing**: Future full overrides run in isolated context

### Output Validation
1. **JSON structure check**: Ensure AI returns valid JSON
2. **Fact key validation**: All keys must be lowercase
3. **Array value validation**: All fact values must be arrays
4. **Fallback**: Return error state if parsing fails

## Testing Strategy

### Unit Tests (Factory)
```csharp
[Fact]
public void RenderPrompt_ReturnsBasePrompt()
{
    var result = _factory.RenderPrompt();
    Assert.Contains("Analyze the image and output ONLY valid JSON", result);
}

[Fact]
public void RenderPromptFor_Portrait_AddsClothingFocus()
{
    var portrait = CreatePortraitStyle();
    var result = _factory.RenderPromptFor(portrait);

    Assert.Contains("Facial expressions", result);
    Assert.Contains("joyful-expression", result);
}

[Fact]
public void GetClassificationPrompt_IncludesActiveStyles()
{
    var styles = new[] { CreatePortraitStyle(), CreateLandscapeStyle() };
    var result = _factory.GetClassificationPrompt(styles);

    Assert.Contains("Portrait & People", result);
    Assert.Contains("Landscape & Nature", result);
}
```

### Integration Tests (Service)
```csharp
[Fact]
public async Task GenerateAnalysis_SmartMode_CachesInference()
{
    var photo = await CreateTestPhoto();

    await _service.RegenerateAIAnalysisAsync(photo.Id, "smart");

    var updated = await PhotoAsset.Get(photo.Id);
    Assert.NotNull(updated.InferredStyleId);
    Assert.NotNull(updated.InferredAt);
}
```

### Regression Tests (Prompt Quality)
```csharp
[Theory]
[InlineData("portrait_sample1.jpg", ExpectedTags = new[] { "person", "portrait" })]
[InlineData("landscape_sample1.jpg", ExpectedTags = new[] { "landscape", "nature" })]
public async Task Analysis_MaintainsQuality(string imagePath, string[] expectedTags)
{
    var result = await _service.AnalyzeImageAsync(imagePath);

    Assert.All(expectedTags, tag => Assert.Contains(tag, result.Tags));
}
```

## Migration Path

### From Config-Based (Current) to Entity-Based (New)

**Step 1**: Create entity and factory (backward compatible)
- Factory reads from entities if available
- Falls back to config if entities not found

**Step 2**: Run seeder to populate entities
- One-time migration on startup
- Copy config values to database

**Step 3**: Remove config fallback (future)
- Once entities proven stable
- Remove appsettings.json styles section

**Feature Flag**:
```csharp
if (_config.GetValue<bool>("Features:UseEntityBasedStyles", true))
{
    return await _factory.RenderPromptFor(style);
}
else
{
    return _configStrategy.GetPromptTemplate(style.Name, context);
}
```

## Monitoring and Observability

### Metrics to Track
1. **Style usage**: Which styles are most popular?
2. **Classification accuracy**: Does smart mode pick correct styles?
3. **Performance**: Latency breakdown (classification vs analysis)
4. **Quality**: Are customized prompts maintaining JSON structure?

### Logging
```csharp
_logger.LogInformation(
    "Generating analysis for {PhotoId} using style '{StyleName}' (inferred: {WasInferred})",
    photo.Id, style.Name, wasInferred);

_logger.LogInformation(
    "Smart classification: {PhotoId} → {DetectedStyle} in {ElapsedMs}ms",
    photo.Id, detectedStyle.Name, classificationTime.TotalMilliseconds);
```

## Future Enhancements

### 1. A/B Testing Framework
```csharp
public class StyleVariant : Entity<StyleVariant>
{
    public string ParentStyleId { get; set; }
    public string VariantName { get; set; }
    public string FocusInstructions { get; set; }
    public double TrafficPercent { get; set; } // 0.0-1.0
}
```

### 2. Multi-Language Support
```csharp
public class AnalysisStyle
{
    public Dictionary<string, string> LocalizedDescriptions { get; set; }
    public string GetDescription(string locale) =>
        LocalizedDescriptions.TryGetValue(locale, out var desc) ? desc : Description;
}
```

### 3. Style Recommendations
```csharp
// Based on photo characteristics, recommend best style
var recommendedStyle = await _styleRecommender
    .RecommendStyleAsync(photo.Width, photo.Height, photo.CameraModel);
```

### 4. Community Style Sharing
```csharp
public class StyleMarketplace
{
    public async Task<AnalysisStyle> DownloadStyleAsync(string marketplaceId);
    public async Task PublishStyleAsync(AnalysisStyle style);
}
```

## Conclusion

This architecture achieves:
- ✅ **Quality preservation**: Base prompt protected in code
- ✅ **Safe extensibility**: Users customize via parameters
- ✅ **Performance**: Caching at multiple levels
- ✅ **Maintainability**: Clean separation of concerns
- ✅ **Testability**: Factory is unit testable
- ✅ **Koan alignment**: Entity-first pattern with auto-generated API
- ✅ **Future-proof**: Escape hatch for advanced needs

The factory pattern provides the best balance of control (developers) and flexibility (users) while maintaining the rigorous quality of the original prompt.
