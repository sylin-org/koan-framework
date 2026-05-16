# S18.Prism — Personal Knowledge Intelligence

> Raw information in, structured knowledge out.
> One person. Many domains. Each with its own tuned intelligence.

## Purpose

S18.Prism is the **dogfood sample** for the Koan.AI lifecycle expansion (AI-0022 through AI-0031). It exercises every new capability in a single, coherent application that delivers genuine personal value: a self-hosted knowledge system where each domain of the user's life gets its own AI stack, tuned to their vocabulary and interests.

### Koan Capabilities Exercised

| ADR | Capability | How Prism Uses It |
|-----|-----------|------------------|
| AI-0023 | `Model.*` | Model index (crawled ModelCards as entities), per-space model selection, pull/convert/deploy, history/rollback |
| AI-0024 | `Compute.*` | Local GPU detection, VRAM-aware model placement, network delegation for training |
| AI-0025 | `Prompt()` | Per-space prompts, per-lens prompts, A/B testing on summarization styles |
| AI-0026 | `Chain.*` | RAG chains per lens (Brief, Find, Write, Research), branching by content type |
| AI-0027 | `[MediaAnalysis]` | All 5 modes: Vision (photos), OCR (screenshots/PDFs), Transcribe (voice/video), Classify, Extract |
| AI-0028 | `Training.*`, `Dataset.*` | Per-space LoRA fine-tuning from user corrections, Dataset.From<Note>(), Training.Compare() |
| AI-0029 | `Eval.*` | Eval.Drift() on incoming content, Eval.Compare() for model upgrades, quality gates |
| AI-0030 | `Review.*` | Research Brief review queues (approve/dismiss findings), content correction queues |
| AI-0031 | `Agent.*` | Setup agent (model discovery), research agent (deep investigation), per-space Q&A agent |
| MEDIA-0001 | `MediaEntity<T>` | Note inherits MediaEntity — stores raw files with derivatives |
| STOR-0001/0010 | Storage + Replication | File storage with local cache tier, offline capability |

### Existing Koan Capabilities Exercised

| Capability | How Prism Uses It |
|-----------|------------------|
| `Entity<T>` | All domain models are Koan entities with static methods |
| `EntityController<T>` | REST API auto-generated for all entities |
| `[Embedding]` | Notes, ModelCards, Research Findings — all embedded and vector-searchable |
| `Vector<T>.Search()` | Semantic search across notes, cross-space search, model discovery |
| `Client.Chat/Embed/Ocr/Stream` | All AI inference via standard Client surface |
| `Client.Scope()` | Per-space model routing (different models per space) |
| `[StorageBinding]` | Per-space storage profiles |
| `MediaEntity<T>` | Notes are media entities (any file format) |
| `ReplicatedStorageProvider` | Local cache + durable tier for offline use |
| `ZenGarden` | Network compute discovery for training delegation |
| `KoanAutoRegistrar` | All modules auto-registered via Reference = Intent |

---

## Bounded Contexts

Prism follows DDD with clear bounded contexts. Each context has its own folder, its own entities, its own services. Cross-context communication uses domain events and shared references (IDs, not entity instances).

```
S18.Prism/
├── Knowledge/          ← Core domain: Notes, ContentBlocks, search
├── Spaces/             ← Space management, model routing, privacy
├── Ingestion/          ← Universal loader, content extraction
├── Sources/            ← Passive feeds (RSS, GitHub, podcasts, etc.)
├── Research/           ← Active research briefs, findings, crawling
├── ModelIndex/         ← Model card crawling, local model catalog
├── Interaction/        ← Lenses, Pulse view, user Q&A
├── Learning/           ← Training loop, corrections, per-space LoRA
├── Setup/              ← Onboarding agent, space configuration
```

### Context Map

```
                    ┌──────────────┐
         ┌────────►│  Knowledge   │◄────────┐
         │         │  (Notes,     │         │
         │         │   Blocks,    │         │
         │         │   Search)    │         │
         │         └──────┬───────┘         │
         │                │                 │
    NoteCreated      NoteCreated       NoteCreated
    event             event             event
         │                │                 │
   ┌─────┴──────┐  ┌──────┴───────┐  ┌─────┴──────┐
   │ Ingestion  │  │   Sources    │  │  Research   │
   │ (Loader,   │  │ (RSS, Git,   │  │ (Briefs,   │
   │  Extract)  │  │  Podcast)    │  │  Crawl)    │
   └────────────┘  └──────────────┘  └─────┬──────┘
                                           │
                                     ┌─────┴──────┐
                                     │ ModelIndex  │
                                     │ (Cards,    │
                                     │  Crawl)    │
                                     └────────────┘
         │                                 │
   ┌─────┴──────┐                   ┌──────┴───────┐
   │   Spaces   │                   │   Learning   │
   │ (Config,   │                   │ (Training,   │
   │  Models,   │                   │  LoRA,       │
   │  Routing)  │                   │  Eval)       │
   └─────┬──────┘                   └──────────────┘
         │
   ┌─────┴──────┐         ┌──────────────┐
   │Interaction │         │    Setup     │
   │ (Lenses,   │         │ (Onboard,   │
   │  Pulse,    │         │  Agent)     │
   │  Q&A)      │         └──────────────┘
   └────────────┘
```

---

## Domain Models

### Knowledge Context

The core domain. Notes are the atoms of knowledge.

