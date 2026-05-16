# Self-Associative RAG at Scale: Entity-Native Corpora, Hierarchical Distillation, and Emergent Knowledge Structures

**Koan Framework — Technical Whitepaper**
**Version 0.1 — April 2026**

---

## 1. Abstract

This paper describes a retrieval-augmented generation (RAG) architecture designed for enterprise knowledge systems operating over hundreds of heterogeneous documents. The system introduces three novel contributions: (1) entity-native corpora that bind RAG operations to typed domain models with zero-configuration convention inference, (2) a dual knowledge structure combining concept graphs with corpus-wide RAPTOR distillation trees for self-associative cross-document discovery, and (3) a multi-round content interpretation protocol with auto-generated classification strategies that adapts to arbitrary content types without domain-specific configuration.

The architecture processes text, PDFs, images, diagrams, and audio through a unified ingestion pipeline that extracts entities, builds hierarchical summaries, and enables retrieval at multiple levels of abstraction simultaneously. A developer loads files, asks questions, and receives answers that synthesize information across documents — including cross-references the developer never declared. The system discovers these connections from the content itself.

---

## 2. Introduction: The Cross-Reference Problem

> **CHECKPOINT: Why documents are harder than they look**
>
> Imagine you manage 200 healthcare policy documents. A developer asks: "How do I connect to external APIs?" The answer isn't in any single document. It requires information from an API integration guide, a PII handling policy, a HIPAA compliance manual, and a certified library catalog. These documents reference each other implicitly — the compliance policy says "all external data sources must comply with Section 7," and the API guide says "REST APIs are external data sources" — but no index, tag, or cross-reference explicitly connects them.
>
> Traditional search finds documents that *contain* your keywords. What you need is a system that understands how documents *relate to each other* — and can synthesize an answer from pieces scattered across your entire collection.

### 2.1 When Search Isn't Enough

Keyword search fails when the answer requires synthesis across documents. Semantic search (embedding similarity) improves relevance but still operates on individual chunks — it cannot discover that a chunk about "API integration" on page 47 of Document 3 is conceptually connected to a chunk about "data transfer compliance" on page 312 of Document 194.

The fundamental challenge is that enterprise knowledge is **distributed and implicitly connected**. The connections exist in the content — in shared terminology, referenced standards, overlapping requirements — but no single retrieval operation surfaces them.

### 2.2 The Scale Challenge

At scale (hundreds of documents, thousands of pages), three problems compound:

1. **Memory**: A 2,000-page regulatory document cannot be loaded into an AI model's context window. It must be broken into pieces — but naive breaking destroys the narrative structure that gives each piece meaning.

2. **Cross-reference density**: With 200 documents, the number of potential cross-references grows combinatorially. Manual tagging is infeasible. The system must discover connections automatically.

3. **Abstraction mismatch**: A question like "What are the key compliance themes across our regulatory corpus?" requires a corpus-level understanding that no individual chunk provides. The system needs the ability to answer at multiple levels of detail — from a specific policy clause to a thematic summary spanning dozens of documents.

---

## 3. Background: How AI-Powered Search Works

> **CHECKPOINT: RAG in plain English**
>
> Retrieval-Augmented Generation (RAG) is a technique where an AI model answers questions by first *searching* for relevant information, then *generating* an answer based on what it found. Think of it as giving the AI a research assistant: instead of answering from memory (which may be wrong or outdated), the AI looks up the relevant documents first, then writes its answer citing those documents.
>
> The quality of the answer depends entirely on the quality of the search. If the search misses a critical document, the AI cannot include it in its answer — no matter how capable the model is. This paper focuses on making the search as comprehensive and intelligent as possible.

### 3.1 Retrieval-Augmented Generation

RAG [Lewis et al., 2020] combines a retrieval system with a generative language model. The standard pipeline:

1. **Index**: Documents are split into chunks (typically 200-1000 tokens), each chunk is converted into a numerical vector (embedding) that captures its semantic meaning, and these vectors are stored in a searchable index.

2. **Retrieve**: When a user asks a question, the question is also converted to a vector, and the index returns the most semantically similar chunks.

