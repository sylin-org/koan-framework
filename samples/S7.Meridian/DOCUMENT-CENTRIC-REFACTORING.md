# Document-Centric Architecture Refactoring

**Status:** Phases 1–3 shipped (DocumentIds, decoupled SourceDocument, shared Passage cache); admin UI + automation backlog active  
**Created:** October 23, 2025  
**Last Reviewed:** October 23, 2025  
**Target:** Meridian S7 Sample Application  
**Estimated Effort:** Remaining follow-ups 1–2 days (admin UI, test cleanup)

**Contract**

- Inputs: `DocumentPipeline`, `ProcessingJob`, ingestion endpoints, prompt builders, and diagnostics inside `samples/S7.Meridian`.
- Outputs: Reusable `SourceDocument` artifacts, shared `Passage` cache, OrganizationProfile context injection, and pipeline-level document references.
- Error Modes: Pipelines with stale `DocumentIds`, orphaned documents after deletion, regression tests relying on `SourceDocument.PipelineId`, missing OrganizationProfile binding in prompts.
- Acceptance Criteria: Multi-pipeline reuse validated with single-text extraction, passages resolved by document ID, prompts enriched with organization globals, regression suite updated to drop deprecated fields.

---

## Executive Summary

### Problem Statement

Current Meridian architecture creates **1:1 binding** between documents and pipelines via `SourceDocument.PipelineId`. This causes:

- **Duplicate storage** - Same file uploaded to 2 pipelines = 2 complete copies
- **Duplicate processing** - Text extraction, classification, chunking, embedding repeated per pipeline
- **No document reuse** - Cannot share documents across pipelines
- **Expensive pipeline cloning** - Must duplicate all documents

### Proposed Solution

**Documents own processing artifacts** (text, chunks, embeddings, classification), **pipelines own semantic interpretations** (schema-specific extractions). Documents become reusable information containers referenced by multiple pipelines.

### Expected Benefits

- **50-80% cost reduction** for multi-pipeline scenarios
- **Near-zero cost pipeline cloning** (copy config + reference documents)
- **Cleaner separation of concerns** (processing layer vs. interpretation layer)
- **Document versioning centralized** (update once, all pipelines see changes)

### Design Decision

Use **simple `List<string> DocumentIds`** on `DocumentPipeline` rather than separate join table entity:

- Simpler implementation (follows existing `ProcessingJob` pattern)
- Matches Meridian's scale (10-500 documents per pipeline, not thousands)
- Can migrate to join table later if needed (YAGNI principle)

### Implementation Snapshot (2025-10-23)

- `Models/DocumentPipeline.cs` now exposes `DocumentIds`, `OrganizationProfileId`, and helper loaders (`LoadDocumentsAsync`, `LoadPassagesAsync`, `LoadOrganizationProfileAsync`).
- `Models/SourceDocument.cs` no longer carries `PipelineId`; ingestion, processing, and extraction paths rely exclusively on the pipeline document list.
- `Models/Passage.cs` no longer stores `PipelineId`; retrieval filters by `pipeline.DocumentIds` throughout `FieldExtractor`, `PipelineProcessor`, and `PassageIndexer`.
- Organization globals (`Models/OrganizationProfile.cs`) exist and are injected into prompt assembly via `FieldExtractor.BuildInstructionBlock`.
- Controllers (`DocumentsController`, `PipelineNotesController`) and services (`DocumentIngestionService`, `JobCoordinator`, `PipelineProcessor`) use the new helpers and avoid pipeline counters.
- Regression coverage exists for document reuse in `tests/S7.Meridian.Tests`, but several fixtures still reference the removed `SourceDocument.PipelineId` property (see Gap Analysis).

---

## Architecture Overview

### Current Architecture (Pipeline-Centric)

```
Pipeline A
├── SourceDocument 1 (PipelineId=A)
│   ├── ExtractedText
│   ├── Passages (PipelineId=A)
│   └── Embeddings
├── SourceDocument 2 (PipelineId=A)
│   ├── ExtractedText
│   ├── Passages (PipelineId=A)
│   └── Embeddings
└── ExtractedFields (PipelineId=A)

Pipeline B (wants same documents)
├── SourceDocument 1 DUPLICATE (PipelineId=B)  ← Full duplication!
│   ├── ExtractedText (duplicate)
│   ├── Passages (PipelineId=B, duplicate)
│   └── Embeddings (duplicate)
└── ExtractedFields (PipelineId=B)
```

**Problems:**

- Same physical document stored twice
- Text extraction repeated (expensive)
- Classification repeated (LLM call)
- Chunking repeated
- Embeddings repeated (most expensive operation)
- Cannot share processing artifacts