```csharp
// ═══════════════════════════════════════════════════════════
// Knowledge/Models/Note.cs
// ═══════════════════════════════════════════════════════════

[StorageBinding(Profile = "knowledge")]
[MediaAnalysis(Analysis = MediaAnalysis.All, Async = true)]
[Embedding(Policy = EmbeddingPolicy.Explicit, Async = true, Version = 1)]
public class Note : MediaEntity<Note>, IReviewable
{
    // ── Identity ──
    public string? Title { get; set; }
    public string SpaceId { get; set; } = string.Empty;
    public NoteOrigin Origin { get; set; }

    // ── Extracted content (universal representation) ──
    public List<ContentBlock> Blocks { get; set; } = [];

    // ── AI enrichment (populated by [MediaAnalysis]) ──
    public string? Summary { get; set; }
    public List<string> KeyConcepts { get; set; } = [];
    public string? Category { get; set; }
    public List<string> People { get; set; } = [];
    public List<string> ActionItems { get; set; } = [];

    // ── Media analysis outputs (populated by [MediaAnalysis]) ──
    public string? AiDescription { get; set; }
    public string? OcrText { get; set; }
    public string? Transcript { get; set; }

    // ── Search ──
    public float[]? Embedding { get; set; }

    // ── Source tracking ──
    public string? SourceId { get; set; }
    public string? SourceUrl { get; set; }
    public DateTime? SourcePublishedAt { get; set; }
    public string? ExtractorUsed { get; set; }

    // ── User feedback ──
    public int? UserRating { get; set; }
    public ReviewStatus ReviewStatus { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? RejectionReason { get; set; }

    // ── Tags ──
    public List<string> Tags { get; set; } = [];

    public string ToEmbeddingText()
    {
        var parts = new List<string>();
        if (Title != null) parts.Add(Title);
        if (Summary != null) parts.Add(Summary);
        if (AiDescription != null) parts.Add(AiDescription);
        if (OcrText != null) parts.Add(OcrText);
        if (Transcript != null) parts.Add(Transcript);
        foreach (var block in Blocks.Where(b => b.Kind == ContentKind.Text))
            parts.Add(block.Content);
        parts.AddRange(KeyConcepts);
        parts.AddRange(Tags);
        return string.Join("\n", parts);
    }
}

public enum NoteOrigin
{
    Upload,         // User dropped in a file
    Capture,        // Screenshot, voice memo, clipboard
    Source,         // Pulled from a Source (RSS, GitHub, etc.)
    Research,       // Found by a Research Brief
    Generated,      // Digest, synthesis, agent response
    Import          // Bulk import (Obsidian, Zotero, bookmarks)
}
```

```csharp
// ═══════════════════════════════════════════════════════════
// Knowledge/Models/ContentBlock.cs
// ═══════════════════════════════════════════════════════════

/// The universal intermediate representation.
/// Every file, regardless of format, becomes a sequence of ContentBlocks.
public sealed record ContentBlock
{
    public ContentKind Kind { get; init; }
    public string Content { get; init; } = string.Empty;
    public string? StructuredContent { get; init; }
    public int Order { get; init; }
    public ContentSource Source { get; init; } = ContentSource.Empty;
    public Dictionary<string, string> Meta { get; init; } = [];
}

public enum ContentKind
{
    Text,
    Table,
    Image,
    Audio,
    Data
}

public sealed record ContentSource
{
    public string? FileName { get; init; }
    public string? MimeType { get; init; }
    public string? Section { get; init; }
    public string? Extractor { get; init; }

    public static ContentSource Empty => new();
}
```

### Spaces Context

Privacy boundaries with per-space AI configuration.

```csharp
// ═══════════════════════════════════════════════════════════
// Spaces/Models/Space.cs
// ═══════════════════════════════════════════════════════════

public class Space : Entity<Space>
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Icon { get; set; }
    public SpaceAccess Access { get; set; } = SpaceAccess.Private;
    public List<string> MemberIds { get; set; } = [];
    public SpaceModels? Models { get; set; }
    public int NoteCount { get; set; }
    public DateTime? LastActivityAt { get; set; }
}

public enum SpaceAccess
{
    Private,
    Shared
}

/// Per-space AI model configuration.
/// Null fields inherit from the system default.
public sealed record SpaceModels
{
    public ModelRef? Chat { get; init; }
    public ModelRef? Embed { get; init; }
    public ModelRef? Vision { get; init; }
    public ModelRef? Ocr { get; init; }
    public ModelRef? Code { get; init; }
    public string? ChatLora { get; init; }
    public string? EmbedLora { get; init; }
}
```

```csharp
// ═══════════════════════════════════════════════════════════
// Spaces/Services/SpaceScopeService.cs
// ═══════════════════════════════════════════════════════════

/// Wraps Client.Scope() for the active space's model configuration.
public interface ISpaceScopeService
{
    IDisposable Enter(Space space);
    Space? Current { get; }
}

// Implementation sets Client.Scope() based on space.Models,
// loading the appropriate LoRA adapters for embed/chat.
```

### Ingestion Context

The universal loader. Any file format → ContentBlocks.

```csharp
// ═══════════════════════════════════════════════════════════
// Ingestion/Abstractions/IContentExtractor.cs
// ═══════════════════════════════════════════════════════════

public interface IContentExtractor
{
    string[] SupportedMimeTypes { get; }
    int Priority { get; }
    Task<IReadOnlyList<ContentBlock>> ExtractAsync(
        Stream content, string mimeType, ExtractOptions options,
        CancellationToken ct = default);
}

public sealed record ExtractOptions
{
    public string? FileName { get; init; }
    public int MaxTokensPerBlock { get; init; } = 1000;
    public bool IncludeImages { get; init; } = true;
}
```

```csharp
// ═══════════════════════════════════════════════════════════
// Ingestion/Extractors/ (one file per extractor)
// ═══════════════════════════════════════════════════════════

// TextExtractor.cs — .txt, .md, .csv, .json, .xml, .yaml, code files
// PdfExtractor.cs — .pdf (text + table + embedded images)
// OfficeExtractor.cs — .docx, .xlsx, .pptx
// EmailExtractor.cs — .eml, .msg
// WebExtractor.cs — .html, .htm (boilerplate removal)
// ArchiveExtractor.cs — .zip, .tar.gz (recurse into contents)
// AiFallbackExtractor.cs — *.* (render → vision, Priority = 0)
```

```csharp
// ═══════════════════════════════════════════════════════════
// Ingestion/Services/IngestionService.cs
// ═══════════════════════════════════════════════════════════

public interface IIngestionService
{
    /// Ingest a file into a space. Returns the created Note.
    Task<Note> IngestAsync(
        Stream content, string fileName, string spaceId,
        NoteOrigin origin = NoteOrigin.Upload,
        CancellationToken ct = default);

    /// Ingest a URL (fetch content, then process).
    Task<Note> IngestUrlAsync(
        string url, string spaceId,
        CancellationToken ct = default);

    /// Re-process a note with current extractors (after extractor upgrade).
    Task ReprocessAsync(string noteId, CancellationToken ct = default);
}

// Implementation:
// 1. Store raw file via MediaEntity.Upload() (always preserved)
// 2. Detect MIME type
// 3. Find highest-priority IContentExtractor that supports the MIME type
// 4. Extract → ContentBlocks
// 5. [MediaAnalysis] fires (vision, OCR, transcribe, etc.)
// 6. [Embedding] fires (embed the blocks)
// 7. Save Note entity + vector atomically
```

