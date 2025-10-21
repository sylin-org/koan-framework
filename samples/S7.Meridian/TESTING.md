# S7.Meridian - Phase 1 Testing Guide

## Prerequisites

### 1. MongoDB
```bash
# Using Docker:
docker run -d -p 27017:27017 --name meridian-mongo mongo:latest

# Or use existing MongoDB instance and update appsettings.json
```

### 2. Ollama with granite3.3:8b
```bash
# Verify model is available:
ollama list | grep granite3.3

# If not available, pull it:
ollama pull granite3.3:8b

# Test it works:
ollama run granite3.3:8b "What is 2+2?"
```

## Running the Application

```bash
cd samples/S7.Meridian
dotnet run
```

The API will be available at `http://localhost:5000` (or as configured).

## Manual End-to-End Test

### Step 1: Create a Pipeline

**POST** `/api/pipelines`

```json
{
  "name": "Test Company Pipeline",
  "schemaJson": "{\"type\":\"object\",\"properties\":{\"companyName\":{\"type\":\"string\"},\"revenue\":{\"type\":\"number\"},\"employees\":{\"type\":\"number\"},\"founded\":{\"type\":\"string\"}}}",
  "templateMarkdown": "# Company Profile\n\n**Name:** {{companyName}}\n**Revenue:** ${{revenue}}\n**Employees:** {{employees}}\n**Founded:** {{founded}}",
  "biasNotes": ""
}
```

**Expected Response:**
```json
{
  "id": "<pipeline-id>",
  "name": "Test Company Pipeline",
  ...
}
```

Save the `id` for the next steps.

### Step 2: Create a Test Document

Create a file `test-company.txt`:

```text
Acme Corporation Annual Report

Acme Corporation is a leading technology company that was founded in 2010.
The company specializes in innovative software solutions.

Financial Highlights:
- Annual revenue reached $47.2 million in fiscal year 2023
- The company employs 150 talented professionals across three offices
- Revenue grew 23% year-over-year

Company Information:
Established: January 15, 2010
Headquarters: San Francisco, CA
CEO: Jane Smith
```

### Step 3: Upload the Document

**POST** `/api/pipelines/{pipeline-id}/documents`

**Headers:**
- `Content-Type: multipart/form-data`

**Body:**
- `file`: (select test-company.txt)

**Expected Response:**
```json
{
  "id": "<document-id>",
  "pipelineId": "<pipeline-id>",
  "filename": "test-company.txt",
  "status": "Pending"
}
```

### Step 4: Process the Document

**POST** `/api/pipelines/{pipeline-id}/process`

**Expected Response:**
```json
{
  "jobId": "<job-id>",
  "status": "Pending"
}
```

### Step 5: Monitor Job Status

**GET** `/api/jobs/{job-id}`

**Expected Response (Processing):**
```json
{
  "id": "<job-id>",
  "status": "InProgress",
  "startedAt": "2025-01-20T...",
  ...
}
```

**Expected Response (Complete):**
```json
{
  "id": "<job-id>",
  "status": "Completed",
  "finishedAt": "2025-01-20T...",
  ...
}
```

### Step 6: Verify Extracted Fields

**GET** `/api/pipelines/{pipeline-id}/fields`

**Expected Response:**
```json
[
  {
    "fieldPath": "$.companyName",
    "valueJson": "Acme Corporation",
    "confidence": 0.95,
    "evidence": {
      "passageId": "<passage-id>",
      "originalText": "Acme Corporation Annual Report\n\nAcme Corporation is a leading...",
      "span": { "start": 0, "end": 16 }
    }
  },
  {
    "fieldPath": "$.revenue",
    "valueJson": "47.2",
    "confidence": 0.92,
    "evidence": {
      "passageId": "<passage-id>",
      "originalText": "Annual revenue reached $47.2 million in fiscal year 2023",
      "span": { "start": 23, "end": 28 }
    }
  },
  {
    "fieldPath": "$.employees",
    "valueJson": "150",
    "confidence": 0.88,
    "evidence": {
      "passageId": "<passage-id>",
      "originalText": "The company employs 150 talented professionals...",
      "span": { "start": 20, "end": 23 }
    }
  },
  {
    "fieldPath": "$.founded",
    "valueJson": "January 15, 2010",
    "confidence": 0.90,
    "evidence": {
      "passageId": "<passage-id>",
      "originalText": "Established: January 15, 2010",
      "span": { "start": 13, "end": 30 }
    }
  }
]
```