### Proposed Architecture (Document-Centric)

```
Global Document Store
├── SourceDocument 1 (no PipelineId)
│   ├── ExtractedText (once)
│   ├── Passages (once)
│   └── Embeddings (cached, once)
└── SourceDocument 2 (no PipelineId)
    ├── ExtractedText (once)
    ├── Passages (once)
    └── Embeddings (cached, once)

Pipeline A
├── DocumentIds: [Doc1, Doc2]  ← References, not copies
└── ExtractedFields (PipelineId=A, schema-specific)

Pipeline B
├── DocumentIds: [Doc1, Doc2]  ← Same documents, no duplication!
└── ExtractedFields (PipelineId=B, different schema)
```

**Benefits:**

- Documents processed once, reused by multiple pipelines
- Pipelines only store schema-specific extractions
- Pipeline cloning = copy config + copy DocumentIds list (instant)

---

## Gap Analysis

- **Virtual document creation still sets `SourceDocument.PipelineId`** (`Services/PipelineProcessor.cs`, `CreateVirtualDocumentFromNotesAsync`). The model no longer exposes that property, so the helper must stop assigning it and rely on `pipeline.AttachDocument(saved.Id)`.
- **Regression suites reference removed property** (`tests/S7.Meridian.Tests/Integration/PipelineE2ETests.cs`, `JobCoordinatorTests.cs`) and need to be updated to populate documents via `pipeline.AttachDocument` helpers.
- **Admin surface for `OrganizationProfile` missing.** The model exists and prompts consume it, but no UI or API exposes CRUD for organizational globals.
- **Document reuse telemetry absent.** We do not log when a document participates in multiple pipelines; consider adding a `RunLog` metadata field when reuse occurs.
- **Deduplication/backfill scripts not implemented.** Content-hash reuse remains backlog; cloning automation endpoints remain unshipped.

---

## Ownership Model

### Documents Own (Processing Artifacts - Schema-Agnostic)

**Physical Storage:**

- ✅ `OriginalFileName` - File metadata
- ✅ `StorageKey` - Blob storage reference
- ✅ `ContentHash` - **NEW** - For deduplication
- ✅ `MediaType`, `Size` - File properties

**Text Extraction:**

- ✅ `ExtractedText` - Parsed text from PDF/DOCX/etc.
- ✅ `TextHash` - Content hash for change detection
- ✅ `PageCount` - Document structure metadata
- ✅ `Status` - Processing readiness (Pending → Extracted → Classified → Indexed)

**Classification:**

- ✅ `SourceType` - Document type ("Financial Report", "Legal Contract")
- ✅ `ClassificationConfidence` - Classifier certainty score
- ✅ `ClassificationMethod` - How it was classified
- ✅ `ClassificationReason` - Classifier explanation

**Chunking (Passage entities):**

- ✅ `Passage.SourceDocumentId` - Link to parent document (no PipelineId)
- ✅ `Passage.Text` - Chunk text for retrieval
- ✅ `Passage.TextHash` - Chunk content hash
- ✅ `Passage.SequenceNumber`, `PageNumber`, `Section` - Position metadata

**Embeddings:**

- ✅ Cached by `Passage.TextHash` (already implemented)
- ✅ Reusable across all pipelines automatically

### Pipelines Own (Semantic Interpretations - Schema-Specific)

**Schema Definition:**

- ✅ `SchemaJson` - Output structure (fields to extract)
- ✅ `TemplateMarkdown` - Deliverable format

**Extraction Context:**

- ✅ `AnalysisInstructions` - Domain-specific extraction guidance
- ✅ `BiasNotes` - Operator hints for retrieval focus
- ✅ `AuthoritativeNotes` - Hard overrides (creates virtual document)

**Extracted Values:**

- ✅ `ExtractedField.PipelineId` - **KEEP** - Extraction is schema-dependent
- ✅ `ExtractedField.FieldPath` - Schema field name
- ✅ `ExtractedField.ValueJson` - Extracted value
- ✅ Links to `SourceDocumentId`, `PassageId` for provenance

**Deliverables:**

- ✅ `Deliverable.PipelineId` - Final output
- ✅ Confidence scores, quality metrics

**Document References:**

- ✅ `DocumentIds` - **NEW** - List of document IDs used by this pipeline

---

### Organizational Globals (New)

**Entity:** `OrganizationProfile : Entity<OrganizationProfile>`

- `ScopeClassification` - data boundary tag
- `RegulatoryRegime` - controlling statutes (HIPAA, SOX, etc.)
- `LineOfBusiness` - business unit anchor
- `Department` - accountable team name
- `PrimaryStakeholders` - role → contacts map

