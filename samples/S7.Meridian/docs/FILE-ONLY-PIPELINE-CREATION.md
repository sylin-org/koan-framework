# File-Only Pipeline Creation API

**Decision Date:** 2025-10-24
**Status:** Approved
**Context:** Meridian Phase 4.5 - Simplified pipeline creation for automation scenarios

---

## Problem Statement

Current pipeline creation requires **11+ API calls** with complex orchestration:
1. Create source types (4 calls: AI suggest + POST for each)
2. Create analysis type (1 call)
3. Create pipeline (1 call)
4. Upload documents (4 calls)
5. Wait for jobs (4 waits)
6. Retrieve deliverable (1 call)

This creates friction for:
- **Automation scripts** - Complex error handling and state management
- **Testing scenarios** - Difficult to reproduce exact conditions
- **Version control** - No declarative way to define pipeline structure
- **Team collaboration** - Hard to share pipeline configurations

---

## Decision

Implement a **file-only payload API** where:
1. Client uploads **only files** (no form fields, no JSON body)
2. One file **must be named** `analysis-config.json`
3. Config file contains pipeline metadata, analysis definition, and document manifest
4. Documents **in manifest** are assigned explicit source types
5. Documents **not in manifest** are auto-classified using AI/heuristics

### API Signature

```http
POST /api/pipelines/create
Content-Type: multipart/form-data

files[]: analysis-config.json
files[]: meeting-notes.txt
files[]: customer-bulletin.txt
files[]: vendor-prescreen.txt
```

---

## Configuration Schema

### Example: `analysis-config.json`

```json
{
  "pipeline": {
    "name": "Enterprise Architecture Review Q4 2025",
    "description": "Quarterly EA assessment for Atlas modernization",
    "tags": ["q4-2025", "atlas", "synapse"],
    "notes": "Primary vendor: Synapse Analytics. Contact: Jordan Kim.",
    "bias": "Focus on integration timeline and financial stability"
  },

  "analysis": {
    "type": "EAR"
  },

  "manifest": {
    "meeting-notes.txt": {
      "type": "MEET",
      "notes": "Steering committee meeting"
    },
    "vendor-prescreen.txt": {
      "type": "CONT"
    }
  }
}
```

### Property Names (Simplified)

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `pipeline.name` | string | ✓ | Pipeline name |
| `pipeline.description` | string | | Optional description |
| `pipeline.tags` | string[] | | Custom tags |
| `pipeline.notes` | string | | User data that overrides extraction |
| `pipeline.bias` | string | | Operator guidance for retrieval |
| `analysis.type` | string | ✓* | Analysis type code (e.g., "EAR", "VDD") |
| `analysis.name` | string | ✓* | Custom analysis name (if not using type) |
| `analysis.instructions` | string | ✓* | Custom AI instructions |
| `analysis.template` | string | ✓* | Custom Markdown template |
| `analysis.schema` | object | | Custom JSON schema |
| `manifest.{filename}.type` | string | | Source type code (e.g., "MEET", "INV") |
| `manifest.{filename}.notes` | string | | Optional metadata |

**Note:** Either `analysis.type` (seeded template) OR custom fields required

---

## Behavior Specification

### 1. Configuration Discovery
- API scans uploaded files for `analysis-config.json` (case-insensitive)
- If not found: **Error 400** - "Required file 'analysis-config.json' not found"
- If invalid JSON: **Error 400** - "Invalid JSON: {details}"
- If validation fails: **Error 400** - "{validation error}"

### 2. Analysis Type Resolution

**Option A: Seeded Template**
```json
{
  "analysis": {
    "type": "EAR"
  }
}
```
- Resolves to seeded `AnalysisType` with code "EAR"
- If not found: **Error 400** - "Analysis type 'EAR' not found. Available: COM, EAR, FIN, SEC, VDD"

**Option B: Custom Analysis**
```json
{
  "analysis": {
    "name": "Custom Financial Deep Dive",
    "instructions": "Extract revenue, EBITDA, cash flow...",
    "template": "# Financial Analysis\n{{revenue}}\n{{ebitda}}",
    "schema": { "properties": { "revenue": { "type": "string" } } }
  }
}
```
- Creates ephemeral `AnalysisType` for this pipeline only
- Not reusable across pipelines

### 3. Document Processing

**Manifest Specified:**
```json
{
  "manifest": {
    "meeting-notes.txt": { "type": "MEET" }
  }
}
```
- Document assigned `SourceType` with code "MEET"
- `ClassificationMethod = Manual`
- `Confidence = 1.0`

**Not in Manifest:**
```json
{
  "manifest": {
    // customer-bulletin.txt not listed
  }
}
```
- Auto-classified using heuristics (signal phrases, descriptor hints)
- `ClassificationMethod = Heuristic`
- `Confidence = 0.0-1.0` (based on match quality)

