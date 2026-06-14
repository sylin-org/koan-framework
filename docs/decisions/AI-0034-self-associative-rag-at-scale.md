---
id: AI-0034
slug: AI-0034-self-associative-rag-at-scale
domain: AI
status: Proposed
date: 2026-04-02
---

# ADR: Self-Associative RAG at Scale — Entity-Native Corpora, Agentic Retrieval, and Emergent Knowledge Graphs

**Contract**

- **Inputs:** Raw files (text, PDF, images, diagrams, audio/video) or Entity<T> instances ingested into typed, named or unnamed corpora. Natural-language directives that shape how the ingestion pipeline extracts meaning. Natural-language focus strings that shape how the retrieval agent searches and generates. Evaluation test sets for quality measurement.
- **Outputs:** `Rag.Corpus<T>()` static facade returning a persistent, queryable knowledge corpus scoped to an entity type. Answers with source citations, confidence scores, and retrieval traces. Streaming answers via `IAsyncEnumerable`. Typed extraction via `Ask<TResult>()`. Stateful conversational sessions. Corpus health metrics and freshness indicators. Quality evaluation results (RAGAS + hallucination + context utilization).
- **Error Modes:** `Ask()` on empty corpus: throws `RagCorpusEmptyException` on string overload; returns `RagQueryStatus.EmptyCorpus` on `AskResult`. `Ingest()` with corrupt file: returns `RagIngestResult` with per-file error details; successful files are committed, failures do not block the batch. `Rag.Corpus<T>("Nonexistent")` where no corpus with that name has been created: throws `RagCorpusNotFoundException`. `Rag.Compose(a, b)` where caller lacks authorization for corpus `b`: fails at Query Gate before any retrieval executes. No registered embedding adapter: throws with remediation message ("No embed adapter registered. Install Koan.AI.Adapter.Ollama or configure a cloud source."). Agent retrieval exhausts max search rounds without meeting confidence threshold: returns answer with `Confidence < MinConfidence` and `RagQueryStatus.LowConfidence` with retrieval trace for debugging.
- **Acceptance Criteria:** A developer can load 500 heterogeneous policy documents into a corpus with `Rag.Corpus<Policy>().Ingest(files)` and query with `Rag.Corpus<Policy>().Ask("How do I connect to external APIs?")` — receiving an answer that surfaces API integration patterns, PII handling requirements, compliance policies, and certified library recommendations, all from different source documents, without any manual cross-referencing configuration. The system discovers these connections through entities extracted from the documents themselves. Quality metrics (faithfulness, relevancy) meet or exceed 0.85 on a representative evaluation set. Adding `<PackageReference Include="Koan.AI.Rag" />` and calling `services.AddKoan()` enables the full RAG pipeline with zero additional configuration.

**Edge Cases**

- `Rag.Corpus<Policy>()` called from two different request threads simultaneously: Thread-safe. Both receive the same corpus instance (singleton per entity type + name). Concurrent `Ingest` and `Ask` operations are safe; the corpus uses versioned snapshots so queries always see a consistent state.
- `Ingest(entity)` where the entity has no string properties: Falls back to `entity.ToString()`. If the result is empty or a type name, the ingestion logs a warning and skips the entity without failing the batch.
- `Ask()` with a Focus string that contradicts the corpus Directive: Focus takes precedence at query time. The Directive shapes how content was extracted and indexed; the Focus shapes how it is retrieved and presented. No conflict — they operate at different pipeline stages.
- `Rag.Compose(corpusA, corpusB)` where corpora use different embedding models: Each corpus searches independently using its own model. Results are percentile-normalized within each corpus before merge. Cross-encoder reranking produces a unified ranking. Score magnitude differences between models do not bias results.
- `Rebuild("New directive")` called during active ingestion: Queues the rebuild. Active ingestion completes for in-flight documents. Rebuild starts after the queue drains. Documents ingested during the rebuild use the new directive.
- `Corpus<Policy>()` (default, unnamed) when the entity class has multiple `[RagCorpus]` attributes: The unnamed `Corpus<Policy>()` returns the default corpus (the one declared with `[RagCorpus]` and no name). Named corpora require explicit `Corpus<Policy>("Medical")`. If no unnamed `[RagCorpus]` exists, the default corpus uses convention inference (all string properties, no directive).
- Ingestion of a 200-page PDF with mixed content (text pages, scanned images, embedded diagrams, tables): Modality detection runs per page. Text pages are parsed directly. Image pages route through OCR. Diagrams route through Describe. Tables are extracted as structured data. All outputs merge into a single rich entity. ColPali visual indexing runs in parallel if the vector store supports multi-vector.
- `Adapt()` called when `AiCapability.Train` has no registered adapter: Throws with remediation message ("Embedding adaptation requires a training-capable adapter. Configure a local compute endpoint or install Koan.AI.Adapter.Training."). Does not silently degrade.
- Concept graph entity resolution produces a false merge (e.g., "Apple" the company and "apple" the fruit): Merge-on-read design preserves original mentions. The canonical entity mapping is a separate table, not a destructive merge. Corrective action: delete the mapping row. All original data intact. Graph quality metrics (density, clustering coefficient) surface anomalies.
- Document deleted via `Entity<Policy>.Delete()` with auto-wired lifecycle hook: Chunks from the document are removed from the vector index. Entity mentions from the document are decremented in the concept graph. Entities reaching zero mentions are pruned. Content-hash-based deduplication tracks provenance at document level, not chunk level, to support GDPR right-to-erasure correctly.

## Context

### The Problem

Enterprise knowledge is scattered across hundreds or thousands of documents — policies, technical guides, compliance requirements, certified library catalogs, architectural diagrams, meeting recordings. When a developer asks "How do I connect to external APIs?", the answer requires synthesis across multiple document types and domains:

- API integration patterns (from technical guides)
- PII handling requirements (from compliance policies)
- HIPAA data transfer rules (from regulatory documents)
- Approved libraries and their certification status (from library catalogs)

No single document contains the complete answer. The connections between these documents exist in the content itself — a compliance policy that says "all external data sources must comply with Section 7" and a technical guide that says "REST APIs are external data sources" — but traditional RAG (flat chunking + vector search) cannot discover these cross-document relationships. Embedding similarity between "API connection tutorial" and "PII data handling requirements" is low despite their conceptual relevance.

### Why Existing Koan Infrastructure Is Necessary but Insufficient

The framework provides all the building blocks:

| Building Block | ADR | Status |
|---|---|---|
| `Vector<T>` facade with hybrid search | ADR-0051 | Complete |
| `Client.Chat/Embed/Describe/Ocr/Transcribe/Extract/Classify/Rerank` | AI-0021, AI-0033 | Complete |
| `ChainBuilder` with `Retrieve<T>()`, `Rerank()`, `Compress()` | AI-0026 | Complete |
| `[Embedding]` with convention inference, templates, token truncation | ARCH-0070 | Complete |
| `[MediaAnalysis]` with multi-flag processing | AI-0027 | Complete |
| `Modality` enum for content routing | AI-0033 | Complete |
| `Client.Scope()` for per-category model routing | AI-0021 | Complete |
| `IRerankAdapter` with `Client.Rerank()` | AI-0033 | Complete |
| `IAugmentation` pipeline | AI-0010 | Complete |
| `Agent.Create()` with tool use | AI-0031 | Complete |
| `Chunker` with semantic boundaries (800-1000 tokens, 50-token overlap) | AI-0018 | Complete |
| Recipes and model resolution chain | AI-0032 | Complete |
| `EmbeddingMetadata.ComputeSignature()` for change detection | ARCH-0070 | Complete |
| `Prompt` class with uri-inspired immutable design | AI-0025 | Complete |