**Prompt Contract:** always inject `OrganizationProfile` fields when assembling document-oriented prompts; keep formatting terse to avoid drift.

**UI Contract:** expose a lightweight list/detail surface so operators can view and edit organizational globals without touching configuration files.

---

## Implementation Status by Phase

### Phase 1 – DocumentIds List (Complete)

**Shipped**

- `Models/DocumentPipeline.cs` defines `DocumentIds`, `OrganizationProfileId`, attach/remove helpers, and async loaders for documents, passages, and organization profiles.
- `Services/DocumentIngestionService.cs` persists uploaded documents and appends their IDs to the owning pipeline without mutating counters.
- Controllers (`DocumentsController`, `PipelineNotesController`) and orchestration services (`JobCoordinator`, `PipelineProcessor`) now hydrate via the new helpers.
- `Models/OrganizationProfile.cs` exists and `FieldExtractor` injects organizational context into prompts.

**Outstanding**

- Admin list/detail UI (and REST surface, if needed) for maintaining `OrganizationProfile` entities remains unimplemented.
- Pipeline diagnostics do not yet warn when `DocumentIds` references missing or deleted documents.

**Verification**

- Manual ingestion/processing runs (`scripts/phase4.5`) confirm pipelines persist document IDs.
- `tests/S7.Meridian.Tests/JobCoordinatorTests` cover reuse paths but still assume `SourceDocument.PipelineId`; refactor these fixtures to attach documents via pipeline helpers.

---

### Phase 2 – Remove SourceDocument.PipelineId (Complete, cleanup pending)

**Shipped**

- `Models/SourceDocument.cs` no longer exposes `PipelineId`; ingestion, processing, and controller paths rely solely on pipeline document lists.
- `ProcessingJob` documents are merged through `pipeline.AttachDocuments`, and controllers fetch via `LoadDocumentsAsync`.

**Gap**

- `Services/PipelineProcessor.cs` (`CreateVirtualDocumentFromNotesAsync`) still assigns the removed property. Drop the setter and ensure the virtual document is attached only through `pipeline.AttachDocument`.
- Integration and orchestration tests instantiate `SourceDocument` with `PipelineId`; update them to persist, attach, and process documents via the pipeline list.

**Verification**

- Multi-pipeline reuse works manually: the second pipeline attaches existing document IDs and skips redundant extraction/embedding when hashes match.

---

### Phase 3 – Remove Passage.PipelineId (Complete)

**Shipped**

- `Models/Passage.cs` retains only `SourceDocumentId` as foreign key.
- `PassageChunker`, `FieldExtractor`, `PipelineProcessor`, and `PassageIndexer` filter passages by `pipeline.DocumentIds` and reuse cached embeddings keyed on `Passage.TextHash`.

**Outstanding**

- Observability: surface a warning when reuse returns zero passages (e.g., document deleted) so operators can remediate quickly.

**Verification**

- Retrieval and extraction succeed with shared passages; `RunLog` entries confirm reuse without duplicate chunking.

---

## Progressive TODOs

- [ ] Strip the `SourceDocument.PipelineId` assignment in `PipelineProcessor.CreateVirtualDocumentFromNotesAsync` and rely on `pipeline.AttachDocument(saved.Id)` exclusively.
- [ ] Refactor `tests/S7.Meridian.Tests/Integration/PipelineE2ETests.cs` and related fixtures to build pipelines by attaching saved document IDs instead of setting properties directly.
- [ ] Implement an admin/API surface for maintaining `OrganizationProfile` entities (sample UI namespace: `Pages/Globals/OrganizationProfile`).
- [ ] Emit telemetry (e.g., `RunLog` metadata) when a document participates in multiple pipelines to quantify reuse benefits.
- [ ] Add validation that flags `DocumentPipeline.DocumentIds` references pointing to missing `SourceDocument` rows.
- [ ] Keep content-hash deduplication on deck; wire ingestion metrics so we can trigger the enhancement when duplicate uploads exceed agreed thresholds.

### Optional Enhancements (Post-Core Refactor)

The three phases above deliver document reuse, pipeline cloning (by copying `DocumentIds`), and retrieval parity. Additional features remain valuable but can ship later if and when real requirements surface.

**Content-Hash Deduplication**

- Add `ContentHash` only when duplicate uploads create measurable storage or processing pressure.
- Implementation mirrors the outline in earlier drafts—compute hash during ingestion, reuse existing `SourceDocument` when hashes match.

**First-Class Pipeline Cloning Support**

- Pipelines can already be cloned by copying configuration and `DocumentIds` in caller code.
- Add REST/UI affordances only if teams need guided automation rather than simple reuse in custom workflows.

**Join Table Migration**