3. **Generate**: The retrieved chunks are provided as context to a language model, which generates a natural-language answer grounded in the retrieved evidence.

### 3.2 The Limits of Flat Search

Standard RAG uses a "flat" index — every chunk is at the same level of abstraction, and retrieval is a single-pass similarity search. This has three well-documented failure modes:

**Lost context**: A chunk reading "employees must comply with section 4.2" is meaningless without the parent document's title, section hierarchy, and the content of section 4.2.

**No cross-document reasoning**: Flat search returns the N most similar chunks to the query. If the answer requires combining information from chunks that are semantically distant from each other (but connected through a reasoning chain), flat search fails.

**Abstraction mismatch**: A thematic question ("What are the key themes?") matches poorly against specific, granular chunks. A detailed question ("What is the TLS version requirement?") matches poorly against high-level summaries. Flat search operates at one granularity only.

### 3.3 Related Work

This system builds on several research foundations:

**RAPTOR** [Sarthi et al., ICLR 2024]: Recursive Abstractive Processing for Tree-Organized Retrieval. Constructs a hierarchical tree by clustering document chunks, summarizing each cluster, and recursively repeating. Demonstrated 20% absolute accuracy improvement on multi-step reasoning benchmarks. The collapsed tree retrieval strategy (searching all tree levels simultaneously) outperformed tree traversal.

**GraphRAG** [Edge et al., Microsoft Research, 2024]: Extracts entity-relationship knowledge graphs from documents, applies hierarchical community detection, and generates community summaries. Enables corpus-wide thematic queries that flat RAG cannot answer.

**Contextual Retrieval** [Anthropic, 2024]: Prepends a LLM-generated context summary to each chunk before embedding. The context describes which document the chunk comes from and what surrounding content discusses. Reduces retrieval failure rate by 67% when combined with hybrid search and reranking.

**LinearRAG** [ICLR 2026]: Achieves 80% of full GraphRAG quality using entity extraction and semantic linking alone — without explicit relationship extraction. Dramatically reduces ingestion cost.

**A-RAG** [Feb 2026]: Demonstrates that agentic retrieval (an AI agent with search tools that decides strategy per query) achieves 78% accuracy on complex multi-hop queries, versus 34% for fixed retrieval pipelines.

---

## 4. System Architecture

### 4.1 Entity-Native Corpora

> **CHECKPOINT: Your data model is the starting point**
>
> Most RAG systems treat documents as generic files — upload a PDF, get chunks. This system does something different: it binds RAG to your application's *data model*. If your application has a `Policy` entity with `Title`, `Content`, and `EffectiveDate` properties, the RAG system understands that structure. It knows which properties contain the text to search, it can filter by `EffectiveDate`, and it automatically updates the search index when a policy is created, modified, or deleted.
>
> This means zero configuration for the developer. Declare your data model, load your documents, ask questions. The system handles everything else.

#### 4.1.1 Typed Knowledge Pools

A corpus is scoped to an entity type and optionally named:

```
Rag.Corpus<Policy>()                                    // Default corpus
Rag.Corpus<Policy>("Medical", "Optimize for medical terminology")  // Named with directive
```

The entity type parameter is not cosmetic. It determines:
- Which properties contribute to the searchable text (convention inference)
- Which vector index stores the chunks (provider-transparent)
- Which entity lifecycle events trigger re-ingestion (automatic)
- Which partition scoping rules apply (multi-tenant isolation)

#### 4.1.2 Zero-Configuration Convention Inference

The system resolves extraction metadata from the entity type using a precedence chain:

1. **Explicit attribute configuration** (`[RagCorpus]` with properties) — if declared
2. **Naming conventions** — `Title`, `Name`, `Content` properties detected automatically
3. **Type scanning** — all string properties contribute by default

This follows the principle that operations work immediately without configuration. Attributes customize lifecycle behavior but never gate functionality.

#### 4.1.3 Lifecycle Integration

Entity create/update/delete events automatically trigger corpus updates:

- **Create/Update**: The entity's content is extracted, chunked, embedded, and added to the concept graph. If the content hasn't changed (detected via SHA-256 signature), the update is skipped.
- **Delete**: Chunks are removed from the vector index. Entity mentions are decremented in the concept graph. Entities reaching zero mentions are pruned.