### Sources Context

Passive feeds that auto-ingest into spaces.

```csharp
// ═══════════════════════════════════════════════════════════
// Sources/Models/Source.cs
// ═══════════════════════════════════════════════════════════

public class Source : Entity<Source>
{
    public string Name { get; set; } = string.Empty;
    public SourceType Type { get; set; }
    public string SpaceId { get; set; } = string.Empty;
    public string Configuration { get; set; } = "{}";
    public SourceSchedule Schedule { get; set; } = SourceSchedule.Daily;
    public bool Enabled { get; set; } = true;
    public DateTime? LastPulledAt { get; set; }
    public int TotalItemsPulled { get; set; }
    public int FailureCount { get; set; }
}

public enum SourceType
{
    Rss,
    Podcast,
    YouTube,
    GitHub,
    HackerNews,
    Reddit,
    Email,
    FolderWatch,
    Bookmark,
    Zotero,
    Obsidian
}

public sealed record SourceSchedule
{
    public TimeSpan Interval { get; init; }
    public bool Immediate { get; init; }

    public static SourceSchedule Hourly => new() { Interval = TimeSpan.FromHours(1) };
    public static SourceSchedule Daily => new() { Interval = TimeSpan.FromDays(1) };
    public static SourceSchedule OnChange => new() { Immediate = true };
    public static SourceSchedule Every(TimeSpan interval) => new() { Interval = interval };
}
```

```csharp
// ═══════════════════════════════════════════════════════════
// Sources/Abstractions/ISourcePuller.cs
// ═══════════════════════════════════════════════════════════

public interface ISourcePuller
{
    SourceType Type { get; }
    Task<IReadOnlyList<SourceItem>> PullAsync(
        Source source, DateTime? since, CancellationToken ct = default);
}

public sealed record SourceItem(
    string Title,
    string? Content,
    string? Url,
    Stream? MediaStream,
    string? MediaContentType,
    DateTime? PublishedAt,
    Dictionary<string, string> Meta);

// Implementations:
// Sources/Pullers/RssPuller.cs
// Sources/Pullers/PodcastPuller.cs (downloads audio)
// Sources/Pullers/YouTubePuller.cs (fetches transcript or downloads audio)
// Sources/Pullers/GitHubPuller.cs (releases, READMEs, issues)
// Sources/Pullers/HackerNewsPuller.cs
// Sources/Pullers/FolderWatchPuller.cs (file system watcher)
// Sources/Pullers/ZoteroPuller.cs (reads Zotero SQLite DB)
// Sources/Pullers/ObsidianPuller.cs (reads markdown vault)
```

```csharp
// ═══════════════════════════════════════════════════════════
// Sources/Workers/SourcePullWorker.cs
// ═══════════════════════════════════════════════════════════

/// Background service that pulls sources on schedule.
/// Each pull: ISourcePuller → SourceItems → IIngestionService → Notes.
public sealed class SourcePullWorker : BackgroundService { }
```

### Research Context

Active topic monitoring with intelligent findings evaluation.

```csharp
// ═══════════════════════════════════════════════════════════
// Research/Models/ResearchBrief.cs
// ═══════════════════════════════════════════════════════════

public class ResearchBrief : Entity<ResearchBrief>
{
    public string Name { get; set; } = string.Empty;
    public string SpaceId { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public List<string> Keywords { get; set; } = [];
    public List<string> Exclusions { get; set; } = [];
    public SearchScope Scope { get; set; } = SearchScope.Web;
    public SourceSchedule Schedule { get; set; } = SourceSchedule.Daily;
    public IngestPolicy Policy { get; set; } = IngestPolicy.Adaptive;
    public double RelevanceThreshold { get; set; } = 0.7;
    public int MaxItemsPerRun { get; set; } = 20;
    public bool Enabled { get; set; } = true;
    public DateTime? LastRunAt { get; set; }
    public int TotalFound { get; set; }
    public int TotalIngested { get; set; }
    public int TotalDismissed { get; set; }
}

public sealed record SearchScope
{
    public bool Web { get; init; }
    public bool Arxiv { get; init; }
    public bool HackerNews { get; init; }
    public bool Reddit { get; init; }
    public bool YouTube { get; init; }
    public bool GitHub { get; init; }
    public bool News { get; init; }
    public List<string> CustomUrls { get; init; } = [];

    public static SearchScope All => new()
    {
        Web = true, Arxiv = true, HackerNews = true,
        Reddit = true, YouTube = true, GitHub = true, News = true
    };
    public static SearchScope Academic => new() { Arxiv = true, Web = true };
    public static SearchScope Tech => new() { HackerNews = true, GitHub = true, Web = true, YouTube = true };
}

public enum IngestPolicy
{
    AutoIngest,
    ReviewFirst,
    Digest,
    Adaptive
}
```

```csharp
// ═══════════════════════════════════════════════════════════
// Research/Models/ResearchFinding.cs
// ═══════════════════════════════════════════════════════════

public class ResearchFinding : Entity<ResearchFinding>, IReviewable
{
    public string BriefId { get; set; } = string.Empty;
    public string SpaceId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? Url { get; set; }
    public string FoundVia { get; set; } = string.Empty;
    public double RelevanceScore { get; set; }
    public string? WhyRelevant { get; set; }
    public DateTime? PublishedAt { get; set; }
    public FindingStatus Status { get; set; } = FindingStatus.Pending;
    public string? NoteId { get; set; }

    // IReviewable
    public ReviewStatus ReviewStatus { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? RejectionReason { get; set; }
}

public enum FindingStatus
{
    Pending,
    AutoIngested,
    Approved,
    Dismissed,
    Digested
}
```