- If pipelines begin exceeding ~1000 documents and list performance degrades, migrate to `PipelineDocument` join table as a follow-up change.

These enhancements should remain out of scope for the initial refactor so the core change set stays minimal.

---

## Query Migration Guide

### Pattern 1: Get Documents for Pipeline

**BEFORE:**

```csharp
var documents = await SourceDocument.Query(d => d.PipelineId == pipelineId, ct);
```

**AFTER:**

```csharp
var pipeline = await DocumentPipeline.Get(pipelineId, ct);
var documents = await pipeline.LoadDocumentsAsync(ct);
```

**Helper Method (Recommended):**

```csharp
// Add to DocumentPipeline model
public async Task<List<SourceDocument>> LoadDocumentsAsync(CancellationToken ct)
{
    if (DocumentIds.Count == 0)
    {
        return new List<SourceDocument>();
    }

    return await SourceDocument.GetManyAsync(DocumentIds, ct);
}

// Usage
var pipeline = await DocumentPipeline.Get(pipelineId, ct)
    ?? throw new InvalidOperationException($"Pipeline {pipelineId} not found.");
var documents = await pipeline.LoadDocumentsAsync(ct);
```

### Pattern 2: Get Passages for Pipeline

**BEFORE:**

```csharp
var passages = await Passage.Query(p => p.PipelineId == pipelineId, ct);
```

**AFTER:**

```csharp
var pipeline = await DocumentPipeline.Get(pipelineId, ct);
var passages = await pipeline.LoadPassagesAsync(ct);
```

**Helper Method (Recommended):**

```csharp
// Add to DocumentPipeline model (optional)
public async Task<List<Passage>> LoadPassagesAsync(CancellationToken ct)
{
    if (DocumentIds.Count == 0)
    {
        return new List<Passage>();
    }

    return await Passage.Query(p => DocumentIds.Contains(p.SourceDocumentId), ct);
}
```

### Pattern 3: Filter Virtual Documents

**BEFORE:**

```csharp
var realDocs = await SourceDocument.Query(
    d => d.PipelineId == pipelineId && !d.IsVirtual, ct);
```

**AFTER:**

```csharp
var pipeline = await DocumentPipeline.Get(pipelineId, ct);
var allDocs = await pipeline.LoadDocumentsAsync(ct);
var realDocs = allDocs.Where(d => !d.IsVirtual).ToList();
```

---

## File Modification Checklist

### Phase 1 Files (DocumentIds List)

- [ ] **Models/DocumentPipeline.cs** - Add `DocumentIds` property
- [ ] **Models/DocumentPipeline.cs** - Store `OrganizationProfileId` (string) to retrieve globals
- [ ] **Models/SourceDocument.cs** - Mark `PipelineId` as `[Obsolete]` (keep for validation)
- [ ] **Services/DocumentIngestionService.cs** - Populate `pipeline.DocumentIds` instead of `document.PipelineId`
- [ ] **Services/PipelineProcessor.cs** - Update document queries (line ~93-96, ~212-214) and remove pipeline counter mutations
- [ ] **Services/FieldExtractor.cs** - Update document query (line ~224)
- [ ] **Services/JobCoordinator.cs** - Ensure uses `pipeline.DocumentIds` (already correct)
- [ ] **Controllers/DocumentsController.cs** - Update GetDocuments query (line ~29)
- [ ] **Controllers/PipelineRefreshController.cs** - Update document query (line ~36)
- [ ] **Controllers/PipelineNotesController.cs** - Update document queries (line ~90, ~140)
- [ ] **Models/OrganizationProfile.cs** - New entity holding global parameters
- [ ] **Prompt assembly** (builder/service TBD) - Inject OrganizationProfile globals into every document prompt
- [ ] **UI module** (e.g., `Pages/Globals/OrganizationProfile`) - Implement list/detail CRUD for organization globals

### Phase 2 Files (Remove SourceDocument.PipelineId)

- [ ] **Models/SourceDocument.cs** - Remove `PipelineId` property entirely
- [ ] **Services/DocumentIngestionService.cs** - Append IDs to pipeline and persist once per batch
- [ ] **Services/PipelineProcessor.cs** - Ensure virtual notes appenders register document IDs via `IsVirtual`
- [ ] **Controllers/DocumentsController.cs** - Verify reuse paths manipulate `DocumentIds`

### Phase 3 Files (Remove Passage.PipelineId)

- [ ] **Models/Passage.cs** - Remove `PipelineId` property
- [ ] **Services/PassageChunker.cs** - Remove PipelineId assignment (line ~35)
- [ ] **Services/PassageIndexer.cs** - Remove pipelineId from vector metadata (line ~90)
- [ ] **Services/PipelineProcessor.cs** - Update passage query (line ~232)
- [ ] **Services/FieldExtractor.cs** - Update passage and source-type queries to use pipeline helper (line ~224, ~430, ~441)