This means the corpus is always current — no batch reindex jobs unless the developer explicitly requests one.

---

### 4.2 Multi-Round Content Interpretation

> **CHECKPOINT: Not all content is text**
>
> A single PDF might contain text pages, scanned images, architecture diagrams, data tables, and embedded charts. A single-pass "describe this image" produces: "A diagram showing boxes and arrows." That's technically accurate and completely useless for search.
>
> Our system uses a multi-round protocol: first, it classifies *what kind of content* this is (a network architecture diagram). Then, using a strategy designed for that content type, it deeply interprets the content (extracts every component, connection, data flow, and failure path). Finally, it enriches the interpretation with implicit information (security assumptions, scalability implications). The result is a rich text representation that captures the *meaning* of the content — not just what it looks like.

#### 4.2.1 The Classification → Interpretation → Enrichment Protocol

Each piece of non-text content goes through three rounds:

**Round 1 — Classification**: A vision model identifies the content type using a hierarchical category system (e.g., `diagram/architecture`, `table`, `chart/line`, `photograph/object`). This classification determines which interpretation strategy to apply.

**Round 2 — Interpretation**: An interpretation strategy — a detailed prompt template designed for this specific content type — guides the AI to extract structured information. For an architecture diagram, this means: components, connections, data flows, failure paths, and security boundaries. For a table: headers, row-as-assertions, relationships between columns, and trends.

**Round 3 — Enrichment** (optional): A follow-up pass extracts implicit information not covered by interpretation: constraints enforced by the design, single points of failure, scalability implications, assumptions.

#### 4.2.2 Strategy Resolution

The system resolves interpretation strategies through a three-tier hierarchy:

1. **Pre-determined strategies**: Eight built-in strategies for known content types (architecture diagrams, sequence diagrams, tables, charts, forms, code, photographs, screenshots). These are optimized prompt templates designed to extract the maximum meaningful information from each content type.

2. **Auto-generated strategies**: When the classifier identifies a content type not covered by built-in strategies (e.g., a musical score, an X-ray, a circuit board layout), the system generates a custom strategy using the highest-quality reasoning model available. The model designs a bespoke interpretation approach based on what it sees. This strategy is cached per corpus so the generation cost is paid once per novel content type.

3. **Corpus-cached strategies**: After an auto-generated strategy is used multiple times, it is promoted to a persistent strategy for that corpus. The system learns its own content types.

#### 4.2.3 Best-of-Breed Model Routing

Different extraction tasks have different optimal models. The pipeline routes each stage to the best available model:

| Stage | Optimal Model Characteristic | Example |
|-------|-----|---------|
| Classification | Fast, cheap | llama-8b |
| Strategy Generation | Best reasoning | opus, llama-70b |
| Interpretation | Strong vision | llava-next, pixtral |
| Entity Extraction | Strong reasoning | llama-70b |
| Embedding | Domain-appropriate | nomic-embed-text |

The developer doesn't configure this routing unless they want to override it. The system uses whatever models are available, falling through a priority chain from local to cloud.

---

### 4.3 Contextual Chunking with Hierarchical Distillation

> **CHECKPOINT: Understanding documents you can't read all at once**
>
> Imagine a 2,000-page regulation. No AI model can process all 2,000 pages at once. We have to break it into pieces — but how we break it determines how well the system can answer questions about it.
>
> Our approach works like a textbook: the document is divided into overlapping sections (so nothing falls between the cracks), then groups of sections are summarized into chapter summaries, chapters are summarized into part summaries, and eventually the whole document is captured in a single abstract. Every level — from individual paragraph to full-document abstract — is searchable. A specific question ("What is the TLS version requirement?") finds the paragraph. A broad question ("What are the key compliance themes?") finds the chapter summary.
>
> The same process then runs across the *entire corpus*. Paragraphs from different documents that discuss the same topic (say, HL7 data exchange) are grouped together and summarized — even though they're from completely different documents. This is how the system discovers that Document 3, Document 54, and Document 194 are all talking about the same thing.

#### 4.3.1 Overlapping Window Segmentation