### ModelIndex Context

Proactive model card crawling with semantic search.

```csharp
// ═══════════════════════════════════════════════════════════
// ModelIndex/Models/ModelCard.cs
// ═══════════════════════════════════════════════════════════

[Embedding(Policy = EmbeddingPolicy.Explicit, Async = true, Version = 1)]
public class ModelCard : Entity<ModelCard>
{
    public string HubId { get; set; } = string.Empty;
    public string Registry { get; set; } = "huggingface";
    public string? Author { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
    public string? Task { get; set; }
    public List<string> Domains { get; set; } = [];
    public long ParameterCount { get; set; }
    public long FileSizeBytes { get; set; }
    public ModelFormat[] AvailableFormats { get; set; } = [];
    public string? License { get; set; }
    public int Downloads { get; set; }
    public int Likes { get; set; }
    public DateTime LastModified { get; set; }
    public long EstimatedVramBytes { get; set; }
    public float[]? Embedding { get; set; }
    public DateTime CrawledAt { get; set; }

    public string ToEmbeddingText() =>
        $"{Title}\n{Description}\n{string.Join(", ", Tags)}\n{string.Join(", ", Domains)}";
}
```

```csharp
// ═══════════════════════════════════════════════════════════
// ModelIndex/Models/ModelDiscovery.cs
// ═══════════════════════════════════════════════════════════

/// Proactive discovery: a model was found that matches user's interests.
public class ModelDiscovery : Entity<ModelDiscovery>
{
    public string ModelCardId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public double RelevanceScore { get; set; }
    public List<string> MatchedInterests { get; set; } = [];
    public DiscoveryStatus Status { get; set; }
    public DateTime DiscoveredAt { get; set; }
}

public enum DiscoveryStatus
{
    Suggested,
    Accepted,
    Dismissed,
    Evaluated,
    Deployed
}
```

```csharp
// ═══════════════════════════════════════════════════════════
// ModelIndex/Abstractions/IModelRegistryCrawler.cs
// ═══════════════════════════════════════════════════════════

public interface IModelRegistryCrawler
{
    string RegistryName { get; }
    Task<IReadOnlyList<ModelCard>> CrawlAsync(
        ModelCrawlScope scope, CancellationToken ct = default);
}

// Implementations:
// ModelIndex/Crawlers/HuggingFaceCrawler.cs
// ModelIndex/Crawlers/OllamaLibraryCrawler.cs
// ModelIndex/Crawlers/OnnxZooCrawler.cs
```

### Interaction Context

Lenses, Pulse view, and user Q&A.

```csharp
// ═══════════════════════════════════════════════════════════
// Interaction/Models/Lens.cs
// ═══════════════════════════════════════════════════════════

public class Lens : Entity<Lens>
{
    public string Name { get; set; } = string.Empty;
    public string? SpaceId { get; set; }
    public string PromptName { get; set; } = string.Empty;
    public LensMode Mode { get; set; }
    public List<string> FocusTopics { get; set; } = [];
}

public enum LensMode
{
    Brief,      // "What's new? What connects?"
    Find,       // "I know I read about this somewhere."
    Write,      // "Help me build an argument."
    Research,   // "Go deeper. Follow the leads."
    Learn,      // "What do I know? What are the gaps?"
    Capture     // "Save this. Don't interrupt."
}
```

```csharp
// ═══════════════════════════════════════════════════════════
// Interaction/Services/PulseService.cs
// ═══════════════════════════════════════════════════════════

/// Generates the Pulse view: what's new, what matters, what connects.
public interface IPulseService
{
    Task<PulseView> GenerateAsync(
        string? spaceId, CancellationToken ct = default);
}

public sealed record PulseView
{
    public List<PulseSection> Sections { get; init; } = [];
    public DateTime GeneratedAt { get; init; }
}

public sealed record PulseSection
{
    public string SpaceName { get; init; } = string.Empty;
    public List<PulseItem> NewItems { get; init; } = [];
    public List<PulseItem> Connections { get; init; } = [];
    public List<PulseItem> ActionItems { get; init; } = [];
    public List<ModelDiscovery> ModelUpdates { get; init; } = [];
}

public sealed record PulseItem
{
    public string NoteId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string? ConnectionReason { get; init; }
    public double? RelevanceScore { get; init; }
}
```

```csharp
// ═══════════════════════════════════════════════════════════
// Interaction/Services/QueryService.cs
// ═══════════════════════════════════════════════════════════

/// Handles user questions. Routes through per-space chain + agent.
public interface IQueryService
{
    Task<QueryResult> AskAsync(
        string question, string? spaceId, LensMode mode,
        CancellationToken ct = default);

    IAsyncEnumerable<QueryChunk> StreamAsync(
        string question, string? spaceId, LensMode mode,
        CancellationToken ct = default);
}

public sealed record QueryResult
{
    public string Answer { get; init; } = string.Empty;
    public List<NoteCitation> Citations { get; init; } = [];
    public List<string> CrossSpaceConnections { get; init; } = [];
    public QueryMetrics Metrics { get; init; } = new();
}

public sealed record NoteCitation
{
    public string NoteId { get; init; } = string.Empty;
    public string NoteTitle { get; init; } = string.Empty;
    public string SpaceName { get; init; } = string.Empty;
    public string Excerpt { get; init; } = string.Empty;
    public double Relevance { get; init; }
}
```

### Learning Context

The closed-loop: corrections → training → better models.

```csharp
// ═══════════════════════════════════════════════════════════
// Learning/Services/SpaceTrainingService.cs
// ═══════════════════════════════════════════════════════════

/// Per-space fine-tuning from user corrections and ratings.
public interface ISpaceTrainingService
{
    /// Check if a space has enough corrections for retraining.
    Task<TrainingReadiness> CheckReadinessAsync(
        string spaceId, CancellationToken ct = default);

    /// Train a new LoRA for a space's embedding model.
    Task<JobRef> TrainEmbeddingLoraAsync(
        string spaceId, CancellationToken ct = default);

    /// Train a new LoRA for a space's chat model.
    Task<JobRef> TrainChatLoraAsync(
        string spaceId, CancellationToken ct = default);

    /// Get improvement metrics for a space.
    Task<ImprovementMetrics> GetMetricsAsync(
        string spaceId, CancellationToken ct = default);
}

public sealed record TrainingReadiness(
    bool Ready,
    int CorrectionsAvailable,
    int CorrectionsNeeded,
    string? Recommendation);

public sealed record ImprovementMetrics(
    int TotalCorrections,
    int TrainingRuns,
    double SearchRelevanceBefore,
    double SearchRelevanceAfter,
    double ImprovementPercent,
    DateTime? LastTrainedAt);
```