- Optional backlog items (implement when needed)
  - [ ] **Services/DocumentIngestionService.cs** - Add content-hash deduplication
  - [ ] **Controllers/PipelinesController.cs** - Add clone endpoint / automation helpers
  - [ ] **Frontend** - Add clone UI (if applicable)

### Test Files to Update

- [ ] **tests/.../Integration/PipelineE2ETests.cs** - Update document association tests
- [ ] **tests/.../DocumentIngestionTests.cs** - Add deduplication tests
- [ ] **tests/.../FieldExtractorTests.cs** - Update passage query tests
- [ ] **scripts/phase4.5/phase4.5-common.ps1** - Update PowerShell ingestion logic

---

## Testing Strategy

### Unit Tests

**Phase 1 Tests:**

```csharp
[TestMethod]
public async Task DocumentIngestion_PopulatesPipelineDocumentIds()
{
    var pipeline = new DocumentPipeline { Name = "Test" };
    await pipeline.Save();

    var doc = await _ingestion.IngestAsync(pipeline.Id, testFile, ct);

    var reloaded = await DocumentPipeline.Get(pipeline.Id);
    Assert.IsTrue(reloaded.DocumentIds.Contains(doc.Id));
}

[TestMethod]
public async Task GetDocumentsForPipeline_ReturnsLinkedDocuments()
{
    var pipeline = new DocumentPipeline { DocumentIds = { docId1, docId2 } };
    await pipeline.Save();

    var docs = await SourceDocument.GetForPipelineAsync(pipeline.Id);

    Assert.AreEqual(2, docs.Count);
}
```

**Phase 2 Tests:**

```csharp
[TestMethod]
public async Task VirtualNotesDocuments_AreMarkedIsVirtual()
{
    var pipeline = await DocumentPipeline.Get(pipelineId);
    pipeline.AuthoritativeNotes = "Always use $2.5M";
    await pipeline.Save();

    await _processor.ProcessAsync(BuildJobFor(pipeline), ct);

    var docs = await SourceDocument.GetForPipelineAsync(pipeline.Id, ct);
    Assert.IsTrue(docs.Any(d => d.IsVirtual));
}

[TestMethod]
public async Task ManualDocumentReuse_AddsExistingId()
{
    var doc = await _ingestion.IngestAsync(pipelineA.Id, testFile, ct);

    var pipelineB = await DocumentPipeline.Get(pipelineBId);
    pipelineB.DocumentIds.Add(doc.Id);
    await pipelineB.Save();

    var reloaded = await DocumentPipeline.Get(pipelineBId);
    Assert.IsTrue(reloaded.DocumentIds.Contains(doc.Id));
}
```

**Phase 3 Tests:**

```csharp
[TestMethod]
public async Task Passages_SharedAcrossPipelines()
{
    var pipelineA = CreatePipeline(docId);
    var pipelineB = CreatePipeline(docId); // Same document

    await _processor.ProcessAsync(pipelineA);
    var passagesA = await Passage.GetForPipelineAsync(pipelineA.Id);

    await _processor.ProcessAsync(pipelineB);
    var passagesB = await Passage.GetForPipelineAsync(pipelineB.Id);

    // Same document = same passages
    Assert.AreEqual(passagesA.Count, passagesB.Count);
    CollectionAssert.AreEqual(passagesA.Select(p => p.Id).ToList(),
                             passagesB.Select(p => p.Id).ToList());
}
```

### Integration Tests

**End-to-End Workflow:**

1. Create pipeline A
2. Upload documents to A
3. Process A → Verify extractions
4. Create pipeline B
5. Link same documents to B
6. Process B → Verify:
   - Same documents used
   - Same passages used
   - Different extractions (schema-specific)
   - No duplicate storage

**Performance Comparison:**

```csharp
[TestMethod]
public async Task DocumentReuse_ReducesProcessingCost()
{
    var documents = await UploadDocuments(10);

    // Pipeline A: Full processing
    var startA = DateTime.UtcNow;
    var pipelineA = await ProcessPipeline(documents);
    var costA = (DateTime.UtcNow - startA).TotalSeconds;

    // Pipeline B: Reuse documents (should be faster)
    var startB = DateTime.UtcNow;
    var pipelineB = await ProcessPipeline(documents); // Same documents
    var costB = (DateTime.UtcNow - startB).TotalSeconds;

    Assert.IsTrue(costB < costA * 0.5, "Reuse should be 50%+ faster");
}
```

---

## Rollback Strategy

### Phase 1 Rollback