### 4. Error Handling

| Condition | Response |
|-----------|----------|
| Config file missing | 400 - "Required file 'analysis-config.json' not found" |
| Invalid JSON syntax | 400 - "Invalid JSON at line X, column Y" |
| Missing `pipeline.name` | 400 - "Pipeline name is required" |
| Missing analysis type | 400 - "Analysis must specify 'type' or custom definition" |
| Both type and custom | 400 - "Cannot specify both 'type' and custom definition" |
| Invalid analysis type | 400 - "Type 'XYZ' not found. Available: {codes}" |
| File in manifest but not uploaded | 400 - "File 'xyz.txt' in manifest but not uploaded" |
| Invalid source type | 400 - "Source type 'ABC' not found. Available: {codes}" |
| Zero documents | 400 - "At least one document required (excluding config)" |

---

## Response Format

```json
{
  "pipelineId": "01936b1c-7e8f-7890-abcd-ef1234567890",
  "pipelineName": "Enterprise Architecture Review Q4 2025",
  "analysisType": "EAR",
  "analysisTypeName": "Enterprise Architecture Review",
  "isCustomAnalysis": false,
  "jobId": "01936b1c-7e8f-7890-abcd-ef1234567891",
  "status": "Pending",
  "documents": [
    {
      "documentId": "01936b1c-...",
      "fileName": "meeting-notes.txt",
      "sourceType": "MEET",
      "sourceTypeName": "Meeting Summary/Notes",
      "method": "Manual",
      "confidence": 1.0,
      "inManifest": true
    },
    {
      "documentId": "01936b1d-...",
      "fileName": "customer-bulletin.txt",
      "sourceType": "RPT",
      "sourceTypeName": "Technical Report",
      "method": "Heuristic",
      "confidence": 0.65,
      "inManifest": false
    }
  ],
  "statistics": {
    "totalDocuments": 4,
    "manifestSpecified": 3,
    "autoClassified": 1
  }
}
```

---

## Auto-Classification Strategy

### Phase 1: Heuristic Matching (Current)

**Scoring Algorithm:**
1. **Signal Phrases** - +10 points per match
2. **Descriptor Hints** - +5 points per match
3. **Filename Match** - +15 points if filename contains source type name
4. **Confidence Calculation** - `min(score / 50, 0.95)`

**Example:**
```
Document: "meeting-notes.txt"
Content: "attendees: CIO Dana Wright, action items:..."

Scoring:
  MEET source type:
    - Signal: "attendees:" (+10)
    - Signal: "action items" (+10)
    - Filename: "meeting" in "meeting-notes.txt" (+15)
    Total: 35 points → 70% confidence
```

### Phase 2: AI-Based Classification (Future)

**Planned Enhancements:**
- Embedding-based similarity using vector search
- Few-shot learning from user corrections
- Confidence calibration based on historical accuracy
- Multi-model ensemble for critical documents

---

## Discovery Endpoints

### List Analysis Type Codes

```http
GET /api/type-codes/analysis

Response:
{
  "types": [
    {
      "code": "COM",
      "name": "Compliance Review",
      "description": "Compliance and regulatory assessment",
      "tags": ["compliance", "regulatory", "audit"]
    },
    {
      "code": "EAR",
      "name": "Enterprise Architecture Review",
      "description": "Enterprise architecture readiness assessment",
      "tags": ["enterprise", "architecture", "modernization"]
    }
  ]
}
```

### List Source Type Codes

```http
GET /api/type-codes/source

Response:
{
  "types": [
    {
      "code": "MEET",
      "name": "Meeting Summary/Notes",
      "description": "Meeting discussions, decisions, action items",
      "tags": ["meeting", "notes", "decisions"]
    }
  ]
}
```

---

## Benefits

### Developer Experience
- **11 API calls → 2 calls** (create + get deliverable)
- **~100 lines → ~40 lines** of automation code
- **Zero dynamic type creation** - use seeded templates
- **Self-documenting** - config file is the specification

### Version Control
- **Git-friendly** - commit `analysis-config.json` alongside code
- **Diff-friendly** - JSON changes are reviewable
- **Reproducible** - same files = same pipeline

### Team Collaboration
- **Shareable configs** - distribute standard templates
- **Progressive disclosure** - start simple, add complexity
- **Discoverable** - `GET /api/type-codes/{category}` shows options

### Production Readiness
- **Validation** - schema enforcement with clear errors
- **Telemetry** - tracks manual vs auto-classification
- **Extensible** - easy to add new config options
- **Backward compatible** - doesn't break existing APIs

---

## Migration Path