Documents are divided into fixed-size windows with 15% overlap [validated by NVIDIA FinanceBench, Vectara NAACL 2025]. The overlap ensures that every narrative thread at a window boundary appears in at least two adjacent windows. This is more robust than semantic boundary detection, which NAACL 2025 research showed does not reliably outperform fixed-size windowing with overlap [Vectara, 2025].

Child chunks (300 tokens, embedded for precision matching) nest within parent chunks (1,200 tokens, returned for context preservation). When a search matches a child chunk, the parent chunk is returned to the user — providing surrounding context without inflating the search index.

#### 4.3.2 Contextual Prefix Generation

Following the Anthropic Contextual Retrieval pattern [2024], each chunk receives a prepended context summary describing its provenance:

*"This chunk is from the HIPAA Data Handling Policy, Section 7 — External API Integrations, discussing requirements for data transfer to third-party systems."*

This summary is generated once per document (not per chunk) and prepended to all chunks from that document. The cost is one LLM call per document; the benefit is a 67% reduction in retrieval failures [Anthropic, 2024].

#### 4.3.3 RAPTOR Tree Construction

After chunking, the system builds a hierarchical distillation tree using the RAPTOR algorithm [Sarthi et al., 2024] adapted for corpus-wide operation:

1. **Embed**: All leaf chunks are converted to embedding vectors (already done during ingestion).

2. **Reduce dimensionality**: UMAP [McInnes et al., 2018] projects embeddings from 768 dimensions to 10, preserving local neighborhood structure while eliminating noise dimensions that destabilize clustering.

3. **Cluster**: Gaussian Mixture Models (GMM) with diagonal covariance group semantically similar chunks. Soft clustering allows a chunk to appear in multiple clusters — a chunk about "HL7 PII requirements" belongs to both the "HL7" cluster and the "PII handling" cluster. Bayesian Information Criterion (BIC) automatically determines the optimal number of clusters.

4. **Summarize**: Each cluster is summarized by an LLM, producing a single text that captures the key information from its member chunks.

5. **Recurse**: Summaries are re-embedded and the cluster-summarize cycle repeats. Each level produces fewer, more abstract summaries. The process terminates when the corpus is captured in a small number of root-level summaries.

#### 4.3.4 Adaptive Depth

The number of tree levels adapts to corpus size:

```
depth = min(ceil(log(chunkCount) / log(clusterFactor)), maxDepth)
```

| Corpus Size | Chunks | Tree Depth | Total Nodes |
|---|---|---|---|
| Single short document | 50 | 2 | ~55 |
| Single long document | 500 | 3 | ~555 |
| Small corpus (50 docs) | 10,000 | 4 | ~11,111 |
| Medium corpus (200 docs) | 50,000 | 5 | ~55,555 |
| Large corpus (1,000 docs) | 250,000 | 5 | ~277,778 |

The cluster factor (default 10) is configurable. Larger factor = fewer levels = cheaper tree but coarser summaries. Tree depth is capped at 5 levels regardless of formula output, as research indicates that summaries-of-summaries beyond 4-5 levels degrade in information density.

#### 4.3.5 Corpus-Wide Cross-Document Clustering

The distillation tree operates across document boundaries. When built at corpus level, chunks from different documents that discuss the same topic are clustered together and summarized jointly.

For example, in a corpus of 200 regulatory documents:
- Document 1 (page 47), Document 3 (page 112), Document 54 (page 8), and Document 194 (page 203) all contain chunks about HL7 data exchange.
- These chunks are clustered together at Level 1, regardless of their source document.
- The Level 1 summary reads: "Four regulatory documents address HL7 data exchange requirements, covering message formatting (Doc 1), real-time clinical data feeds (Doc 3), integration testing requirements (Doc 54), and cross-border data transfer provisions (Doc 194)."
- A query about "HL7 best practices" matches this summary and retrieves synthesized information from all four documents — without any manual cross-referencing or tagging.

This is the mechanism that makes the system self-associative: the content declares its own connections through semantic similarity at the embedding level, surfaced through hierarchical clustering.

---

### 4.4 Emergent Knowledge Structures