**Keep `SourceDocument.PipelineId` as deprecated during Phase 1**

```csharp
[Obsolete("Use pipeline.DocumentIds instead")]
public string PipelineId { get; set; } = string.Empty;
```

**Validation script:**

```csharp
// Run after Phase 1 implementation to verify parallel paths match
foreach (var pipeline in await DocumentPipeline.All())
{
    // Old path
    var docsViaOldPath = await SourceDocument.Query(d => d.PipelineId == pipeline.Id);

    // New path
    var docsViaNewPath = await SourceDocument.GetManyAsync(pipeline.DocumentIds);

    if (docsViaOldPath.Count != docsViaNewPath.Count)
    {
        Logger.LogError("Mismatch for pipeline {PipelineId}: old={OldCount}, new={NewCount}",
            pipeline.Id, docsViaOldPath.Count, docsViaNewPath.Count);
    }
}
```

### Phase 2 Rollback

**If issues detected after removing PipelineId:**

1. Re-add `PipelineId` property to `SourceDocument`
2. Run migration script to populate from `DocumentPipeline.DocumentIds`:

```csharp
foreach (var pipeline in await DocumentPipeline.All())
{
    foreach (var docId in pipeline.DocumentIds)
    {
        var doc = await SourceDocument.Get(docId);
        if (doc != null && string.IsNullOrEmpty(doc.PipelineId))
        {
            doc.PipelineId = pipeline.Id;
            await doc.Save();
        }
    }
}
```

### Phase 3 Rollback

**Most risky - re-add Passage.PipelineId if retrieval breaks:**

```csharp
// Last-resort rollback: duplicate passages per pipeline by re-running chunker
foreach (var pipeline in await DocumentPipeline.All())
{
    await _chunker.RebuildAsync(pipeline.DocumentIds, ct);
}
```

---

## Edge Cases & Considerations

### Virtual Documents from Authoritative Notes

**Problem:** Virtual documents are pipeline-specific overrides, shouldn't be reused.

**Solution:**

```csharp
// Virtual documents already flagged via IsVirtual
var clonedDocIds = original.DocumentIds
    .Where(id => {
        var doc = await SourceDocument.Get(id);
        return doc != null && !doc.IsVirtual; // Skip Authoritative Notes copies
    })
    .ToList();
```

### Document Updates & Cache Invalidation

**Problem:** If document content changes, all pipelines using it should be notified.

**Solution (Future Enhancement):**

```csharp
public async Task UpdateDocumentAsync(string documentId, Stream newContent, CancellationToken ct)
{
    var doc = await SourceDocument.Get(documentId);
    var oldHash = doc.ContentHash; // Populate only if deduplication enhancement implemented

    // Reprocess document
    var newHash = await ReprocessDocumentAsync(doc, newContent, ct);

    if (oldHash != newHash)
    {
        // Find all pipelines using this document
        var allPipelines = await DocumentPipeline.All(ct);
        var affectedPipelines = allPipelines.Where(p => p.DocumentIds.Contains(documentId));

        // Mark pipelines for refresh
        foreach (var pipeline in affectedPipelines)
        {
            await _notifications.NotifyPipelineStale(pipeline.Id, documentId);
        }
    }
}
```

### Concurrent Document Additions

**Problem:** Two requests add documents to same pipeline simultaneously.

**Mitigation:**

- Koan's `Entity<T>` handles optimistic concurrency (versioning)
- Use `HashSet<string>` for DocumentIds to prevent duplicates:

```csharp
var docIdSet = new HashSet<string>(pipeline.DocumentIds);
docIdSet.Add(newDocumentId);
pipeline.DocumentIds = docIdSet.ToList();
await pipeline.Save(ct);
```

### Large Document Lists Performance

**Problem:** Pipelines with 1000+ documents might have slow list operations.

**Threshold:** Monitor `pipeline.DocumentIds.Count`:

- < 500 documents: Simple list is fine
- 500-1000 documents: Watch query performance
- > 1000 documents: Consider migrating to join table

**Migration trigger:**

```csharp
if (pipeline.DocumentIds.Count > 1000)
{
    Logger.LogWarning("Pipeline {PipelineId} has {Count} documents, consider join table migration.",
        pipeline.Id, pipeline.DocumentIds.Count);
}
```

### Classification Context

**Question:** Does classification need pipeline context?

**Analysis:** Current `DocumentClassifier` uses:

- Document text
- Global SourceType catalog
- Embeddings for similarity

**Verdict:** ✅ Classification is document-intrinsic (no pipeline context needed)

---

## Performance Expectations

### Before Refactoring (Current)