What is missing is the **orchestration layer** that composes these primitives into a self-associative knowledge system — the gap between "I can search vectors" and "I get the right answer with cross-document context every time, automatically."

### Research Foundation

This ADR is informed by the following research (2024-2026):

| Technique | Source | Relevance |
|---|---|---|
| Contextual Retrieval | Anthropic, 2024 | 67% retrieval failure reduction with contextual chunk prefixes |
| RAPTOR | Stanford, ICLR 2024 | Recursive abstractive processing for hierarchical retrieval |
| GraphRAG | Microsoft, 2024 | Entity-relationship extraction for corpus-wide reasoning |
| LinearRAG | ICLR 2026 | Entity extraction + semantic linking without relationship extraction; 80% of GraphRAG quality at fraction of cost |
| LazyGraphRAG | Microsoft Research, 2024 | 1000x cheaper graph construction via deferred query-time extraction |
| A-RAG | Feb 2026 | Agentic retrieval with hierarchical tool interfaces; 78% accuracy vs 34% for pipeline RAG on multi-hop queries |
| ColPali | arXiv 2407.01449 | Vision-language model retrieval from page images; preserves visual layout without OCR |
| Late Chunking | Jina AI, 2024 | Full-document embedding with post-attention chunking |
| CRAG | arXiv 2401.15884 | Corrective retrieval with sufficiency evaluation |
| RAGRouter-Bench | Jan 2026 | No single retrieval paradigm is universally optimal; adaptive routing required |
| CustomIR | arXiv 2510.21729 | Unsupervised embedding adaptation via synthetic training pairs; up to 27-44% domain improvement |
| Search-R1 / R1-Searcher | Mar 2025 | RL-trained search agents outperform prompt-engineered by 20-41% |

### Persona-Driven Validation

Six personas were evaluated to stress-test the design:

| Persona | Primary Interaction | Key Insight |
|---|---|---|
| Healthcare Developer (Priya) | `corpus.Ask("How do I connect to external APIs?")` | Must surface PII policies, compliance requirements, and certified libraries from different source documents without manual cross-referencing. The system must discover these connections. |
| Pokemon Collector (Marcus) | `Rag.Compose(cards, market, rules).Ask("Best legal Fire-type under $50 with best win rate?")` | Requires cross-corpus composition: card data + market prices + tournament rules. Domain-agnostic mechanism must work identically for any subject matter. |
| Data Scientist (Riku) | `corpus.Evaluate(testSet)`, `corpus.Adapt()`, `corpus.Search("query")` | Needs quality metrics, embedding fine-tuning, and raw chunk inspection for debugging. Cannot be locked out of the internals. |
| DevOps Engineer (Sam) | `corpus.Stats()`, `corpus.IsReady()`, boot report | Needs operational visibility: freshness, compute usage, health, reindex recommendations. Zero interaction with AI concepts. |
| Compliance Officer (Dana) | `corpus.AskResult("...")` with citations | Every answer must cite source documents. Audit trail mandatory. Cannot tolerate hallucinated compliance advice. |
| Platform Architect (Jun) | `[RagCorpus]` attribute, `EntityContext.Partition`, Zen Garden parallelism | Needs multi-tenant isolation, declarative entity configuration, and horizontal scaling across GPU topology. |

Three cross-persona signals:

1. **Zero-config must truly be zero** — Priya and Marcus should never encounter configuration, graph strategies, or chunking options. The system must work immediately.
2. **Quality is non-negotiable** — Dana and Priya cannot tolerate missed cross-references or hallucinated facts. Quality over speed at every decision point.
3. **The mechanism must be domain-agnostic** — Pokemon cards, healthcare policies, and mathematical proofs must use the identical pipeline. No domain vocabulary in the architecture.

### Design Principles

Four principles govern every decision in this ADR:

1. **The documents are the configuration.** If a relationship between API guides and PII policies exists in the content, the system discovers it. No developer declares cross-cutting rules. The content graph is the policy engine.
2. **The API speaks human, the pipeline speaks machine.** Developers write natural-language directives and focus strings. The pipeline translates intent into retrieval strategy. Cognitive load is bounded by the developer's ability to express what they want in a sentence.
3. **Convention over configuration, with escape hatches.** Every operation works with zero configuration via Entity<T> convention inference. Named corpora, directives, options objects, and structured hints are progressive escape hatches for users who need control.
4. **Entity<T> is the bridge.** Production data models are the substrate for knowledge. Entities declare their corpus membership via attributes. Entity lifecycle events feed the corpus automatically. The entity schema informs extraction.

## Decision

### Part 1: `Rag.Corpus<T>()` — The Static Facade

A new first-class static facade sits alongside `Entity<T>`, `Vector<T>`, and `Client`, following established Koan patterns: entity-typed, static access, convention defaults, tier escalation.

**Tier escalation:**

```csharp
// Tier 0: Zero config — works immediately
await Rag.Corpus<Policy>().Ingest(files);
var answer = await Rag.Corpus<Policy>().Ask("What are the PII rules?");

// Tier 1: Named corpus with directive
var medical = Rag.Corpus<Policy>("Medical", "Optimize for medical terminology");
await medical.Ingest(files);

// Tier 2: Query with focus
var answer = await corpus.Ask("How do I connect to external APIs?",
    "Emphasize security and compliance implications");

// Tier 3: Rich result with citations
var result = await corpus.AskResult("What are the PII rules?");
// result.Answer, result.Sources[], result.Confidence, result.TokensUsed,
// result.Latency, result.Trace (retrieval strategy, intermediate results)

// Tier 4: Streaming
await foreach (var token in corpus.Stream("Summarize all policies"))
    Console.Write(token);

// Tier 5: Typed extraction
var risks = await corpus.Ask<ComplianceRisk[]>("What are the compliance risks?");

// Tier 6: Stateful session
await using var session = corpus.Session();
await session.Ask("What are the PII requirements?");
await session.Ask("How does that apply to REST APIs?");  // Carries context

// Tier 7: Composition across corpora
var answer = await Rag.Compose(
    Rag.Corpus<Policy>(),
    Rag.Corpus<TechGuide>()
).Ask("How do I connect to external APIs?");

// Tier 8: Full options (escape hatch)
var answer = await corpus.Ask("...", new RagQueryOptions
{
    Focus = "...",
    MaxSearchRounds = 3,
    MinConfidence = 0.7f,
    Hint = new RetrievalHint { Depth = SearchDepth.Deep, Prefer = SearchPreference.Recall }
}, ct);
```

**Operational surface:**