### Step 7: Get the Deliverable

**GET** `/api/pipelines/{pipeline-id}/deliverables/latest`

**Expected Response:**
```json
{
  "markdown": "# Company Profile\n\n**Name:** Acme Corporation\n**Revenue:** $47.2\n**Employees:** 150\n**Founded:** January 15, 2010",
  "renderedAt": "2025-01-20T...",
  ...
}
```

## Verification Checklist

### Phase 1 Completion Criteria

- [ ] **Build succeeds** with no warnings
- [ ] **Ollama integration** works (granite3.3:8b responds)
- [ ] **Upload & Extract** test document successfully processed
- [ ] **AI extracts fields** with confidence scores (not hardcoded)
- [ ] **Cache performance**:
  - First run: 0 hits, N misses
  - Second run (same doc): N hits, 0 misses
  - Cache hit rate >80% on second identical document
- [ ] **Logs show RAG stages**:
  - Query generation
  - Passage retrieval
  - Token budget enforcement
  - LLM extraction
  - Span localization
- [ ] **Evidence tracking** works (passages, spans, confidence)

### Expected Log Output

**First Run (cache misses):**
```
[INF] Extracting 4 fields for pipeline <id>
[DBG] Embedding query: Find information about company name.
[DBG] Embedding cache MISS for passage <id>
[DBG] Embedding cache MISS for passage <id>
[INF] Embedding cache: 0 hits, 10 misses (10 total)
[INF] Retrieved 5 passages for query: Find information about company name.
[DBG] Token budget: 1247 tokens (limit: 2000), 3 passages included
[DBG] Extraction prompt hash for $.companyName: a3f2d8e1b4c7
[DBG] Calling AI for field $.companyName with model granite3.3:8b
[DBG] Span located via exact match
[INF] Extracted field $.companyName: "Acme Corporation" (confidence: 95%)
```

**Second Run (cache hits):**
```
[INF] Extracting 4 fields for pipeline <id>
[DBG] Embedding cache HIT for passage <id>
[DBG] Embedding cache HIT for passage <id>
[INF] Embedding cache: 10 hits, 0 misses (10 total)
[INF] Retrieved 5 passages for query: Find information about company name.
```

## Performance Metrics to Verify

1. **Cache Hit Rate**: >80% on second run
2. **Extraction Confidence**: All fields >70% confidence
3. **Processing Time**: <30 seconds for 1-page document
4. **Memory Usage**: <500MB for single pipeline

## Troubleshooting

### Ollama Not Responding
```bash
# Check Ollama is running:
ollama list

# Restart Ollama service if needed
```

### MongoDB Connection Failed
```bash
# Check MongoDB is running:
docker ps | grep mongo

# Check connection string in appsettings.json
```

### No Fields Extracted
- Check logs for LLM response parsing errors
- Verify schema JSON is valid
- Check if passages were indexed (look for "Upserted N passages" log)

### Low Confidence Scores
- Review bias notes in pipeline configuration
- Check if text quality is good (OCR artifacts can reduce confidence)
- Verify field names match document terminology

## Next Steps (Phase 2)

After Phase 1 verification, proceed to:
- **Phase 2**: Merge policies (highestConfidence → intelligent merging)
- **Phase 3**: Document classification
- **Phase 4**: Field overrides and incremental refresh
- **Phase 5**: Production hardening