```
Scenario: 3 pipelines × 50 documents

Processing Cost:
- Pipeline A: 50 docs × ($0.05 extract + $0.10 embed) = $7.50
- Pipeline B: 50 docs × ($0.05 extract + $0.10 embed) = $7.50
- Pipeline C: 50 docs × ($0.05 extract + $0.10 embed) = $7.50
Total: $22.50

Storage Cost:
- 3 × 50 documents × 5MB avg = 750MB

Time:
- Pipeline A: 50 docs × 30sec = 25 minutes
- Pipeline B: 50 docs × 30sec = 25 minutes
- Pipeline C: 50 docs × 30sec = 25 minutes
Total: 75 minutes
```

### After Refactoring (Proposed)

```
Scenario: 3 pipelines × 50 shared documents

Processing Cost:
- Documents (once): 50 docs × ($0.05 extract + $0.10 embed) = $7.50
- Pipeline A: 50 docs × $0.02 extract only = $1.00
- Pipeline B: 50 docs × $0.02 extract only = $1.00
- Pipeline C: 50 docs × $0.02 extract only = $1.00
Total: $10.50 (53% savings)

Storage Cost:
- 1 × 50 documents × 5MB avg = 250MB (67% savings)

Time:
- Documents (once): 50 docs × 30sec = 25 minutes
- Pipeline A: 50 docs × 5sec = 4 minutes
- Pipeline B: 50 docs × 5sec = 4 minutes
- Pipeline C: 50 docs × 5sec = 4 minutes
Total: 37 minutes (51% faster)
```

### Pipeline Cloning Performance

```
Before: Clone with 100 documents
- Create pipeline record: 0.1s
- Duplicate 100 documents: 30s (upload, extract, classify, chunk, embed)
- Total: ~30 seconds

After: Clone with 100 documents
- Create pipeline record: 0.1s
- Copy DocumentIds list: 0.01s
- Total: ~0.1 seconds (300x faster)
```

---

## Success Metrics

### Phase 1 Success Criteria

- ✅ All document queries migrated to use `pipeline.DocumentIds`
- ✅ `pipeline.DocumentIds` correctly populated on upload
- ✅ Existing tests pass (backward compatibility)
- ✅ Validation script shows old/new paths match

### Phase 2 Success Criteria

- ✅ `SourceDocument.PipelineId` removed from model
- ✅ Content-hash deduplication working (same file = same document)
- ✅ Document linking API functional
- ✅ No breaking changes to extraction pipeline

### Phase 3 Success Criteria

- ✅ `Passage.PipelineId` removed from model
- ✅ Passages shared across pipelines
- ✅ Retrieval/extraction quality unchanged
- ✅ Performance improvement measurable

### Phase 4 Success Criteria

- ✅ Upload same file twice → Single storage record
- ✅ Upload same file to different pipelines → Document reused
- ✅ Storage costs reduced (measure before/after)

### Phase 5 Success Criteria

- ✅ Pipeline cloning functional via API
- ✅ Cloned pipelines share documents
- ✅ Independent extractions per pipeline
- ✅ Cloning completes in < 1 second

---

## Future Enhancements

### Join Table Migration (If Needed at Scale)

**Trigger:** Pipelines exceed 1000 documents regularly

**Implementation:**

1. Create `PipelineDocument` entity
2. Migration script: Populate from `DocumentPipeline.DocumentIds`
3. Add query helpers to abstract join logic
4. Deprecate `DocumentIds` list (keep for backward compat)

### Document Versioning

**Feature:** Track document versions, notify affected pipelines

**Implementation:**

```csharp
public sealed class SourceDocument : Entity<SourceDocument>
{
    public int Version { get; set; } = 1;
    public DateTime LastModified { get; set; }
    public string? PreviousVersionId { get; set; }
}

// When document updated
var affectedPipelines = await FindPipelinesUsingDocument(documentId);
foreach (var pipeline in affectedPipelines)
{
    await NotifyStaleData(pipeline.Id, documentId);
}
```

### Cross-Pipeline Document Analytics

**Feature:** Show document usage statistics

**Queries enabled:**

```csharp
// Which pipelines use this document?
var usage = await DocumentPipeline.Query(
    p => p.DocumentIds.Contains(documentId), ct);

// Most frequently used documents
var allPipelines = await DocumentPipeline.All(ct);
var docCounts = allPipelines
    .SelectMany(p => p.DocumentIds)
    .GroupBy(id => id)
    .OrderByDescending(g => g.Count());
```

### Chunking Strategy Variations

**Challenge:** Different pipelines might need different chunk sizes

**Solution:**