```csharp
// Ingestion — returns result, not Task
RagIngestResult result = await corpus.Ingest(files, progress, ct);
// result.FilesProcessed, result.ChunksCreated, result.EntitiesExtracted,
// result.Errors[], result.Duration

// Single entity ingestion (lifecycle hook target)
await corpus.Ingest(entity, ct);

// Remove from corpus
await corpus.Remove(entity, ct);

// Debugging — raw chunks without synthesis
IReadOnlyList<RagChunk> chunks = await corpus.Search("query", maxResults: 10, ct);

// Rebuild with optional new directive (requires confirmation for directive changes)
await corpus.Rebuild(ct);
await corpus.Rebuild(new RagRebuildOptions { Directive = "New directive", Confirm = true }, ct);

// Embedding adaptation (Phase 2 — API reserved, implementation deferred)
await corpus.Adapt(ct);

// Quality evaluation
RagEvaluation eval = await corpus.Evaluate(testSet, ct);
// eval.Faithfulness, eval.Relevancy, eval.HallucinationScore,
// eval.ContextUtilization, eval.Results[], eval.Duration

// Health and metrics
RagCorpusStats stats = await corpus.Stats(ct);
// stats.Documents, stats.Chunks, stats.Entities, stats.Relationships,
// stats.ComputeNodes, stats.IngestDuration, stats.AvgQueryLatency,
// stats.ReindexRecommended, stats.ReindexReason, stats.FreshnessScore

// Readiness check
bool ready = await corpus.IsReady(ct);

// Reset (dev/test)
await corpus.Clear(ct);
```

**Design invariants:**

- Every async method accepts `CancellationToken ct = default`.
- `Rag.Corpus<T>()` is idempotent: calling it multiple times returns the same singleton instance (per entity type + name combination).
- The default (unnamed) corpus for an entity type has the implicit name `typeof(T).Name`.
- `Session()` returns `IAsyncDisposable` with configurable token budget and exhaustion strategy (auto-summarize by default).
- `Ingest()` always returns `RagIngestResult`, never bare `Task`. Partial failures are surfaced per-file; successful files are committed.
- `Ask(string)` throws `RagCorpusEmptyException` on an empty corpus. `AskResult()` returns structured status.

### Part 2: Entity Declaration — `[RagCorpus]` Attribute

Corpora are declared on entities, following the `[Embedding]` and `[MediaAnalysis]` precedent. The attribute uses `AllowMultiple = true` to support multiple named corpora on a single entity type.

```csharp
// Convention default — all string properties contribute, no directive
[RagCorpus]
public class Policy : Entity<Policy>
{
    public string Title { get; set; }
    public string Content { get; set; }
    public DateOnly EffectiveDate { get; set; }

    [EmbeddingIgnore]
    public string InternalNotes { get; set; }
}

// Named corpus with directive and source routing
[RagCorpus("Medical", Directive = "Optimize for medical terminology",
    Source = "garden/reasoning-model")]
public class Policy : Entity<Policy> { ... }

// Multiple corpora on the same entity type
[RagCorpus]                                         // Default
[RagCorpus("Medical", Directive = "...")]           // Medical perspective
[RagCorpus("Legal", Directive = "...")]             // Legal perspective
public class Policy : Entity<Policy> { ... }
```

**Attribute properties:**

| Property | Type | Default | Purpose |
|---|---|---|---|
| `Name` | `string?` | `null` (unnamed/default) | Corpus identity |
| `Directive` | `string?` | `null` | Natural-language extraction shaping |
| `Source` | `string?` | `null` | AI source routing (matches `[Embedding].Source` pattern) |
| `GraphStrategy` | `GraphStrategy` | `Lightweight` | Entity extraction + semantic linking (default), Full (+ relationships), or Lazy (query-time) |
| `Async` | `bool` | `true` | Defer ingestion to background worker (default) or process synchronously |
| `Version` | `int` | `1` | Increment to trigger re-ingestion of all entities (matches `[MediaAnalysis].Version` pattern) |

**Auto-registration:** `KoanRagAutoRegistrar` discovers `[RagCorpus]` attributes via assembly scanning, wires `Entity<T>.Events.AfterUpsert()` and `Entity<T>.Events.AfterDelete()` hooks in its `Initialize()` method, registers `RagIngestionWorker` as a background service, registers `RagCorpusHealthCheck` with tags `["ai", "rag", "ready"]`, and reports corpus inventory in the boot report via `Describe()`.

**Reference = Intent:** Adding `<PackageReference Include="Koan.AI.Rag" />` and calling `services.AddKoan()` enables the full RAG pipeline for any entity with `[RagCorpus]`. No additional `Program.cs` configuration.

### Part 3: Ingestion Pipeline — Multi-Modal Understanding

The ingestion pipeline transforms raw content into deeply understood, richly connected knowledge. It uses best-of-breed models per extraction stage via `Client.Scope()`, parallelizes across available Zen Garden compute, and produces a multi-faceted entity representation.

**Pipeline stages:**

```
Raw Content
    │
    ▼
┌───────────────────────────────────────────────────────────────┐
│  INGESTION GATE (Security Surface)                            │
│  1. File validation (magic bytes, size limits, format check)  │
│  2. Content sanitization (strip macros, scripts, objects)     │
│  3. Sensitivity pre-classification (local-only model)         │
│  4. Injection pattern scanning (text-based + OCR extraction)  │
│  → Routing decision: local-only or cloud-eligible             │
└───────────────────────┬───────────────────────────────────────┘
                        │
                        ▼
┌───────────────────────────────────────────────────────────────┐
│  TRUSTED INNARDS (Parallel Extraction)                        │
│                                                               │
│  ┌─────────────┐  ┌─────────────┐  ┌───────────────────────┐ │
│  │   Parse      │  │  Describe   │  │  Classify             │ │
│  │  (text,      │  │  (vision    │  │  (domain detection,   │ │
│  │   structure, │  │   model)    │  │   smaller model)      │ │
│  │   tables)    │  │             │  │                       │ │
│  └──────┬───────┘  └──────┬──────┘  └───────────┬───────────┘ │
│         │                 │                      │             │
│  ┌──────▼───────┐         │                      │             │
│  │  Modality-   │         │                      │             │
│  │  specific:   │         │                      │             │
│  │  OCR /       │         │                      │             │
│  │  Transcribe  │         │                      │             │
│  └──────┬───────┘         │                      │             │
│         │                 │                      │             │
│         └────────┬────────┘──────────┬───────────┘             │
│                  │                   │                          │
│         ┌────────▼─────────┐  ┌─────▼────────────────┐        │
│         │  Extract Entities│  │  Extract Facts &      │        │
│         │  (reasoning      │  │  Assertions           │        │
│         │   model)         │  │  (reasoning model)    │        │
│         └────────┬─────────┘  └─────┬────────────────┘        │
│                  │                   │                          │
│                  └─────────┬─────────┘                          │
│                            │                                    │
│                   ┌────────▼────────┐                           │
│                   │  Assemble Rich  │                           │
│                   │  Entity         │                           │
│                   └────────┬────────┘                           │
│                            │                                    │
│            ┌───────────────┼───────────────┐                    │
│            │               │               │                    │
│     ┌──────▼─────┐  ┌─────▼──────┐  ┌─────▼──────┐           │
│     │  Semantic   │  │ Multi-Level│  │  Merge to  │           │
│     │  Chunking   │  │  Embed     │  │  Concept   │           │
│     │  + Context  │  │ (doc,      │  │  Graph     │           │
│     │  Prefixes   │  │  section,  │  │ (entity    │           │
│     │  + Parent/  │  │  chunk)    │  │  resolution│           │
│     │  Child      │  │            │  │  + merge)  │           │
│     └────────────┘  └────────────┘  └────────────┘           │
└───────────────────────────────────────────────────────────────┘
```

**Per-stage model routing:** Each extraction stage routes to the best available model via the existing `Client.Scope()` mechanism. Configuration is optional; convention discovery finds what is available:

```json
{
  "Koan": {
    "Rag": {
      "Models": {
        "Ocr": "garden/pixtral-large",
        "Describe": "garden/llava-next",
        "Transcribe": "local/whisper-large-v3",
        "Extract": "garden/llama-70b",
        "Classify": "local/llama-8b",
        "Embed": "local/nomic-embed-text"
      }
    }
  }
}
```

If not configured, the pipeline uses whatever adapters are registered, falling back through the standard AI-0015 source-member resolution chain.

**Directive propagation:** When a corpus has a directive (e.g., "Optimize for medical terminology"), it is injected into every LLM-based extraction prompt as a domain guidance prefix. The developer writes one sentence; the pipeline distributes it to every stage where it is relevant. The extraction prompt for entity extraction becomes:

```
DOMAIN GUIDANCE: {directive}

Given this text chunk from a document titled "{title}", section "{section}":

---
{chunk_content}
---

Extract:
1. ENTITIES: Named concepts, objects, standards, rules, categories, or any other
   noun that another document in this corpus might also reference.
2. FACTS: Specific assertions, rules, or claims this text makes. Each fact should
   be self-contained.
```

This prompt is domain-agnostic. It works identically for Pokemon cards, mathematical proofs, and healthcare policies. The directive provides domain hints; the LLM's world knowledge handles interpretation.

**Contextual chunk prefixes:** Each chunk receives an LLM-generated context prefix (Anthropic contextual retrieval pattern) that describes: which document it comes from, which section, what the surrounding content discusses. The document-level summary is generated once per document and prepended to all chunks from that document. This is not N separate LLM calls per document — it is one summary call, then string concatenation.

**Parent-child chunk hierarchy:** Child chunks (200-400 tokens) are embedded for precision matching. On retrieval, the parent chunk (800-1600 tokens) containing the matched child is returned, preserving surrounding context. The `Vector<T>` index stores child embeddings; the chunk store maps child → parent for context expansion.

### Part 4: Concept Graph — Emergent Knowledge Structure

The concept graph is the mechanism that makes context self-associative. It is built from entities and relationships extracted by the LLM from document content. No human declares categories, cross-references, or domain taxonomies. The documents declare their own connections.

**Graph construction strategies (tiered):**

| Strategy | What It Does | Cost | When to Use |
|---|---|---|---|
| **Lightweight** (default) | Entity extraction + semantic linking. Entities are connected by embedding proximity of their descriptions. No explicit relationship extraction. | Low (~$40-60 for 500 docs) | All corpora. Covers 80% of cross-document relationship discovery. |
| **Full** | Entity extraction + explicit relationship extraction. LLM identifies labeled edges ("requires", "is-a", "governed-by"). | High (~$300-400 for 500 docs) | Corpora where explicit, labeled relationships matter (regulatory, ontological). Opt-in via `GraphStrategy = GraphStrategy.Full`. |
| **Lazy** | No graph at ingest. Entities and relationships are extracted at query time on the retrieved chunks. | Minimal at ingest | Streaming data, exploratory use, or corpora with very high document churn. |

**Entity resolution (domain-agnostic):**

Extracted entities are resolved across documents using a tiered strategy:

1. **Exact string match + normalized aliases** — catches "Fire-type" = "Fire type" = "fire type". Cheap, handles 60-70% of duplicates.
2. **Embedding proximity** — embed entity names + descriptions, cluster by cosine similarity above threshold (0.92 default). Catches "external data source" ≈ "third-party data connection". Handles another 20%.
3. **LLM-assisted resolution** — for the remaining ~10% ambiguous pairs, the reasoning model evaluates with context from both documents. Reserved for high-ambiguity cases to control cost.

**Merge-on-read, not merge-on-write.** Original entity mentions are preserved with their surface forms and source documents. Entity resolution produces a canonical mapping table, not a destructive merge. To undo a false merge: delete the mapping row. All original data is intact.

**Hierarchical resolution.** The system detects is-a relationships between entities ("REST API" is-a "API"). This builds a corpus-specific taxonomy that emerged entirely from the documents — not from a predefined ontology.

**Reference counting and garbage collection.** Each entity mention is tracked to its source document and chunk. When a document is deleted or updated, mention counts are decremented. Entities reaching zero mentions are pruned. A periodic mark-and-sweep compaction (background maintenance task) serves as a consistency safety net against reference count bugs.

**Storage — `IConceptGraphStore` interface:**

The concept graph uses an adapter interface from day one:

- `Load()` — deserialize graph state
- `Save()` — persist graph state
- `GetNeighborhood(entityId, depth)` — retrieve local subgraph for query-time exploration
- `ApplyDelta(GraphDelta)` — apply incremental changes (new entities, new edges, removals)

First adapter: in-memory with periodic snapshot to `.Koan/cache/rag/`. Suitable for corpora up to ~500K entities (~55MB memory). Cold start time is the migration trigger, not entity count — when snapshot deserialization exceeds 5 seconds, the boot report emits a warning.

Second adapter (planned): SQLite-backed via the existing `SqliteCacheStore` pattern in `Koan.Cache.Adapter.Sqlite`. Memory-mapped access for lazy loading of graph neighborhoods.

**Multi-process deployments:** Single-writer (ingestion worker), multi-reader (query instances). Readers load a snapshot and receive incremental updates via entity change events. The ingestion worker is the sole graph mutator.

**Model independence:** The concept graph is derived from entity content via `Client.Extract<T>()`, not from embedding clusters. This means the graph survives an embedding model change without reconstruction. `Adapt()` or operator model rotation triggers re-embedding but not re-extraction.

### Part 5: Agentic Retrieval — Adaptive, Invisible Intelligence

Retrieval is agent-driven, not pipeline-driven. The agent has access to a set of tools and decides which to use per query based on query analysis. Simple queries use one tool call. Complex multi-hop queries use multiple rounds with different tools. The developer sees none of this — they call `Ask()` and get an answer.

**Why agentic over pipeline:** A-RAG (Feb 2026) demonstrated that agentic retrieval achieves 78% accuracy on complex multi-hop queries vs 34% for fixed pipelines. The key finding: with clean tool interfaces, the agent spontaneously generalizes to diverse retrieval strategies without predefined workflows. RAGRouter-Bench (Jan 2026) confirmed that no single retrieval paradigm is universally optimal — adaptive selection is required.

**Agent tool set:**

| Tool | What It Does | Priority |
|---|---|---|
| `semantic_search` | Dense embedding similarity search against chunk index | Primary |
| `keyword_search` | Sparse (BM25/SPLADE) search for exact term matching | Primary |
| `chunk_read` | Retrieve a specific chunk or its parent by ID | Primary |
| `sufficiency_check` | Evaluate: "Do I have enough context to answer well?" | Primary |
| `concept_explore` | Follow entity connections in the concept graph to discover related content | Composite (invokes semantic_search on connected entities) |
| `multi_level_search` | Search at document or section granularity instead of chunk level | Composite (invokes semantic_search with level filter) |
| `metadata_filter` | Filter by structured entity metadata (date, type, category, confidence) | Structured |

Primary tools are the agent's core decision space. Composite tools are syntactic sugar over primaries — the agent can call them directly, but they decompose into primitive operations. This follows A-RAG's finding that fewer, well-designed tools outperform richer tool sets.

**Tool extensibility:** `Rag.Corpus<T>().WithTools(customTool)` adds developer-defined tools to the agent's tool set. Custom tools participate in agent reasoning alongside framework tools. This follows the `Agent.Create().WithTools()` pattern from AI-0031.