> **CHECKPOINT: How the system discovers connections you didn't declare**
>
> Two independent structures capture different kinds of connections between documents:
>
> The **concept graph** captures *explicit relationships* between named things. "HIPAA" is connected to "PII" which is connected to "encryption at rest." When you ask about one, the system follows the connections to find related concepts — like a web of linked Wikipedia articles.
>
> The **distillation tree** captures *thematic similarity* across documents. Documents that discuss related topics cluster together, even if they never reference each other by name. A regulation about data privacy and a technical guide about encryption end up in the same cluster because they're about the same underlying concern.
>
> Together, they provide two complementary paths to find relevant information: follow the named connections (graph), or find the thematic neighborhood (tree). A query benefits from both simultaneously.

#### 4.4.1 Concept Graph (Entity Extraction + Resolution)

The concept graph is built through a three-stage pipeline:

**Extraction**: An LLM extracts entities (named concepts, standards, rules, categories) and facts (self-contained assertions) from each chunk. The extraction prompt is domain-agnostic — the same prompt works for healthcare policies, Pokemon cards, and mathematical proofs. An optional natural-language directive provides domain hints.

**Resolution**: Extracted entities are resolved against existing entities using a tiered strategy:
1. Exact string match with normalization (60-70% of duplicates)
2. Embedding similarity above threshold (20%)
3. LLM-assisted disambiguation for ambiguous cases (10%)

Resolution uses a merge-on-read design: original surface forms are preserved, and the canonical mapping is a separate lookup. False merges can be undone by removing the mapping entry.

**Graph construction**: Resolved entities and their relationships form a graph that grows incrementally as documents are ingested. Entity mentions are reference-counted to their source documents. When a document is deleted, mentions are decremented; entities reaching zero mentions are pruned.

#### 4.4.2 Distillation Tree (Thematic Hierarchy)

The RAPTOR distillation tree (Section 4.3.3) provides a complementary structure: progressive summaries organized by thematic similarity rather than explicit relationships. Where the concept graph answers "what is X connected to?", the distillation tree answers "what does the corpus say about the general area around X?"

#### 4.4.3 Complementary Retrieval: Graph × Tree

The two structures answer different query types:

| Query Type | Concept Graph | Distillation Tree |
|---|---|---|
| "What is HL7 connected to?" | Direct traversal: HL7 → FHIR → interoperability | Not directly useful |
| "What does the corpus say about HL7?" | Limited | Level 1-2 summary for the HL7 cluster |
| "How do I connect to external APIs?" | Graph traversal: API → compliance → PII → HIPAA | Tree: Level 2 cluster connecting API and compliance themes |
| "What are the key themes?" | Cannot answer (no aggregation) | Root-level summaries |
| "What is the TLS version requirement?" | Entity lookup | Leaf chunk match |

At query time, both structures are consulted. The concept graph contributes connected entities and their descriptions. The distillation tree contributes thematic summaries at the appropriate abstraction level. The combined context is richer than either alone.

---

### 4.5 Retrieval: Finding What Matters

> **CHECKPOINT: How the system decides what's relevant**
>
> When you ask a question, the system doesn't just find the most similar text. It uses multiple strategies — like an experienced researcher who knows when to search by keyword, when to browse related topics, and when to check the table of contents.
>
> Simple questions ("What is the TLS version?") get a quick, direct search. Complex questions ("How do data privacy requirements affect our API architecture?") trigger multiple rounds of searching: find relevant chunks, discover connected concepts in the knowledge graph, explore thematic summaries at different levels of detail, and assess whether the accumulated context is sufficient before generating an answer.
>
> The system adapts its search strategy to each question automatically. The developer never configures this — they ask, and the system decides how to search.

#### 4.5.1 Collapsed Tree Search (Multi-Resolution)

Following RAPTOR's finding that collapsed tree retrieval outperforms tree traversal [Sarthi et al., 2024], all tree levels are stored in the same vector index alongside leaf chunks. A single similarity search returns results at the natural granularity for each query:

- A factoid query ("What is the TLS version requirement?") matches a leaf chunk with high similarity.
- A thematic query ("What are the key compliance themes?") matches a Level 3-4 summary.
- A cross-document query ("How do these regulations address data transfer?") matches Level 1-2 cluster summaries that span multiple documents.

