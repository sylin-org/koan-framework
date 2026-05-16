# Koan.Data.AI

Data-layer AI integration for Koan: embeddings lifecycle, media analysis, and semantic search — all driven by convention, with attribute opt-in for automatic processing.

- Target framework: net10.0
- License: Apache-2.0
- Version: 0.6.3

## Install

```powershell
dotnet add package Sylin.Koan.Data.AI
```

## Core Principle

**Convention over configuration.** On-demand operations (embed a string, search semantically) work with zero attributes. Attributes are only needed to opt into **automatic** processing on entity save.

---

## Embeddings

### On-demand semantic search (no attributes required)

```csharp
// Embed and search without any attribute decoration
var results = await Entity.SemanticSearch<Article>("machine learning basics", limit: 10);
var similar  = await someArticle.FindSimilar(limit: 5);
```

### Auto-embed on save (opt-in with `[Embedding]`)

```csharp
public class Article : Entity<Article>
{
    public string Title   { get; set; } = "";
    public string Content { get; set; } = "";

    [EmbeddingIgnore]  // Exclude from embedding text
    public string InternalNotes { get; set; } = "";
}

// article.Save() → embedding computed and stored automatically
```

### `EmbeddingPolicy` — control text composition

| Value | Behaviour |
|-------|-----------|
| `AllStrings` | Embed all `string` properties (default) |
| `AllPublic` | Embed all public properties |
| `FullJson` | Serialize the whole entity to JSON |
| `Explicit` | Only embed properties you mark |

### Async embedding queue

Large or slow-to-embed entities use `EmbedJob<TEntity>` for background processing:

```csharp
// Queue for async embedding rather than blocking the request
await EmbedJob<Article>.QueueAsync(article.Id);

// Job status
EmbedJobStatus: Pending | Processing | Completed | Failed | FailedPermanent
```

---

## Media Analysis

`MediaAnalysisAttribute` opts an entity into automatic analysis when media files are associated with it (images, audio, video). The attribute is a lifecycle opt-in — on-demand operations work without it.

```csharp
[MediaAnalysis(MediaAnalysis.Describe | MediaAnalysis.Ocr)]
public class DocumentScan : Entity<DocumentScan>
{
    public string FileUrl       { get; set; } = "";
    public string Description   { get; set; } = "";  // Populated by Describe
    public string ExtractedText { get; set; } = "";  // Populated by Ocr
}
```

### `MediaAnalysis` flags

| Flag | What it does |
|------|-------------|
| `Describe` | Generate a natural-language description of the image |
| `Ocr` | Extract text from the image |
| `Transcribe` | Transcribe speech from audio/video |
| `Classify` | Classify content into categories |
| `Extract` | Extract structured data fields |

### `MediaAnalysisMetadata.Resolve<T>()`

```csharp
// Returns null if no [MediaAnalysis] attribute — no attribute = no auto-analysis
var meta = MediaAnalysisMetadata.Resolve<DocumentScan>();
if (meta is not null)
{
    // meta.Modes, meta.DescriptionProperty, meta.OcrTextProperty, etc.
}
```

---

## Reference

- **ADR**: `docs/decisions/AI-0021-category-driven-ai-with-convention-defaults.md`
- **Guide**: `docs/guides/ai-integration.md`
- **Sample**: `samples/S18.Prism/` (real-world media analysis usage)
- **Related**: `Koan.Data.Vector` for raw vector storage, `Koan.AI` for the chat/embed facade