**Tool discovery:** `RagToolProvider` registers tools based on corpus capabilities at build time. Has a vector store? Register `semantic_search`. Has a concept graph? Register `concept_explore`. Has a ColPali-capable store? Register `visual_search`. Presence = Capability, consistent with Zen Garden's discovery model.

**Focus propagation:** The Focus string becomes the agent's steering instruction. It reliably influences *what* the agent searches for (topic scoping, terminology preference). For tactical parameters that natural language cannot reliably control (search depth, precision/recall preference, max rounds), the optional `RetrievalHint` struct provides structured control:

```csharp
// Focus string: semantic steering
await corpus.Ask("What patterns emerge?", "Focus on statistical relationships");

// RetrievalHint: tactical steering (Tier 8)
await corpus.Ask("...", new RagQueryOptions
{
    Focus = "Focus on statistical relationships",
    Hint = new RetrievalHint
    {
        Depth = SearchDepth.Deep,       // More search rounds
        Prefer = SearchPreference.Recall, // Broader results over precision
        MaxRounds = 4
    }
});
```

**Retrieval strategy pinning (for evaluation):** In production, the agent decides strategy (`RetrievalStrategy.Auto`). For eval test suites and regression testing, strategy can be pinned:

```csharp
await corpus.Ask("...", new RagQueryOptions
{
    Strategy = RetrievalStrategy.SemanticOnly  // Deterministic for eval
});
```

Available strategies: `Auto` (agent decides), `SemanticOnly`, `KeywordOnly`, `HybridOnly`, `GraphFirst`. Pinning disables the agent's tool selection and forces a specific retrieval path.

**Query Gate (Security Surface):**

```
User Query
    │
    ▼
┌───────────────────────────────────────────────────────────┐
│  QUERY GATE                                               │
│  1. Authentication — who is asking                        │
│  2. Authorization — partition-scoped corpus access check   │
│     (for Compose: intersection of permissions)            │
│  3. Audit logging — query hash, identity, corpus, time    │
│  4. Response filtering — redact restricted content        │
└───────────────────┬───────────────────────────────────────┘
                    │
                    ▼
             Agentic Retrieval
                    │
                    ▼
┌───────────────────────────────────────────────────────────┐
│  GENERATION (Structural Prompt Separation)                 │
│  System role: Instructions, Focus, generation constraints  │
│  Tool-result role: Retrieved chunks (UNTRUSTED data)      │
│  → No retrieved content in system or assistant roles      │
│  → Structural injection defense independent of gates      │
└───────────────────────────────────────────────────────────┘
                    │
                    ▼
              Answer + Citations + Trace
```

**Generation hardening:** The generation step structurally separates instructions from retrieved content using the model's message API roles — not text markers within a single prompt. System role carries instructions and Focus; tool-result or user role carries retrieved chunks marked as data. This is the primary defense against retrieval-path prompt injection, independent of the Ingestion Gate's text-based scanning.

### Part 6: Cross-Corpus Composition

`Rag.Compose()` creates a federated query surface across multiple corpora. It is not a graph merge — it is parallel retrieval with unified ranking.

**Composition semantics (v1 — federated):**

1. Fan out the query to each corpus in parallel.
2. Each corpus executes its own agentic retrieval independently.
3. Results are percentile-normalized within each corpus (different embedding models produce different score distributions).
4. Merged results are reranked with `Client.Rerank()` (AI-0033 `IRerankAdapter`).
5. Top-N reranked results feed into the generation agent.

**Score normalization:** Each corpus normalizes its chunk scores to percentile rank within its own result set before merge. This prevents systematic bias toward corpora with higher-magnitude similarity scores.

**Per-corpus top-K:** Each corpus contributes `2x` the final desired count to the merge set, ensuring the reranker has sufficient candidates from each corpus. Configurable via `RagComposeOptions.PerCorpusTopK`.

**Authorization:** Compose enforces the intersection of the caller's permissions across all constituent corpora. If the caller can access Corpus A but not Corpus B, `Rag.Compose(A, B).Ask()` fails at the Query Gate before any retrieval executes. Authorization is checked per-partition, per-corpus, at the gate — not at the merge step.

**Cross-corpus concept graph edges:** Deferred to v2+. In v1, cross-corpus relationships are discovered at generation time by the LLM from the federated result set, not at retrieval time through shared graph edges. This is simpler, more secure (no cross-partition graph leakage), and provides a working system to learn from before tackling the harder problem.

**Internally built on `ChainBuilder.Parallel()`:** Compose creates a `Chain.Create().Parallel(...)` with per-corpus retrieval steps, not a new composition mechanism. This avoids extending ChainBuilder's API surface for a single consumer.

### Part 7: ColPali — Layered Visual Retrieval

ColPali (arXiv 2407.01449) embeds document page images directly using a Vision Language Model, producing multi-vector representations (ColBERT-style late interaction) that preserve visual layout, table structure, and diagram spatial relationships without OCR.

**Layered capability:** ColPali activation is infrastructure-determined, not developer-configured. The framework checks `Vector<T>.GetCapabilities()` at boot time:

- Vector store supports `MultiVectorPerEntity` (Qdrant, Weaviate, Vespa)? → ColPali path enabled alongside text extraction. Both indexes are queried; results merge.
- Vector store is single-vector only (PGVector, Milvus)? → Text extraction pipeline only. No degradation in functionality; ColPali simply isn't available.

The boot report shows which retrieval mode is active:

```
Retrieval: visual + semantic (multi-vector)    ← ColPali active
    — or —
Retrieval: semantic (text extraction)          ← ColPali not available
```

**Security consideration:** ColPali encodes page images directly, bypassing text-based injection scanning. When ColPali is active, the Ingestion Gate must run OCR-based injection scanning on extracted text from every page before the ColPali encoding path — not as an alternative to it. Both paths pass the gate.

### Part 8: Embedding Adaptation — Corpus-Scoped Fine-Tuning

**Status: API reserved, implementation deferred until AI training adapters are available (requires `AiCapability.Train`).**

Embedding adaptation generates synthetic query-document pairs from corpus content, fine-tunes the embedding model on those pairs, and re-indexes the corpus with the adapted model. Research shows 27-44% improvement in domain-specific retrieval quality.

**Configuration:**

```json
{
  "Koan": {
    "Rag": {
      "EmbeddingAdaptation": {
        "Enabled": false
      }
    }
  }
}
```

Configurable globally, per-corpus (via `[RagCorpus(AdaptEmbeddings = true)]`), or on-demand (`corpus.Adapt(ct)`).

**Design constraints:**

- Adaptation produces a **corpus-scoped model**, not a global one. The "Medical" corpus gets a medical-tuned model. Other corpora keep the general model.
- **Blue-green re-indexing protocol:** New embeddings go to a shadow index. Old index stays live for queries. Atomic switch when 100% re-embedded. During the transition window, queries run against the complete old index — never a mix of old and new embeddings.
- **Corpus-confidential artifact:** The adapted model is treated with the same access controls as the corpus itself. Model export or serving to external parties requires the same authorization as reading the corpus.

### Part 9: Evaluation Framework — Measuring Quality

RAG quality is measurable and must be measured. The evaluation framework is built-in, not an afterthought.

**Metrics:**