### Setup Context

The onboarding agent that configures Prism from natural language.

```csharp
// ═══════════════════════════════════════════════════════════
// Setup/Services/SetupAgentService.cs
// ═══════════════════════════════════════════════════════════

/// The onboarding agent. Configures spaces and models from user intent.
public interface ISetupAgentService
{
    IAsyncEnumerable<SetupStep> ConfigureAsync(
        string userDescription, CancellationToken ct = default);
}

public sealed record SetupStep
{
    public SetupPhase Phase { get; init; }
    public string Message { get; init; } = string.Empty;
    public List<ModelRecommendation>? Recommendations { get; init; }
    public SetupAction? RequiresAction { get; init; }
}

public enum SetupPhase
{
    Understanding,      // Parsing user intent
    SearchingModels,    // Querying local ModelCard index
    Recommending,       // Presenting model options
    WaitingForApproval, // User confirms
    Pulling,            // Downloading models
    Configuring,        // Setting up spaces + model routing
    Complete
}

// Implementation uses Agent.Create() with:
// - WithEntities<ModelCard, Space>()
// - WithSearch<ModelCard>()
// - Tool.From<ComputeService>("GetLocalGpu")
// - Tool.From<ModelService>("Pull", "Convert", "Quantize", "Deploy")
```

---

## Controllers

Minimal. `EntityController<T>` provides CRUD. Custom endpoints only for business logic.

```csharp
// ═══════════════════════════════════════════════════════════
// Controllers/EntityControllers.cs
// ═══════════════════════════════════════════════════════════

[Route("api/spaces")]
public class SpaceController : EntityController<Space> { }

[Route("api/notes")]
[Pagination(Mode = PaginationMode.On, DefaultSize = 30, MaxSize = 200)]
public class NoteController : EntityController<Note> { }

[Route("api/sources")]
public class SourceController : EntityController<Source> { }

[Route("api/briefs")]
public class ResearchBriefController : EntityController<ResearchBrief> { }

[Route("api/model-cards")]
[Pagination(Mode = PaginationMode.On, DefaultSize = 50)]
public class ModelCardController : EntityController<ModelCard> { }

[Route("api/lenses")]
public class LensController : EntityController<Lens> { }
```

```csharp
// ═══════════════════════════════════════════════════════════
// Controllers/PrismController.cs
// ═══════════════════════════════════════════════════════════

/// Custom endpoints for Prism-specific operations.
[ApiController]
[Route("api/prism")]
public class PrismController : ControllerBase
{
    // ── Ingestion ──
    [HttpPost("ingest")]
    public async Task<ActionResult<Note>> Ingest(
        IFormFile file, [FromQuery] string spaceId) { }

    [HttpPost("ingest/url")]
    public async Task<ActionResult<Note>> IngestUrl(
        [FromBody] IngestUrlRequest request) { }

    // ── Query ──
    [HttpPost("ask")]
    public async Task<ActionResult<QueryResult>> Ask(
        [FromBody] AskRequest request) { }

    [HttpPost("ask/stream")]
    public async IAsyncEnumerable<QueryChunk> AskStream(
        [FromBody] AskRequest request) { }

    // ── Pulse ──
    [HttpGet("pulse")]
    public async Task<ActionResult<PulseView>> GetPulse(
        [FromQuery] string? spaceId) { }

    // ── Search (cross-space) ──
    [HttpPost("search")]
    public async Task<ActionResult<SearchResult>> Search(
        [FromBody] SearchRequest request) { }

    // ── Setup ──
    [HttpPost("setup")]
    public async IAsyncEnumerable<SetupStep> Setup(
        [FromBody] SetupRequest request) { }

    // ── Learning metrics ──
    [HttpGet("spaces/{spaceId}/improvement")]
    public async Task<ActionResult<ImprovementMetrics>> GetImprovement(
        string spaceId) { }
}
```

---

## Review Queues

```csharp
// ═══════════════════════════════════════════════════════════
// Initialization/ReviewQueueConfiguration.cs
// ═══════════════════════════════════════════════════════════

public static class ReviewQueueConfiguration
{
    public static void ConfigureReviewQueues(this IServiceCollection services)
    {
        services.AddKoanReview(review =>
        {
            // Research findings review queue
            review.Queue<ResearchFinding>("research-findings", q => q
                .Where(f => f.Status == FindingStatus.Pending)
                .Display(f => new
                {
                    f.Title,
                    f.Summary,
                    f.FoundVia,
                    f.RelevanceScore,
                    f.WhyRelevant,
                    f.Url,
                    f.PublishedAt
                })
                .Approve()
                .Reject()
                .Label(f => f.RelevanceScore, [0.0, 0.25, 0.5, 0.75, 1.0])
                .Flag("must-read", "share"));

            // Note correction queue (for improving summaries/classifications)
            review.Queue<Note>("note-corrections", q => q
                .Where(n => n.ReviewStatus == ReviewStatus.Pending
                          && n.Origin != NoteOrigin.Upload)
                .Display(n => new
                {
                    n.Title,
                    n.Summary,
                    n.Category,
                    n.KeyConcepts,
                    n.Tags
                })
                .Approve()
                .Edit(n => n.Summary)
                .Edit(n => n.Category)
                .Edit(n => n.Tags));

            // Model discovery review
            review.Queue<ModelDiscovery>("model-discoveries", q => q
                .Where(d => d.Status == DiscoveryStatus.Suggested)
                .Display(d => new
                {
                    d.Reason,
                    d.RelevanceScore,
                    d.MatchedInterests
                })
                .Approve()
                .Reject());
        });
    }
}
```

---

## Chains