No routing decision is needed. The embedding similarity naturally selects the appropriate level. RAPTOR's evaluation showed that 18-57% of retrieved nodes come from non-leaf layers, confirming that the tree provides value beyond flat chunking.

#### 4.5.2 Concept Graph Exploration

After initial vector search, the system explores the concept graph for related entities. Entities mentioned in the retrieved chunks serve as seed nodes; the graph is traversed to a configurable depth to discover connected concepts and their descriptions.

This addresses the "implicit connection" problem: a chunk about API integration doesn't mention PII handling, but the concept graph connects "external API" → "external data source" → "PII requirements" → "HIPAA." The traversal surfaces the PII handling context that flat search would miss.

#### 4.5.3 Adaptive Complexity per Query

The retrieval pipeline adapts its strategy per query. Simple queries use a single search round. Complex queries trigger multiple rounds with different tools (semantic search, keyword search, concept graph exploration, multi-level tree search). A sufficiency check after each round determines whether the accumulated context is adequate to answer the question — or whether additional search is needed.

#### 4.5.4 Structural Prompt Separation (Security)

Retrieved content is placed in tool-result or user message roles in the generation prompt — never in the system role. This structural separation using the model API's message types (not text-based markers) is the primary defense against retrieval-path prompt injection, where adversarial content embedded in documents could manipulate the generation model's behavior.

---

### 4.6 Quality Measurement

> **CHECKPOINT: How we know the answers are right**
>
> An AI system that confidently delivers wrong answers is worse than one that says "I don't know." We measure answer quality across multiple dimensions: Is the answer supported by the retrieved documents? (Faithfulness.) Does it actually address the question? (Relevancy.) Does it contain claims that aren't in any source? (Hallucination.) Did the search find the right documents? (Recall.) Were the found documents actually used? (Utilization.)
>
> These metrics run against test sets — known question-answer pairs that the system should get right. When a new version of the system is deployed, the test set catches regressions before users encounter them.

#### 4.6.1 Metrics

The evaluation framework implements RAGAS [Es et al., 2023] metrics supplemented with hallucination detection and context utilization:

| Metric | What It Measures | Method |
|---|---|---|
| **Faithfulness** | Does the answer follow the retrieved context? | LLM-as-judge: evaluate claims against sources |
| **Answer Relevancy** | Does the answer address the question? | LLM-as-judge: relevance scoring |
| **Context Precision** | Are the retrieved chunks relevant? | Source relevance score aggregation |
| **Context Recall** | Did retrieval find all relevant sources? | Comparison against expected source IDs |
| **Hallucination Score** | Are there unsupported claims? | LLM-as-judge: compare against reference answer |
| **Context Utilization** | Were retrieved chunks actually used? | Ratio of high-relevance to total sources |

#### 4.6.2 Test-Driven Quality

Evaluation runs against curated test sets — a practice adapted from software testing. Each test case specifies a query, an expected answer (optional), and expected source document IDs (optional). The system generates answers and metrics per case, then aggregates to corpus-level scores.

Failed test cases are isolated — a single failure does not abort the evaluation run. This enables regression detection: when a system change degrades one metric (e.g., faithfulness drops on compliance questions after a chunking parameter change), the specific affected test cases identify the regression.

---

## 5. Cost & Performance

> **CHECKPOINT: What this actually costs to run**
>
> The short answer: for a corpus of 200 regulatory documents (~50,000 pages), full ingestion with all features costs about $12 using cloud AI services, or approximately $0.25 and 2-3 hours of GPU time if you run AI models locally. After the initial ingestion, each question is answered in 1.5-3 seconds. Adding new documents costs proportionally less because the system only processes what changed.

### 5.1 Ingestion Cost Model

For a corpus of 500 documents averaging 10 pages each (15 million tokens total):

| Component | Self-Hosted Cost | Cloud Cost | Time (1 GPU) |
|---|---|---|---|
| Content extraction (multi-round) | ~$0.01 | ~$5-15 | 45 min |
| Contextual chunking + embedding | ~$0.01 | ~$1.20 | 15 min |
| Entity extraction (Lightweight graph) | ~$0.10 | ~$40-60 | 2-3 hours |
| RAPTOR tree construction | ~$0.23 | ~$11.50 | 93 min |
| **Total** | **~$0.35** | **~$58-88** | **~5 hours** |