| Metric | What It Measures | Source |
|---|---|---|
| **Faithfulness** | Does the answer follow the retrieved context? (No hallucinated claims) | RAGAS |
| **Answer Relevancy** | Does the answer address the question? | RAGAS |
| **Context Precision** | Are the retrieved chunks relevant to the question? | RAGAS |
| **Context Recall** | Did retrieval find all relevant chunks? | RAGAS |
| **Hallucination Score** | Are there claims not supported by any source? (LLM-judge) | Supplement |
| **Context Utilization** | What fraction of retrieved chunks contributed to the answer? | Supplement |

**Test set format:**

```csharp
var testSet = new RagTestSet
{
    new RagTestCase("What are the PII rules?", 
        expectedAnswer: "PII rules require encryption at rest and in transit...",
        expectedSourceIds: ["policy-037", "policy-041"]),
    new RagTestCase("What is the TLS requirement?",
        expectedAnswer: "All external connections must use TLS 1.3 or higher"),
};

RagEvaluation eval = await corpus.Evaluate(testSet, ct);
```

**Custom metric extension:** `corpus.Evaluate(testSet, customMetrics: [...])` accepts user-defined metric functions for domain-specific evaluation (citation accuracy for legal, entity extraction F1 for biomedical).

**Trajectory capture:** Every evaluation run captures the full sequence of agent tool calls, intermediate results, and final answer as a serializable trajectory. This serves dual purposes: debugging retrieval quality issues, and generating training data for future RL-trained retrieval agents (Search-R1 pattern).

### Part 10: Zen Garden Parallelism — Distributed Compute

The ingestion pipeline parallelizes across available Zen Garden compute automatically. The developer calls `corpus.Ingest(files)` and the framework distributes work across whatever GPUs are available.

**Document-level parallelism:** Each document's extraction pipeline is independent until entity resolution. With N stones in the garden:

- Documents are distributed across stones based on available compute capacity.
- The XTX 24GB handles heavy reasoning tasks (entity extraction with 13B+ models).
- The RTX 8GB cards handle lighter tasks (OCR, Describe, Classify with 7B models).
- Zen Garden's topology API provides stone discovery and capability detection.

**Within-document parallelism:** For a single document, independent extraction stages run on different GPUs simultaneously:

- Parse + Describe + Classify are independent → parallel.
- Extract Entities depends on parsed content → sequential after parse.
- Chunking + Embedding + Graph Merge depend on extraction → sequential after extraction.

**Entity resolution pipelining:** Entity resolution is inherently sequential (each resolved entity affects subsequent resolutions), but it pipelines behind extraction:

- Batch 1 documents: extract (parallel) → resolve (sequential)
- Batch 2 documents: extract (parallel, overlaps with Batch 1 resolve) → resolve

With 5 GPUs, the ingestion of 500 documents drops from ~16 hours (single GPU) to ~2-3 hours.

**Query parallelism:** Sub-question decomposition and multi-corpus composition fan out to multiple stones in parallel. Each sub-question or corpus searches independently.

**The DX for parallelism:** None. The developer does not configure parallelism. `Stats()` reports compute node count and ingestion throughput. More GPUs = faster ingestion, lower query latency.

### Part 11: Security Boundary Model

Two security surfaces. Trusted innards. The pipeline runs inside a controlled perimeter.

**Ingestion Gate:**

| Step | Purpose | Mitigates |
|---|---|---|
| File validation | Magic bytes vs declared type, size limits, malformed detection | HIGH-5 (file type validation) |
| Content sanitization | Strip macros, scripts, embedded active content | HIGH-5 |
| Sensitivity pre-classification | Local-only model classifies content sensitivity before any cloud routing | CRIT-2 (PHI to cloud) |
| Injection scanning | Flag documents with known prompt-injection patterns; run on both text and OCR-extracted text (ColPali bypass defense) | CRIT-1 (partial — ingestion path) |
| Entity quarantine | Newly extracted entities enter quarantine at configurable confidence threshold before promotion to live graph | MED-2 (model poisoning) |
| Async batch processing | No per-request feedback to ingester on entity resolution outcomes | HIGH-1 (entity resolution oracle) |

**Query Gate:**

| Step | Purpose | Mitigates |
|---|---|---|
| Authentication | Identity verification | CRIT-3 (access control) |
| Authorization | Partition-scoped corpus access; Compose = intersection of permissions | CRIT-3, Compose leakage |
| Audit logging | Query hash (not verbatim text for sensitive domains), identity, corpus, timestamp, chunk IDs | HIGH-3 (retrieval audit) |
| Response filtering | Redact restricted content based on caller's authorization level | CRIT-3 |

**Trusted Innards:**

Chunking, embedding, entity resolution, concept graph, agent retrieval, reranking, compression, generation — these trust each other. No per-component authentication. No encryption on internal channels. Internal prompt construction is framework-controlled code.

**Generation-layer defense (independent of gates):**

Retrieved chunks are always placed in tool-result or user message roles, never in system role. This is structural prompt separation using the model API's message types, not text-based markers. This defends against retrieval-path injection that survives the Ingestion Gate.

**Multi-tenant isolation:**

Each partition gets its own concept graph. No cross-partition graph edges. `EntityContext.Partition` scopes all corpus operations. For deployments that need cross-partition knowledge sharing (single-tenant, organizational partitions), read-only graph federation is available as an explicit opt-in — not a default.

**Compliance positioning:**

| Requirement | Mechanism |
|---|---|
| HIPAA §164.312(a)(1) — Access Control | Query Gate partition authorization at retrieval time |
| HIPAA §164.312(b) — Audit Controls | Query Gate audit logging every query with identity |
| HIPAA §164.312(a)(2)(iv) — Encryption | Infrastructure-level encryption at rest for chunk store, graph store, vector store |
| GDPR Art. 17 — Right to Erasure | Document-level provenance tracking; entity reference counting; entity pruned at zero mentions |
| GDPR Art. 25 — Data Minimization | `[EmbeddingIgnore]` for sensitive properties; convention inference uses string properties only |
| SOC 2 CC6.1 — Logical Access | Partition-scoped authorization with intersection semantics for Compose |
| SOC 2 CC7.2 — Monitoring | Boot report health checks, query audit logging, graph quality metrics |

### Part 12: Incremental Updates and Ever-Growing Corpora

Corpora are ever-growing. The initial bulk `Ingest(files)` is a one-time event. After that, entities flow in through lifecycle hooks. The architecture is optimized for incremental, not batch.

**Incremental ingestion flow:**

1. Entity saved → `Events.AfterUpsert` fires → ingestion worker queues the entity.
2. Worker computes content hash via `EmbeddingMetadata.ComputeSignature()`.
3. If hash matches existing record → skip (no change). If different → re-extract.
4. Re-extraction replaces old chunks, updates entity mentions, adjusts graph.
5. Only changed content is re-embedded. Unchanged chunks keep their embeddings.

**Document deletion:**

1. Entity deleted → `Events.AfterDelete` fires → removal worker queues.
2. Chunks from the document are removed from the vector index.
3. Entity mentions from the document are decremented.
4. Entities reaching zero mentions are pruned from the graph.
5. Provenance is tracked at document level (not chunk level) to support GDPR erasure.

**Reprocessing triggers and recommendations:**