```csharp
// ═══════════════════════════════════════════════════════════
// Interaction/Chains/PrismChains.cs
// ═══════════════════════════════════════════════════════════

public static class PrismChains
{
    /// RAG chain for the "Find" lens: retrieve → rerank → answer with citations.
    public static ChainBuilder Find() => Chain.Create()
        .Retrieve<Note>(query: "{question}", topK: 10, rerank: true)
        .Compress()
        .WithPrompt("prism-find")
        .Chat("{question}\n\nRelevant notes:\n{context}");

    /// Chain for the "Brief" lens: summarize new items, identify connections.
    public static ChainBuilder Brief() => Chain.Create()
        .WithPrompt("prism-brief")
        .Chat("Summarize these new items and identify connections to existing knowledge:\n\n{items}");

    /// Chain for the "Write" lens: find evidence for/against claims.
    public static ChainBuilder Write() => Chain.Create()
        .Parallel(
            ("supporting", Chain.Create()
                .Retrieve<Note>(query: "evidence supporting: {claim}", topK: 5)
                .Chat("Extract supporting evidence:\n{context}")),
            ("contradicting", Chain.Create()
                .Retrieve<Note>(query: "evidence against: {claim}", topK: 5)
                .Chat("Extract contradicting evidence:\n{context}")))
        .WithPrompt("prism-write")
        .Chat("Claim: {claim}\n\nSupporting:\n{supporting}\n\nContradicting:\n{contradicting}");

    /// Chain for the "Research" lens: deep investigation with multi-query.
    public static ChainBuilder Research() => Chain.Create()
        .Retrieve<Note>(query: "{question}", topK: 15, rerank: true)
        .Compress()
        .WithPrompt("prism-research")
        .Chat("{question}\n\nResearch context:\n{context}");

    /// Chain for cross-space search: searches all spaces with their own embeddings.
    public static ChainBuilder CrossSpaceSearch() => Chain.Create()
        .WithPrompt("prism-cross-space")
        .Chat("Search across all spaces for: {question}\n\nResults:\n{results}");

    /// Chain for Research Brief relevance scoring.
    public static ChainBuilder ScoreRelevance() => Chain.Create()
        .System("Score the relevance of this finding to the user's space content. " +
                "Return a score 0.0-1.0 and a brief explanation.")
        .Chat("Brief topic: {topic}\nFinding: {finding}\nExisting space concepts: {concepts}")
        .Parse<RelevanceAssessment>();

    /// Chain for Pulse digest generation.
    public static ChainBuilder DigestSynthesis() => Chain.Create()
        .WithPrompt("prism-digest")
        .Chat("Synthesize these {count} items into a digest. " +
              "Identify themes, connections, and action items:\n\n{items}");
}

public sealed record RelevanceAssessment(double Score, string Reason);
```

---

## Program.cs

```csharp
// ═══════════════════════════════════════════════════════════
// Program.cs
// ═══════════════════════════════════════════════════════════

var builder = WebApplication.CreateBuilder(args);

// ── Koan Framework ──
builder.Services.AddKoan()
    .AsWebApi();

builder.Services.AddKoanZenGarden(builder.Configuration);

// ── Prism Options ──
builder.Services.AddKoanOptions<PrismOptions>(
    builder.Configuration, "Prism");

// ── Ingestion (content extractors auto-discovered via Reference = Intent) ──
builder.Services.AddScoped<IIngestionService, IngestionService>();

// ── Sources ──
builder.Services.AddHostedService<SourcePullWorker>();

// ── Research ──
builder.Services.AddHostedService<ResearchBriefWorker>();
builder.Services.AddHostedService<ModelIndexCrawlWorker>();

// ── Interaction ──
builder.Services.AddScoped<IQueryService, QueryService>();
builder.Services.AddScoped<IPulseService, PulseService>();
builder.Services.AddScoped<ISpaceScopeService, SpaceScopeService>();

// ── Learning ──
builder.Services.AddScoped<ISpaceTrainingService, SpaceTrainingService>();

// ── Setup ──
builder.Services.AddScoped<ISetupAgentService, SetupAgentService>();

// ── Review Queues ──
builder.Services.ConfigureReviewQueues();

var app = builder.Build();
AppHost.Current ??= app.Services;

app.MapControllers();
app.MapStaticAssets();
app.Run();
```

---

## Project References