With 5 GPUs via distributed compute: ~1-2 hours total. The dominant cost is LLM inference for entity extraction and tree summarization.

### 5.2 Query Latency

| Stage | Latency |
|---|---|
| Query embedding | 15-30ms |
| Hybrid vector search | 20-50ms |
| Concept graph exploration | 5-30ms |
| Cross-encoder reranking | 80-200ms |
| LLM generation | 500-2000ms |
| **Total (typical)** | **1.5-3.0s** |

### 5.3 Incremental Updates

When 10 new documents are added to an existing 200-document corpus:

| Operation | Full Rebuild | Incremental | Savings |
|---|---|---|---|
| LLM calls | 23,333 | 560-2,225 | 90-98% |
| Wall clock (1 GPU) | 97 min | 3-9 min | 91-97% |

Incremental updates assign new chunks to existing cluster centroids, re-summarize only affected clusters, and propagate changes upward through the tree. Content-hash diffing (SHA-256 signature comparison) skips unchanged documents entirely.

### 5.4 Scaling Characteristics

| Corpus Size | Chunks | Tree Build | Memory | Query Latency |
|---|---|---|---|---|
| 50 documents | 10K | 9 min | ~100MB | 1.2s |
| 200 documents | 50K | 45 min | ~400MB | 1.8s |
| 1,000 documents | 250K | 4 hours | ~1.5GB | 2.5s |

Ingestion scales linearly with document count. Query latency scales logarithmically (HNSW index). Memory scales linearly with chunk count and tree node count.

---

## 6. Design Decisions & Trade-offs

### 6.1 Quality Over Speed

The system is designed for use cases where answer quality is more important than response latency. A 2-second answer that correctly cites three relevant policies from different documents is preferable to a 200ms answer that only finds one.

This manifests in several choices: multi-round content interpretation (3 LLM calls per image instead of 1), RAPTOR tree construction (22K+ summarization calls per corpus build), and concept graph entity resolution (LLM-assisted disambiguation for ambiguous cases).

### 6.2 Domain Agnosticism

No domain vocabulary appears in the architecture. The same extraction prompts, clustering algorithms, and retrieval strategies work for healthcare policies, Pokemon card collections, and mathematical proofs. Domain adaptation is achieved through natural-language directives ("Optimize for medical terminology"), not through code changes or configuration schemas.

### 6.3 Convention Over Configuration

The system works with zero configuration. Entity properties contribute to searchable text by convention. Chunking parameters have researched defaults. Model routing uses whatever is available. The developer's minimum interaction is three lines of code: declare the entity, ingest files, ask questions.

Configuration is available at every level as an escape hatch — but it's never required.

### 6.4 Security Boundary Model

The system uses a two-gate security model:

**Ingestion Gate**: Validates file content (magic bytes, size limits), scans for prompt injection patterns, classifies data sensitivity using a local-only model (before any cloud routing decision), and sanitizes active content.

**Query Gate**: Authenticates the caller, verifies partition-scoped authorization (including intersection-of-permissions for cross-corpus composition), logs every query for audit, and filters responses based on authorization level.

Between the gates, the pipeline is trusted. Internal components (chunking, embedding, entity resolution, graph construction, generation) trust each other. This simplifies the architecture while concentrating security hardening at the two boundary surfaces.

---

## 7. Novel Contributions

### 7.1 Entity-Native RAG

No existing RAG framework binds retrieval operations to typed domain models. The `Rag.Corpus<T>()` pattern — where the generic type parameter determines extraction conventions, vector storage, lifecycle hooks, and partition scoping — is original to this system.

### 7.2 Auto-Generated Content Interpretation Strategies

Existing multi-modal RAG systems use fixed extraction pipelines (OCR + describe). The multi-round classification → interpretation → enrichment protocol with auto-generated strategies for novel content types — using the highest-quality reasoning model for strategy generation — is a new pattern. The corpus-cached strategy promotion (learning content types from repeated encounters) extends this further.