| Trigger | What Happens | Detection |
|---|---|---|
| Better models available | Full re-extraction recommended | Operator decision |
| Embedding model change | Re-embed all chunks (blue-green protocol) | Automatic on model config change |
| Directive change | Full re-extraction with new directive | `Rebuild(newDirective)` |
| Graph quality degradation | Prune low-confidence edges, re-resolve ambiguous entities | `Stats().GraphDensity` exceeds threshold |
| Significant corpus growth (~2x) | Re-cluster summaries, re-adapt embeddings | `Stats().ReindexRecommended` |

The framework detects when reprocessing is beneficial and surfaces it through `Stats()` and the health check. The developer decides when to pull the trigger.

**`RagIngestionState<T>` entity:**

Tracks per-entity ingestion state for idempotency, retry, and observability. Inherits from `Entity<RagIngestionState>` with GUID v7 key. Stored in `.Koan/` persistent store (not the user's primary data store), matching the `EmbedJob<T>` pattern.

Fields: `EntityId`, `ContentSignature` (SHA-256), `Status` (Pending, Processing, Completed, Failed, FailedPermanent), `RetryCount`, `Error`, `LastProcessed`, `ChunkCount`, `EntityCount`.

### Part 13: Integration with Chain and Agent

`Rag.Corpus<T>()` integrates with both `ChainBuilder` (AI-0026) and `Agent` (AI-0031) for developers who need composition beyond the `Ask()` surface.

**Chain integration (non-agentic, deterministic):**

```csharp
var answer = await Chain.Create()
    .System("You are a compliance advisor.")
    .WithRetrieval(Rag.Corpus<Policy>())     // RAG as a chain step
    .Chat("Answer based on retrieved context: {input}")
    .WithCitations()
    .Run(new { input = userQuery }, ct);
```

**Agent integration (agentic, adaptive):**

```csharp
var agent = Agent.Create()
    .WithCorpus(Rag.Corpus<Policy>())         // Adds RAG tools to agent
    .WithCorpus(Rag.Corpus<TechGuide>())      // Multiple corpora
    .WithTools(customTools)                    // Additional tools
    .Build();

var result = await agent.Run("How do I connect to external APIs?", ct);
```

`WithCorpus()` registers the corpus's retrieval tools (semantic_search, keyword_search, concept_explore, etc.) with the agent, following the `WithAdapterTools()` pattern from AI-0031.

### Part 14: Configuration Defaults

```json
{
  "Koan": {
    "Rag": {
      "ChunkStrategy": "SemanticWithContext",
      "ChildChunkTokens": 300,
      "ParentChunkTokens": 1200,
      "ContextualPrefix": true,
      "HybridAlpha": 0.6,
      "RerankEnabled": true,
      "RerankTopN": 10,
      "GraphStrategy": "Lightweight",
      "EntityResolutionThreshold": 0.92,
      "MaxSearchRounds": 3,
      "MinConfidence": 0.5,
      "CitationsEnabled": true,
      "EmbeddingAdaptation": {
        "Enabled": false
      },
      "Models": {}
    }
  }
}
```

All values have convention defaults. Zero configuration required to use `Rag.Corpus<T>()`.

### Part 15: Content Adapter Architecture — Multi-Round Interpretation

**Added post-initial ADR based on implementation experience.**

Pluggable content-to-text adapters with a multi-round interpretation protocol:

- **Round 1 (Classification)**: Vision model detects content type using hierarchical categories (e.g., `diagram/architecture`, `table`, `chart/line`)
- **Round 2 (Interpretation)**: Strategy-specific prompt extracts structured meaning — components, connections, data, relationships
- **Round 3 (Enrichment)**: Optional pass extracts implicit information — constraints, assumptions, failure modes

Strategy resolution hierarchy:
1. **Pre-determined**: 8 built-in strategies for known content types with optimized prompts
2. **Auto-generated**: Best available reasoning model generates strategy for novel content types (cached per corpus)
3. **Corpus-cached**: Promoted from auto-generated after repeated use

Abstractions: `IContentAdapter`, `ContentAdapterAttribute`, `InterpretationStrategy`, `ContentClassification`.
Built-in adapters: Text, Image, Audio, PDF. Extensible via `[ContentAdapter(".dwg")]` attribute.
Model routing: `RagModelRouting.StrategyGeneration` routes to best reasoning model for strategy creation.

### Part 16: Hierarchical Distillation — Corpus-Wide RAPTOR Tree

**Added post-initial ADR based on research into large-document processing.**

Builds a RAPTOR [Sarthi et al., ICLR 2024] distillation tree from all chunks across all documents in a corpus. The tree progressively clusters and summarizes content bottom-up:

- **Level 0**: All leaf chunks (already embedded during ingestion)
- **Level 1**: Cluster summaries — semantically similar chunks grouped via UMAP + GMM (soft clustering, BIC for k selection)
- **Level 2+**: Recursive clustering and summarization
- **Level N**: Corpus-level thematic summaries

**Cross-document clustering**: Chunks from different documents that discuss the same topic cluster together. Documents 1, 3, 54, 194 discussing HL7 are grouped regardless of document boundaries.

**Collapsed tree retrieval**: All tree levels stored in the same vector index, searched simultaneously. A factoid query matches a leaf chunk; a thematic query matches a Level 2-3 summary. No routing decision needed — embedding similarity naturally selects the right granularity. RAPTOR evaluation showed 18-57% of useful retrieved nodes come from non-leaf layers.

**Adaptive depth**: `min(ceil(log(chunkCount) / log(clusterFactor)), maxDepth)`. Default cluster factor: 10. Max depth: 5. Auto-computed from corpus size with configuration override.

**Two build phases**:
- Phase A (per-document): Built during ingestion, captures internal structure
- Phase B (corpus-wide): Trigger-based (on `Rebuild()`, `ReindexRecommended`, or explicit API), captures cross-document connections

**Incremental updates**: New chunks assigned to nearest existing cluster centroids, only affected clusters re-summarized. Version-stamped nodes with metadata swap for atomic tree updates.

**Clustering implementation**: UMAP NuGet package (pure C#, MIT) for dimensionality reduction (768→10 dims). Diagonal GMM with BIC for k selection and soft assignment. `IClusteringStrategy` interface for algorithm swappability.

Abstractions: `IDistillationTreeStore` (mirrors `IConceptGraphStore`: Load/Save/ApplyDelta/Clear/GetStats), `DistillationNode`.
Configuration: `RagOptions.TreeClusterFactor`, `RagOptions.TreeMaxDepth`, `RagModelRouting.Summarize`.

## Implementation Phases

| Phase | Scope | Depends On | Delivers |
|---|---|---|---|
| **Phase 1: Core Facade + Ingestion** | `Rag.Corpus<T>()`, `[RagCorpus]`, `Ingest()`, `Ask()`, `AskResult()`, `Stream()`, semantic chunking with contextual prefixes, parent-child hierarchy, multi-level embedding | Existing `Vector<T>`, `Client.*`, `ChainBuilder` | Zero-config RAG with quality chunking. Immediate value. |
| **Phase 2: Concept Graph** | Entity extraction (lightweight strategy), entity resolution, `IConceptGraphStore` (in-memory adapter), concept_explore tool for agent | Phase 1 | Self-associative cross-document discovery. The differentiator. |
| **Phase 3: Agentic Retrieval** | Replace chain-based retrieval with agent-driven retrieval. Tool set, adaptive complexity, sufficiency check, Focus propagation. | Phase 2, AI-0031 (Agent) | Adaptive per-query strategy. 78% vs 34% on multi-hop. |
| **Phase 4: Operations** | `Rebuild()`, `Stats()`, `IsReady()`, `Clear()`, `RagIngestionState<T>`, `RagIngestionWorker`, `RagCorpusHealthCheck`, boot report, incremental update protocol | Phase 1 | Production-ready operations and observability. |
| **Phase 5: Composition** | `Rag.Compose()`, federated search, percentile normalization, cross-corpus reranking, authorization intersection | Phase 3 | Cross-domain queries. |
| **Phase 6: Evaluation** | `Evaluate()`, RAGAS metrics, HallucinationScore, ContextUtilization, trajectory capture, `Search()` for debugging | Phase 3 | Quality measurement and regression detection. |
| **Phase 7: ColPali** | Visual retrieval, multi-vector detection, layered capability activation, OCR gate bypass defense | Phase 1, multi-vector provider | Visual document understanding. |
| **Phase 8: Full Graph Strategy** | Relationship extraction, labeled edges, Full `GraphStrategy` option | Phase 2 | Explicit relationship traversal for ontological corpora. |
| **Phase 9: Embedding Adaptation** | `Adapt()`, synthetic training pair generation, corpus-scoped model, blue-green re-indexing | Phase 4, `AiCapability.Train` adapters | 27-44% domain retrieval improvement. |
| **Phase 10: Content Adapters** | `IContentAdapter`, multi-round protocol, built-in strategies, `StrategyGenerator`, `ContentAdapterRegistry` | Phase 1 | Multi-modal content understanding. |
| **Phase 11: Distillation Tree** | `IDistillationTreeStore`, `IClusteringStrategy`, `DistillationTreeBuilder`, UMAP + diagonal GMM, per-document and corpus-wide tree construction | Phase 2 | Multi-resolution retrieval. Cross-document thematic discovery. |
| **Phase 12: Advanced** | Lazy graph strategy, SQLite graph adapter, CAG for small corpora, RL trajectory training data, FLARE-style mid-generation retrieval hook | Prior phases | Future optimization and research integration. |

Phases 1-3 are the critical path. They deliver the core value proposition: load documents, get self-associative answers. Phases 4-6 make it production-ready and measurable. Phases 7-12 are progressive enhancements. Phases 10-11 are implemented as part of the initial delivery.

## Consequences

### Positive

- **Premium DX**: Three nouns (entity, corpus, answer), two verbs (ingest, ask), two modifiers (directive, focus). A developer can go from files to answers in three lines of code with zero configuration.
- **Domain-agnostic**: The identical mechanism works for Pokemon cards, healthcare policies, mathematical proofs, and legal contracts. No domain vocabulary in the architecture. The content declares its own connections.
- **Quality at scale**: Contextual chunking, parent-child hierarchy, concept graph, agentic retrieval with sufficiency checking, cross-encoder reranking. Multiple research-backed quality levers, all invisible to the developer.
- **Entity-native**: Corpora are typed to entities, declared via attributes, fed by lifecycle hooks. Fits naturally alongside `Entity<T>`, `Vector<T>`, and `Client` in the Koan ecosystem.
- **Builds on existing primitives**: `Vector<T>`, `Client.*` verbs, `ChainBuilder`, `IAugmentation`, `EmbeddingMetadata`, `[MediaAnalysis]`, `Modality`, `Client.Scope()`, `Agent` — all reused, not replaced.
- **Horizontally scalable**: Ingestion parallelizes across Zen Garden topology automatically. More GPUs = faster ingestion, lower query latency. Zero configuration.
- **Measurable quality**: Built-in evaluation framework with RAGAS metrics, hallucination detection, and context utilization. Quality regression is detectable and quantifiable.
- **Security by design**: Two-gate boundary model with trusted innards. Structural prompt separation at generation. Partition-scoped multi-tenancy. Composition uses permission intersection.

### Negative

- **Ingestion cost**: Full pipeline with entity extraction is significantly more expensive than flat chunking + embedding (~$40-60 vs ~$0.50 for 500 docs at lightweight graph tier, cloud pricing). Self-hosted cost is primarily GPU time (~2-3 hours with 5 GPUs).
- **Query latency**: Agentic retrieval with sufficiency checking adds latency over simple vector search. Typical 1.5-3.0s vs <1s for flat vector search. Acceptable for the target use case (developer asking compliance questions) but not suitable for sub-second user-facing search.
- **Complexity**: The internal pipeline (modality detection, parallel extraction, entity resolution, concept graph, agentic retrieval) is substantially more complex than flat RAG. The DX hides this complexity, but operational debugging requires understanding the pipeline.
- **New dependency surface**: `IConceptGraphStore`, `RagIngestionWorker`, `RagCorpusHealthCheck`, `RagIngestionState<T>` — new infrastructure components that must be maintained and tested.
- **Agentic non-determinism**: The same query may produce different retrieval paths across invocations. Acceptable in production (bounded by quality metrics), but requires strategy pinning for deterministic evaluation.

### Risks

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Entity resolution produces noisy graph over time | Medium | High | Merge-on-read design; confidence thresholds; periodic mark-and-sweep; graph density alerts |
| Agentic retrieval makes bad tool choices | Medium | Medium | Sufficiency check as self-correction loop; strategy pinning for eval; retrieval trace for debugging |
| Ingestion pipeline LLM rate limits | High | Medium | Zen Garden parallelism; backpressure signaling; batch queuing via `RagIngestionWorker` |
| ColPali bypasses text-based injection scanning | Medium | High | OCR scanning of all pages before ColPali encoding; both paths pass gate |
| Cross-corpus composition reveals information across partitions | Low | Critical | Permission intersection at Query Gate; federated search (no graph merge); partition-isolated graphs by default |
| Embedding adaptation model leaks corpus information | Low | Medium | Corpus-confidential artifact classification; same access controls as corpus |
| Concept graph cold-start time degrades at scale | Medium | Low | `IConceptGraphStore` interface with migration path to SQLite; boot report threshold warning |

## References

- **AI-0021** — Category-Driven AI with Convention-Inferred Defaults (category architecture, `Client.Scope()`)
- **AI-0026** — Chain Composition (fluent builder, `Retrieve<T>()`, `Parallel()`)
- **AI-0027** — Media Analysis Attribute (`[MediaAnalysis]` multi-flag processing)
- **AI-0031** — Agent Architecture (`Agent.Create()`, `WithTools()`, `WithAdapterTools()`)
- **AI-0032** — Recipes and Model Resolution Chain (named model bindings, resolution priority)
- **AI-0033** — Descriptive AI Capability Expansion (14 verbs, `Modality`, `IRerankAdapter`)
- **AI-0015** — Canonical Source-Member Architecture (priority-based routing, cloud fallback)
- **AI-0010** — Augmentation Pipeline (`IAugmentation` hooks)
- **AI-0025** — Prompt Primitives (immutable `Prompt` class, template variables)
- **ARCH-0070** — Attribute-Driven AI Embeddings (`[Embedding]`, `EmbeddingMetadata`, `EmbeddingWorker`)
- **ADR-0051** — Vector Hybrid Search (`Vector<T>.Search()` with alpha weighting)
- **AI-0018** — Chunk Size Hard Cap (1000-token ceiling, section splitting)

### Research

- **Technical Whitepaper**: [docs/archive/research/koan-rag-whitepaper.md](../archive/research/koan-rag-whitepaper.md) — Comprehensive description of the system architecture with abstraction checkpoints for non-specialist audiences, research citations, cost models, and novel contributions.