```csharp
public sealed class DocumentPipeline : Entity<DocumentPipeline>
{
    public string ChunkingProfileId { get; set; } = "default";
}

public sealed class Passage : Entity<Passage>
{
    public string ChunkingProfileId { get; set; } = "default";
}

// Query passages for pipeline with specific chunking
var passages = await Passage.Query(
    p => pipeline.DocumentIds.Contains(p.SourceDocumentId) &&
         p.ChunkingProfileId == pipeline.ChunkingProfileId, ct);
```

---

## Implementation Checklist

### Pre-Implementation

- [ ] Review this document with team
- [ ] Identify any Meridian-specific considerations
- [ ] Set up feature branch: `feature/document-centric-architecture`
- [ ] Create backup of current data (if not greenfield)

### Phase 1 Implementation

- [ ] Add `DocumentIds` to `DocumentPipeline`
- [ ] Update `DocumentIngestionService`
- [ ] Migrate all query sites (see checklist above)
- [ ] Add helper methods (`GetForPipelineAsync`)
- [ ] Run validation script
- [ ] Write/update unit tests
- [ ] Run full test suite
- [ ] Manual testing: upload, process, retrieve

### Phase 2 Implementation

- [ ] Remove `SourceDocument.PipelineId`
- [ ] Update virtual document creation to rely on `IsVirtual`
- [ ] Ensure document reuse by manipulating `DocumentIds`
- [ ] Regression test ingestion + reuse flows

### Phase 3 Implementation

- [ ] Remove `Passage.PipelineId`
- [ ] Update all passage queries
- [ ] Test retrieval quality
- [ ] Performance testing
- [ ] Monitor extraction confidence scores

### Optional Enhancements (Backlog)

- [ ] Introduce content-hash deduplication once duplication pain surfaces
- [ ] Add first-class cloning endpoint/UI if teams request guided automation
- [ ] Migrate to join table when document counts per pipeline exceed practical list limits

### Post-Implementation

- [ ] Update documentation
- [ ] Update API specs
- [ ] Performance benchmarking
- [ ] User acceptance testing
- [ ] Deploy to staging
- [ ] Monitor metrics
- [ ] Deploy to production

---

## Continuation Instructions

**To resume this work in a future session, say:**

> "Continue the work on DOCUMENT-CENTRIC-REFACTORING.md"

**I will:**

1. Read this document to understand context and current state
2. Check the implementation checklist to see what's complete
3. Review the code to determine which phase is in progress
4. Continue from the next uncompleted task in the appropriate phase
5. Update this document with progress notes

**Progress Tracking:**
Add notes below as each phase completes:

---

### Progress Log

**Phase 1: DocumentIds List**

- Status: Not started
- Started: [DATE]
- Completed: [DATE]
- Notes: [Add any issues/learnings here]

**Phase 2: Remove SourceDocument.PipelineId**

- Status: Not started
- Started: [DATE]
- Completed: [DATE]
- Notes: [Add any issues/learnings here]

**Phase 3: Remove Passage.PipelineId**

- Status: Not started
- Started: [DATE]
- Completed: [DATE]
- Notes: [Add any issues/learnings here]

**Phase 5: Pipeline Cloning**

- Status: Not started
- Started: [DATE]
- Completed: [DATE]
- Notes: [Add any issues/learnings here]

---

## Questions & Decisions Log

A: YAGNI - Simple list handles expected scale (10-500 docs/pipeline). Can migrate to join table later if needed. Follows existing ProcessingJob pattern.

**Q: Should Passages remain pipeline-specific?**
A: No - Chunking is deterministic based on document text. Same document = same chunks = same embeddings. Makes retrieval artifacts reusable.

**Q: How to handle AuthoritativeNotes virtual documents?**
A: Mark with `IsVirtual=true` and `OriginPipelineId`. Exclude from reuse/cloning. These are pipeline-specific overrides, not general documents.

**Q: Does classification need pipeline context?**
A: No - Current classifier is document-intrinsic (uses text + global SourceType catalog). No pipeline-specific logic.

A: Koan's Entity<T> handles optimistic concurrency. Use HashSet<string> to prevent duplicate IDs. Not a major concern given upload workflow (batched, not concurrent).

**Q: How do we keep Authoritative Notes isolated?**
A: Leverage the existing `IsVirtual` flag and avoid linking those IDs to other pipelines or clones. No extra fields required.

---

## References

- Architecture Analysis: [conversation summary]
- Current Code: `samples/S7.Meridian/`
- Koan Entity<T>: `/src/Koan.Data.Core/Model/Entity.cs`
- ProcessingJob Pattern: `samples/S7.Meridian/Models/ProcessingJob.cs`

---

**END OF DOCUMENT**

_Last Updated: October 23, 2025_
_Status: Ready for Implementation_
_Next Action: Begin Phase 1 - Add DocumentIds to DocumentPipeline_