```xml
<!-- ═══════════════════════════════════════════════════════════ -->
<!-- S18.Prism.csproj -->
<!-- ═══════════════════════════════════════════════════════════ -->

<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <!-- Core -->
    <ProjectReference Include="..\..\src\Koan.Core\Koan.Core.csproj" />
    <ProjectReference Include="..\..\src\Koan.Data.Core\Koan.Data.Core.csproj" />
    <ProjectReference Include="..\..\src\Koan.Web\Koan.Web.csproj" />
    <ProjectReference Include="..\..\src\Koan.Web.Extensions\Koan.Web.Extensions.csproj" />

    <!-- Data Provider -->
    <ProjectReference Include="..\..\src\Connectors\Data\Mongo\Koan.Data.Connector.Mongo.csproj" />

    <!-- Storage -->
    <ProjectReference Include="..\..\src\Koan.Storage\Koan.Storage.csproj" />
    <ProjectReference Include="..\..\src\Connectors\Storage\Local\Koan.Storage.Connector.Local.csproj" />
    <ProjectReference Include="..\..\src\Connectors\Storage\S3\Koan.Storage.Connector.S3.csproj" />

    <!-- Media -->
    <ProjectReference Include="..\..\src\Koan.Media.Core\Koan.Media.Core.csproj" />
    <ProjectReference Include="..\..\src\Koan.Media.Abstractions\Koan.Media.Abstractions.csproj" />

    <!-- AI — Core -->
    <ProjectReference Include="..\..\src\Koan.AI\Koan.AI.csproj" />
    <ProjectReference Include="..\..\src\Koan.AI.Contracts\Koan.AI.Contracts.csproj" />
    <ProjectReference Include="..\..\src\Koan.Data.AI\Koan.Data.AI.csproj" />

    <!-- AI — New Capabilities (AI-0022 through AI-0031) -->
    <ProjectReference Include="..\..\src\Koan.AI.Models\Koan.AI.Models.csproj" />
    <ProjectReference Include="..\..\src\Koan.AI.Models.HuggingFace\Koan.AI.Models.HuggingFace.csproj" />
    <ProjectReference Include="..\..\src\Koan.AI.Models.Onnx\Koan.AI.Models.Onnx.csproj" />
    <ProjectReference Include="..\..\src\Koan.AI.Prompt\Koan.AI.Prompt.csproj" />
    <ProjectReference Include="..\..\src\Koan.AI.Compute\Koan.AI.Compute.csproj" />
    <ProjectReference Include="..\..\src\Koan.AI.Orchestration\Koan.AI.Orchestration.csproj" />
    <ProjectReference Include="..\..\src\Koan.AI.Training\Koan.AI.Training.csproj" />
    <ProjectReference Include="..\..\src\Koan.AI.Eval\Koan.AI.Eval.csproj" />
    <ProjectReference Include="..\..\src\Koan.AI.Review\Koan.AI.Review.csproj" />
    <ProjectReference Include="..\..\src\Koan.AI.Agents\Koan.AI.Agents.csproj" />

    <!-- AI — Connectors -->
    <ProjectReference Include="..\..\src\Connectors\AI\Ollama\Koan.AI.Connector.Ollama.csproj" />

    <!-- Vector -->
    <ProjectReference Include="..\..\src\Koan.Data.Vector\Koan.Data.Vector.csproj" />
    <ProjectReference Include="..\..\src\Koan.Data.Vector.Abstractions\Koan.Data.Vector.Abstractions.csproj" />
    <ProjectReference Include="..\..\src\Connectors\Data\Vector\Weaviate\Koan.Data.Vector.Connector.Weaviate.csproj" />

    <!-- ZenGarden -->
    <ProjectReference Include="..\..\src\Koan.ZenGarden\Koan.ZenGarden.csproj" />

    <!-- Content Extractors (each adds format support via Reference = Intent) -->
    <ProjectReference Include="..\..\src\Koan.Content.Pdf\Koan.Content.Pdf.csproj" />
    <ProjectReference Include="..\..\src\Koan.Content.Office\Koan.Content.Office.csproj" />
    <ProjectReference Include="..\..\src\Koan.Content.Web\Koan.Content.Web.csproj" />
  </ItemGroup>
</Project>
```

---

## Directory Structure

```
S18.Prism/
├── SPECIFICATION.md                    ← This file
├── Program.cs
├── S18.Prism.csproj
├── appsettings.json
├── appsettings.Development.json
│
├── Knowledge/                          ← Core domain
│   └── Models/
│       ├── Note.cs
│       └── ContentBlock.cs
│
├── Spaces/                             ← Space management
│   ├── Models/
│   │   └── Space.cs
│   └── Services/
│       └── SpaceScopeService.cs
│
├── Ingestion/                          ← Universal loader
│   ├── Abstractions/
│   │   └── IContentExtractor.cs
│   ├── Extractors/
│   │   ├── TextExtractor.cs
│   │   ├── PdfExtractor.cs
│   │   ├── OfficeExtractor.cs
│   │   ├── WebExtractor.cs
│   │   ├── EmailExtractor.cs
│   │   ├── ArchiveExtractor.cs
│   │   └── AiFallbackExtractor.cs
│   └── Services/
│       └── IngestionService.cs
│
├── Sources/                            ← Passive feeds
│   ├── Models/
│   │   └── Source.cs
│   ├── Abstractions/
│   │   └── ISourcePuller.cs
│   ├── Pullers/
│   │   ├── RssPuller.cs
│   │   ├── PodcastPuller.cs
│   │   ├── YouTubePuller.cs
│   │   ├── GitHubPuller.cs
│   │   ├── HackerNewsPuller.cs
│   │   ├── FolderWatchPuller.cs
│   │   ├── ZoteroPuller.cs
│   │   └── ObsidianPuller.cs
│   └── Workers/
│       └── SourcePullWorker.cs
│
├── Research/                           ← Active research briefs
│   ├── Models/
│   │   ├── ResearchBrief.cs
│   │   └── ResearchFinding.cs
│   ├── Services/
│   │   ├── ResearchBriefExecutor.cs
│   │   └── RelevanceScoringService.cs
│   └── Workers/
│       └── ResearchBriefWorker.cs
│
├── ModelIndex/                         ← Proactive model catalog
│   ├── Models/
│   │   ├── ModelCard.cs
│   │   └── ModelDiscovery.cs
│   ├── Abstractions/
│   │   └── IModelRegistryCrawler.cs
│   ├── Crawlers/
│   │   ├── HuggingFaceCrawler.cs
│   │   ├── OllamaLibraryCrawler.cs
│   │   └── OnnxZooCrawler.cs
│   └── Workers/
│       └── ModelIndexCrawlWorker.cs
│
├── Interaction/                        ← Lenses, Pulse, Q&A
│   ├── Models/
│   │   └── Lens.cs
│   ├── Chains/
│   │   └── PrismChains.cs
│   └── Services/
│       ├── QueryService.cs
│       ├── PulseService.cs
│       └── CrossSpaceSearchService.cs
│
├── Learning/                           ← Closed-loop training
│   └── Services/
│       └── SpaceTrainingService.cs
│
├── Setup/                              ← Onboarding agent
│   └── Services/
│       └── SetupAgentService.cs
│
├── Controllers/
│   ├── EntityControllers.cs            ← Auto-wired CRUD
│   └── PrismController.cs             ← Custom endpoints
│
├── Initialization/
│   ├── ReviewQueueConfiguration.cs
│   ├── DefaultPromptSeeder.cs
│   └── DefaultLensSeeder.cs
│
├── Configuration/
│   └── PrismOptions.cs
│
├── wwwroot/                            ← SPA frontend
│   ├── index.html
│   ├── js/
│   └── css/
│
├── docker/
│   └── docker-compose.yml
│
├── .Koan/
│   ├── Data/
│   │   ├── mongo/
│   │   ├── weaviate/
│   │   └── ollama-models/
│   ├── cache/
│   ├── storage/
│   │   └── knowledge/                 ← Raw uploaded files
│   ├── models/                         ← Downloaded AI models
│   └── jobs/                           ← Training job artifacts
│
└── start.bat
```