### 7.3 Dual Knowledge Structures

The combination of a concept graph (structural entity relationships) with a corpus-wide RAPTOR distillation tree (thematic hierarchy from embedding clustering) as complementary retrieval structures operating on the same corpus is, to our knowledge, not described in prior work. Each structure addresses failure modes the other cannot: the graph handles explicit cross-references, the tree handles implicit thematic connections.

### 7.4 Corpus-Wide RAPTOR with Incremental Maintenance

The original RAPTOR paper operates within single documents. This system extends RAPTOR to corpus-wide cross-document clustering with incremental maintenance — persisting cluster centroids and membership for efficient updates when documents are added, and version-stamped tree nodes for atomic corpus-tree swaps without index duplication.

---

## 8. Future Work

### 8.1 Agentic Retrieval with RL-Trained Agents

The current retrieval pipeline uses a chain-based approach. Future work will replace this with a full agentic retrieval loop where an AI agent has access to search tools and decides which to use per query. Research on RL-trained search agents [Search-R1, R1-Searcher, 2025] shows 20-41% improvement over prompt-engineered agents — the system's clean tool interfaces are designed to be RL-trainable.

### 8.2 ColPali Visual Retrieval

ColPali [arXiv 2407.01449] embeds document page images directly using a Vision Language Model, producing multi-vector representations that preserve visual layout. This would complement the text-based extraction pipeline by enabling visual similarity search — matching a query against the visual appearance of document pages, tables, and diagrams without text extraction.

### 8.3 Embedding Adaptation

Corpus-scoped embedding fine-tuning: generate synthetic query-document pairs from corpus content, fine-tune the embedding model on those pairs, and re-index. Research shows 27-44% improvement in domain-specific retrieval [CustomIR, arXiv 2510.21729; Financial domain, arXiv 2512.08088].

### 8.4 Long-Context Hybrid Mode

For small corpora that fit within a model's context window (< 200K tokens), retrieval can be bypassed entirely in favor of full-context processing with precomputed KV caches [TurboRAG, Cache-Craft, 2025-2026]. The system can auto-detect corpus size and select the optimal strategy.

---

## 9. References

- Anthropic. "Introducing Contextual Retrieval." September 2024. https://www.anthropic.com/news/contextual-retrieval
- Edge, D. et al. "From Local to Global: A Graph RAG Approach to Query-Focused Summarization." Microsoft Research, 2024. arXiv:2404.16130.
- Es, S. et al. "RAGAS: Automated Evaluation of Retrieval Augmented Generation." 2023. https://docs.ragas.io
- Lewis, P. et al. "Retrieval-Augmented Generation for Knowledge-Intensive NLP Tasks." NeurIPS, 2020.
- McInnes, L. et al. "UMAP: Uniform Manifold Approximation and Projection for Dimension Reduction." 2018. arXiv:1802.03426.
- Sarthi, P. et al. "RAPTOR: Recursive Abstractive Processing for Tree-Organized Retrieval." ICLR, 2024. arXiv:2401.18059.
- Search-R1. "Training LLMs to Reason and Leverage Search Engines with Reinforcement Learning." March 2025. arXiv:2503.09516.
- Vectara. "Chunking Strategies for RAG: A 2025 Benchmark." NAACL, 2025.
- LinearRAG. "Linear Graph Retrieval Augmented Generation on Large-scale Corpora." ICLR, 2026. arXiv:2510.10114.
- A-RAG. "Scaling Agentic Retrieval-Augmented Generation via Hierarchical Retrieval Interfaces." February 2026. arXiv:2602.03442.
- ColPali. "Vision Language Model Retrieval." 2024. arXiv:2407.01449.
- CustomIR. "Unsupervised Embedding Fine-Tuning for Enterprise RAG." 2025. arXiv:2510.21729.
- TurboRAG. "Accelerating Retrieval-Augmented Generation with Precomputed KV Caches." 2025.
- RAGRouter-Bench. "No Single RAG Paradigm is Universally Optimal." January 2026. arXiv:2602.00296.
- LLM x MapReduce. "Processing Long Documents with LLMs." ACL, 2025. arXiv:2410.09342.