### Existing APIs (Unchanged)
- `POST /api/analysistypes` - Manual type creation
- `POST /api/pipelines` - Traditional pipeline creation
- `POST /api/pipelines/{id}/documents` - Add documents to existing pipeline

### New API (Additive)
- `POST /api/pipelines/create` - File-only pipeline creation
- `GET /api/type-codes/analysis` - Discovery endpoint
- `GET /api/type-codes/source` - Discovery endpoint

### Recommended Usage
- **Standard scenarios** → Use file-only API with seeded types (EAR, VDD, SEC, etc.)
- **Custom one-offs** → Use file-only API with custom analysis definition
- **Complex workflows** → Use existing APIs for fine-grained control

---

## Example: Scenario A Simplified

### Before (Original Script - ~100 lines)
```powershell
# Create 4 source types via AI
$sourceTypes = @()
foreach ($prompt in $sourcePrompts) {
    $created = Ensure-MeridianSourceTypeAi -Prompt $prompt.Prompt
    $sourceTypes += $created
}

# Create analysis type
$analysis = Invoke-MeridianRequest -Path '/api/analysistypes' -Method 'POST' -Body $analysisDefinition

# Create pipeline
$pipeline = New-MeridianPipeline -Definition $pipelineDefinition

# Upload 4 documents
foreach ($doc in $documents) {
    $upload = Upload-MeridianDocumentContent -PipelineId $pipelineId -FileName $doc.FileName -Content $doc.Content
    $job = Wait-MeridianJob -PipelineId $pipelineId -JobId $jobId
}

# Get deliverable
$deliverable = Get-MeridianDeliverable -PipelineId $pipelineId
```

### After (File-Only Script - ~40 lines)
```powershell
# Create config file
$config = @{
    pipeline = @{ name = "EA Review"; type = "EAR" }
    manifest = @{ "meeting-notes.txt" = @{ type = "MEET" } }
} | ConvertTo-Json
[IO.File]::WriteAllText("analysis-config.json", $config)

# Create document files
foreach ($doc in $documents) {
    [IO.File]::WriteAllText($doc.FileName, $doc.Content)
}

# Upload all files
$form = New-Object System.Net.Http.MultipartFormDataContent
$form.Add((Get-FileContent "analysis-config.json"), "files", "analysis-config.json")
foreach ($doc in $documents) {
    $form.Add((Get-FileContent $doc.FileName), "files", $doc.FileName)
}

$response = $client.PostAsync("$BaseUrl/api/pipelines/create", $form).Result
$result = $response.Content.ReadAsStringAsync().Result | ConvertFrom-Json

# Wait and get deliverable
$job = Wait-MeridianJob -PipelineId $result.pipelineId -JobId $result.jobId
$deliverable = Get-MeridianDeliverable -PipelineId $result.pipelineId
```

---

## Implementation Checklist

- [ ] Configuration models (`AnalysisConfig`, `PipelineConfig`, etc.)
- [ ] Validation logic with clear error messages
- [ ] `TypeCodeResolver` service for dynamic code lookup
- [ ] `DocumentClassifier` service for auto-classification
- [ ] `PipelineBootstrapService` orchestration
- [ ] `POST /api/pipelines/create` controller endpoint
- [ ] `GET /api/type-codes/{category}` discovery endpoints
- [ ] Unit tests for config parsing and validation
- [ ] Integration tests for end-to-end flow
- [ ] Scenario A file-only variant script
- [ ] Update API documentation

---

## Future Enhancements

### Phase 2: AI Classification
- Embedding-based document similarity
- User feedback loop for corrections
- Confidence calibration

### Phase 3: Advanced Manifesting
- Source type overrides per document
- Custom field extraction hints
- Priority/precedence settings

### Phase 4: Batch Operations
- Multiple pipeline configs in one payload
- Folder-based auto-organization
- Parallel pipeline creation

---

## Related Documents

- [DOCUMENT-CENTRIC-REFACTORING.md](./DOCUMENT-CENTRIC-REFACTORING.md) - Document-centric architecture
- Seeded Types:
  - `SeedData/AnalysisTypeSeedData.cs` - 5 analysis types (EAR, VDD, SEC, FIN, COM)
  - `SeedData/SourceTypeSeedData.cs` - 5 source types (MEET, INV, CONT, RPT, EMAIL)

---

## Glossary

| Term | Definition |
|------|------------|
| **Seeded Type** | Pre-configured template available via code (e.g., "EAR") |
| **Custom Type** | One-off analysis definition in config file |
| **Manifest** | Map of filenames to source types in config |
| **Auto-Classification** | Automatic source type detection for unlisted files |
| **Type Code** | Short identifier for type (e.g., "MEET", "EAR") |
| **Heuristic** | Rule-based classification using signal phrases |
| **Confidence** | 0.0-1.0 score indicating classification certainty |