---

## Configuration

```json
// appsettings.json
{
  "Koan": {
    "Data": {
      "Mongo": { "Database": "Prism" }
    },
    "Storage": {
      "DefaultProfile": "knowledge",
      "Profiles": {
        "knowledge": {
          "Container": "knowledge",
          "LocalCache": {
            "MaxSize": "2GB",
            "Policy": "lru"
          }
        }
      }
    },
    "Ai": {
      "AutoDiscoveryEnabled": true,
      "Chat": { "Source": "ollama-local" },
      "Embed": { "Source": "onnx-local" }
    }
  },
  "Prism": {
    "ModelIndex": {
      "CrawlSchedule": "daily",
      "SeedRegistries": ["huggingface", "ollama"],
      "MaxIndexSize": 10000
    },
    "Sources": {
      "DefaultPullSchedule": "hourly"
    },
    "Research": {
      "DefaultPolicy": "adaptive",
      "MaxFindingsPerRun": 20
    },
    "Learning": {
      "MinCorrectionsForRetrain": 50,
      "AutoRetrainEnabled": true
    },
    "Ingestion": {
      "MaxFileSizeMb": 100,
      "AiFallbackEnabled": true
    }
  }
}
```

---

## Domain Events

Cross-context communication via Koan event bus. No direct context-to-context imports.

```csharp
// ═══════════════════════════════════════════════════════════
// Events (published by each context, consumed by others)
// ═══════════════════════════════════════════════════════════

// Knowledge context publishes:
public sealed record NoteCreated(string NoteId, string SpaceId, NoteOrigin Origin);
public sealed record NoteUpdated(string NoteId, string SpaceId);

// Sources context publishes:
public sealed record SourceItemPulled(string SourceId, string SpaceId, string ItemUrl);

// Research context publishes:
public sealed record FindingApproved(string FindingId, string BriefId, string SpaceId);
public sealed record FindingDismissed(string FindingId, string BriefId, string SpaceId);

// ModelIndex context publishes:
public sealed record ModelDiscovered(string ModelCardId, string Reason, double Relevance);

// Learning context publishes:
public sealed record SpaceModelRetrained(string SpaceId, string ModelType, ModelRef NewModel);

// Review context publishes:
public sealed record NoteReviewed(string NoteId, ReviewStatus Status);
public sealed record FindingReviewed(string FindingId, ReviewStatus Status);

// Setup context publishes:
public sealed record SpaceConfigured(string SpaceId, SpaceModels Models);
```

---

## Implementation Phasing

| Phase | What | Exercises | Milestone |
|-------|------|-----------|-----------|
| **P1** | Knowledge + Ingestion + basic search | Entity<T>, MediaEntity, [MediaAnalysis], [Embedding], Vector<T>.Search, IContentExtractor | "Drop in a file, search for it semantically" |
| **P2** | Spaces + per-space model routing | Space entity, Client.Scope(), SpaceModels | "Different models per domain" |
| **P3** | Sources + passive feeds | ISourcePuller, RSS/GitHub/Folder Watch, background worker | "Subscribe to RSS, content auto-ingests" |
| **P4** | Interaction (Lenses + Pulse + Q&A) | Chain.*, Prompt.Load(), streaming | "Ask a question, get an answer with citations" |
| **P5** | ModelIndex + Setup Agent | ModelCard entity, HF crawler, Agent.*, Model.Search/Pull/Deploy | "Describe your interests, Prism finds the models" |
| **P6** | Research Briefs | ResearchBrief, findings, Review.*, Adaptive policy | "Watch the internet for AI regulation news" |
| **P7** | Learning (closed loop) | Dataset.From<Note>(), Training.Train(), Eval.Compare(), per-space LoRA | "Your corrections make Prism smarter" |
| **P8** | Polish (cross-space search, import wizards, improvement dashboard) | Eval.Drift(), Training.Compare(), bulk import | "Full experience" |

---

## Dogfood Validation Checklist

Each Koan capability must be validated by Prism's real usage, not contrived:

- [ ] `Model.Pull()` — user accepts model recommendation during setup
- [ ] `Model.Deploy()` — model placed on appropriate runtime after pull
- [ ] `Model.Routes()` — setup agent checks VRAM fit before recommending
- [ ] `Model.History()` — user sees which model version was active when a note was analyzed
- [ ] `Prompt()` — per-lens prompts loaded from catalog, user can edit
- [ ] `Prompt.Load()` with A/B test — "brief" prompt variant comparison
- [ ] `Compute.Available()` — setup agent checks local GPU before recommending models
- [ ] `Chain.Create()` — RAG chains per lens (Find, Brief, Write, Research)
- [ ] `Chain.Retrieve<Note>()` — semantic retrieval with reranking
- [ ] `Chain.Branch()` — route by content type or lens mode
- [ ] `[MediaAnalysis]` — photo → vision, PDF → OCR, voice memo → transcribe
- [ ] `[Embedding]` — all notes embedded, searchable via Vector<Note>
- [ ] `Dataset.From<Note>()` — user-rated notes become training data
- [ ] `Training.Train()` — per-space LoRA from corrections
- [ ] `Training.Compare()` — compare LoRA variants per space
- [ ] `Eval.Drift()` — detect topic shifts in incoming research
- [ ] `Eval.Compare()` — compare current model vs discovered upgrade
- [ ] `Eval.Gate()` — new LoRA must not regress before replacing current
- [ ] `Review.Create<ResearchFinding>()` — approve/dismiss research findings
- [ ] `Review.Create<Note>()` — correct AI summaries/categories
- [ ] `Agent.Create().WithEntities<>().WithSearch<>()` — setup agent, Q&A agent
- [ ] `Client.Scope()` — per-space model routing transparent to user
- [ ] `MediaEntity<Note>` — raw files preserved in storage
- [ ] `ReplicatedStorageProvider` — local cache with optional durable tier
- [ ] `Vector<ModelCard>.Search()` — instant model discovery from local index
- [ ] `IContentExtractor` pipeline — any file format → ContentBlocks
- [ ] Cross-space search — same query, different embeddings per space
