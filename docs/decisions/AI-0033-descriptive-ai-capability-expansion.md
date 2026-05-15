---
id: AI-0033
slug: AI-0033-descriptive-ai-capability-expansion
domain: AI
status: Proposed
date: 2026-03-27
---

# ADR: Descriptive AI Capability Expansion — Verbs, Modalities, and Provider Taxonomy

**Contract**

- **Inputs:** New category definitions extending AI-0021's two-tier architecture (Protocol and Task); `Modality` enum replacing fragile MIME type strings for non-text content; options bags per verb following the `ChatOptions`/`EmbedOptions` pattern; adapter interfaces per new Protocol category; provider rankings prioritizing self-hosted (Ollama, whisper.cpp, ComfyUI, Coqui XTTS) over cloud (OpenAI, ElevenLabs, Stability AI).
- **Outputs:** Twelve new verbs on the `Client` static facade (`Imagine`, `Transcribe`, `Speak`, `Describe`, `Classify`, `Extract<T>`, `Rerank`, `Translate`, `Moderate`, `Edit`, `Render`, plus `Embed(bytes, Modality)` overload); corresponding `*Options` records, `*Result` records, `*Batch` overloads; `Modality` enum for content-type routing; expanded `Client.Scope()` with new category parameters; expanded `MediaAnalysis` flags enum.
- **Error Modes:** New Protocol verb with no registered adapter: throws with remediation message ("No adapter registered for Imagine. Install Koan.AI.Adapter.ComfyUI or configure a cloud source."). `Embed(bytes, Modality.Audio)` with adapter that only supports `Modality.Text`: router returns "No embed adapter supports Audio modality" with installed adapter capabilities listed. Task verb delegating via Chat but Chat model lacks required capability (e.g., `Describe` on a non-vision model): adapter `CanServe` returns false, router selects next adapter or fails with model capability diagnostic. `Classify` with no labels/schema/prompt: throws `ArgumentException` ("Classify requires a label set — provide Labels, a Prompt name, or use the generic Classify<TEnum> overload"). `Render` called with no video-capable adapter: throws with experimental capability warning.
- **Acceptance Criteria:** `Client.Imagine("A zen garden")` works with zero configuration against auto-discovered ComfyUI or configured cloud source. `Client.Embed(imageBytes, Modality.Image)` routes to a multimodal embedding adapter without MIME type strings. `Client.Transcribe(audioBytes)` works against local whisper.cpp. `Client.Speak("Hello")` returns audio bytes from local Coqui/Piper or cloud ElevenLabs. Every verb supports the tier escalation: simple → options → (stream) → entity → rich result. `Client.Scope(imagine: "comfyui-local", speak: "coqui")` routes independently. All options bags include `Source`, `Model`, and `VendorOptions` properties.

**Edge Cases**

- `Client.Embed(bytes, Modality.Image)` when only a text embedding adapter is registered: Fails with clear message listing installed adapter modalities. Does NOT silently fall back to text embedding of base64-encoded bytes.
- `Client.Describe(videoBytes, Modality.Video)` when Chat model supports images but not video: Adapter extracts keyframes (implementation-specific), sends as image sequence. If adapter cannot extract frames, fails with "Model does not support video input; consider extracting keyframes first."
- `Client.Classify<TEnum>(content)` where `TEnum` has `[Flags]` attribute: Switches to multi-label mode automatically. Return type is `TEnum` with multiple flags set. `ClassifyResult<TEnum>.AllScores` contains per-flag scores.
- `Client.Classify(content, new ClassifyOptions { Labels = [...], Prompt = "custom" })` — both labels and prompt provided: Prompt takes precedence; labels are ignored. Logged as dev-mode guidance ("Both Labels and Prompt provided; Prompt 'custom' takes precedence").
- `Client.Imagine("...")` returns different byte formats depending on adapter: `ImagineResult.Format` always populated. `ImagineOptions.Format` requests a specific format; adapter converts if supported, otherwise returns native format with `ImagineResult.Format` reflecting actual.
- `Client.Speak(entity)` where entity has no string properties: Falls back to `entity.ToString()`. If result is empty or type name, throws with "No speakable content found on {Type}".
- `Client.TranscribeStream(liveAudioStream)` when adapter does not support streaming: Buffers chunks and processes in segments (e.g., 30s windows). `TranscribeOptions.ChunkDuration` controls segment size.
- `Client.Edit(imageBytes, "Remove background")` when no edit-capable adapter exists but Imagine adapter exists: Does NOT silently fall back to re-generation. Edit and Imagine are distinct Protocol categories with different semantics (preserving vs. creating).
- `Client.Render("A sunset")` — video generation takes minutes: `RenderOptions.Timeout` defaults to 5 minutes. `OnProgress` callback fires if adapter supports progress reporting. Cancellation via `CancellationToken` sends abort to adapter.
- `Client.Moderate(content, new ModerateOptions { Policy = "nonexistent" })`: If policy references a `PromptEntry` that doesn't exist, throws `PromptNotFoundException` (fail-fast). If policy is a built-in name (e.g., OpenAI's default categories), routes directly.

## Context

AI-0021 established the category-driven architecture with three categories: Chat (Protocol), Embed (Protocol), and Ocr (Task → Chat). AI-0027 added `[MediaAnalysis]` with five declared flags (Describe, Ocr, Transcribe, Classify, Extract) — but only Describe and Ocr have full `Client` verb parity. The remaining flags exist as enum values and attribute infrastructure without corresponding `Client.*` verbs, adapter interfaces, or provider routing.

Meanwhile, the AI capability landscape has expanded dramatically:

1. **Image generation** is mature: ComfyUI (local), Stability AI, OpenAI GPT-image-1.5, Google Gemini 3 Pro Image.
2. **Speech-to-text** is commoditized: whisper.cpp runs locally at production quality; Deepgram Nova-3 leads cloud.
3. **Text-to-speech** is self-hostable: Coqui XTTS (MIT, 1100+ languages), Piper (edge-optimized), Bark.
4. **Reranking** improves RAG quality 20-40%: Cohere Rerank 4.0 (cloud), cross-encoders (local).
5. **Image editing** is distinct from generation: inpainting, background removal, style transfer.
6. **Multimodal embeddings** enable cross-modal search: SigLIP 2, Cohere Embed 4, Google Gemini Embedding 2.
7. **Video generation** is emerging but immature: Runway Gen-4, Veo 3, ComfyUI AnimateDiff.

The framework's `AiCapability` constants already reference many of these (`Vision`, `Transcribe`, `Synthesis`) but they exist only as string constants — not as routable categories with adapter interfaces and `Client` verbs.

### Persona-Driven Validation

Eight user personas were evaluated to stress-test the verb vocabulary:

| Persona | Primary Verbs | Key Insight |
|---------|--------------|-------------|
| Indie Game Developer | Chat, Imagine, Speak, Describe | Unified surface eliminates 5-SDK integration tax |
| Game/Digital Asset Manager | Embed, Classify, Describe, Moderate | Entity-first `[MediaAnalysis]` is the killer feature; needs multi-label and similarity search |
| Enterprise Document Processor | Ocr, Extract\<T\>, Classify, Transcribe, Translate | Needs per-field confidence and audit trails on all Results |
| Accessibility Engineer | Describe, Transcribe, Speak, Translate | `Describe` needs a `Purpose` parameter (alt text ≠ caption ≠ product listing) |
| E-Commerce Developer | Describe, Imagine, Embed, Classify, Translate, Moderate, Rerank | Pushes hard on batch operations and image editing |
| Creative Agency | Imagine, Speak, Render, Edit, Describe | Needs brand consistency across verbs; image editing is distinct from generation |
| IoT/Edge Developer | Transcribe, Speak, Classify, Describe, Ocr | Needs model tier awareness, offline guarantees, latency budgets |
| Data Scientist / ML Engineer | Embed, Rerank, Classify, Extract\<T\>, Chat | Needs batch everything, dimension control, raw adapter access |

Three cross-persona signals emerged:
- **`Edit` must be a distinct Protocol category** — 4/8 personas need image editing (not generation)
- **Batch operations are universal** — every collection-oriented persona hits single-item walls
- **Purpose/intent shapes output** — same verb, same input, different output based on use case

## Decision

### Part 1: Modality Enum

Replace fragile MIME type strings with a first-class enum for content routing:

```csharp
namespace Koan.AI.Contracts;

/// <summary>
/// Content modality for AI operations that accept non-text input.
/// Used by Embed, Describe, Classify, Moderate, and other verbs
/// to route to modality-capable adapters.
/// </summary>
public enum Modality
{
    /// <summary>Text content. Default for string overloads.</summary>
    Text,

    /// <summary>Image content (JPEG, PNG, WebP, TIFF, SVG).</summary>
    Image,

    /// <summary>Audio content (WAV, MP3, FLAC, OGG, M4A).</summary>
    Audio,

    /// <summary>Video content (MP4, WebM, MOV).</summary>
    Video,

    /// <summary>Document content (PDF, DOCX). Adapter extracts content as needed.</summary>
    Document
}
```

The middleware resolves specific formats from byte headers (magic bytes) or `VendorOptions` when adapters need MIME-level detail. Developers never pass MIME strings.

### Part 2: New Protocol Categories

Protocol categories have distinct I/O signatures requiring dedicated adapter interfaces.

**Taxonomy test** (AI-0021 §3) — each passes all four criteria:

| Category | Distinct Input | Dedicated Ecosystem | Different Default Model | Clearer DX |
|----------|---------------|-------------------|----------------------|------------|
| **Imagine** | text → image bytes | ComfyUI, Stability, DALL-E | flux-dev, sd3.5 | `Client.Imagine(prompt)` vs `Client.Chat("generate image", visionOpts)` |
| **Transcribe** | audio bytes → text | whisper.cpp, Deepgram, AssemblyAI | whisper-large-v3 | `Client.Transcribe(audio)` vs manual Whisper integration |
| **Speak** | text → audio bytes | Coqui, Piper, ElevenLabs | xtts-v2 | `Client.Speak(text)` vs manual TTS integration |
| **Edit** | image + instruction → image | ComfyUI inpaint, DALL-E edit | flux-inpaint | `Client.Edit(img, instruction)` vs `Client.Imagine` with img2img hack |
| **Rerank** | query + docs → scored docs | Cohere, cross-encoders | ms-marco-MiniLM | `Client.Rerank(q, docs)` vs manual cross-encoder loading |
| **Render** | text → video bytes | Runway, ComfyUI AnimateDiff | animatediff-v3 | `Client.Render(prompt)` vs manual video pipeline |

#### Adapter Interfaces

```csharp
public interface IImagineAdapter : IAiAdapter
{
    Task<ImagineResponse> Imagine(ImagineRequest request, CancellationToken ct);
}

public interface ITranscribeAdapter : IAiAdapter
{
    Task<TranscribeResponse> Transcribe(TranscribeRequest request, CancellationToken ct);
    IAsyncEnumerable<TranscribeSegment>? StreamTranscribe(Stream audioStream, TranscribeRequest request, CancellationToken ct);
}

public interface ISpeakAdapter : IAiAdapter
{
    Task<SpeakResponse> Speak(SpeakRequest request, CancellationToken ct);
    IAsyncEnumerable<ReadOnlyMemory<byte>>? StreamSpeak(SpeakRequest request, CancellationToken ct);
}

public interface IEditAdapter : IAiAdapter
{
    Task<EditResponse> Edit(EditRequest request, CancellationToken ct);
}

public interface IRerankAdapter : IAiAdapter
{
    Task<RerankResponse> Rerank(RerankRequest request, CancellationToken ct);
}

public interface IRenderAdapter : IAiAdapter
{
    Task<RenderResponse> Render(RenderRequest request, CancellationToken ct);
}
```

`IEmbedAdapter` gains modality awareness:

```csharp
public interface IEmbedAdapter : IAiAdapter
{
    IReadOnlySet<Modality> SupportedModalities { get; }
    Task<AiEmbeddingsResponse> Embed(AiEmbeddingsRequest request, CancellationToken ct);
}
```

### Part 3: Task Categories (Delegate via Protocol)

Task categories provide first-class DX but delegate to Protocol categories when no dedicated adapter exists.

| Task | Via | Dedicated Adapter | Template Required |
|------|-----|-------------------|------------------|
| **Describe** | Chat | `IDescribeAdapter` (optional) | No |
| **Classify** | Chat | `IClassifyAdapter` (optional, e.g. Cohere Classify) | **Yes** — labels, taxonomy, `[Flags]` enum, or named Prompt |
| **Extract\<T\>** | Chat | — | **Yes** — `T` is the schema |
| **Moderate** | Chat | `IModerateAdapter` (optional, e.g. OpenAI omni-moderation) | **Yes** — policy name or `ModerateOptions` |
| **Translate** | Chat | `ITranslateAdapter` (optional, e.g. LibreTranslate, DeepL) | No (target language is a parameter, not a template) |

### Part 4: Options Bags

Every verb gets an immutable `record` following the established pattern. All include the common routing triple (`Source`, `Model`, `VendorOptions`).

```csharp
public sealed record ImagineOptions
{
    public int?           Width           { get; init; }
    public int?           Height          { get; init; }
    public ImageStyle?    Style           { get; init; }  // Photographic, Digital, Anime, Sketch
    public double?        Guidance        { get; init; }  // CFG scale
    public int?           Steps           { get; init; }  // Sampling steps
    public int?           Seed            { get; init; }  // Reproducibility
    public string?        Negative        { get; init; }  // Negative prompt
    public ImageFormat?   Format          { get; init; }  // Png, Jpeg, WebP
    public byte[]?        Reference       { get; init; }  // img2img reference
    public double?        ReferenceWeight { get; init; }  // 0.0–1.0
    public string?        Source          { get; init; }
    public string?        Model           { get; init; }
    public IDictionary<string, object>? VendorOptions { get; init; }
}

public sealed record TranscribeOptions
{
    public string?            Language      { get; init; }  // ISO 639-1 hint
    public TranscriptFormat?  Format        { get; init; }  // PlainText, Srt, Vtt, Segments
    public bool?              Diarize       { get; init; }  // Speaker identification
    public bool?              Timestamps    { get; init; }  // Word-level timestamps
    public int?               SampleRate    { get; init; }  // For raw PCM streams
    public int?               Channels      { get; init; }  // For raw PCM streams
    public string?            Source        { get; init; }
    public string?            Model         { get; init; }
    public IDictionary<string, object>? VendorOptions { get; init; }
}

public sealed record SpeakOptions
{
    public string?       Voice    { get; init; }  // Voice ID or name
    public double?       Speed    { get; init; }  // 0.5–2.0
    public AudioFormat?  Format   { get; init; }  // Mp3, Wav, Ogg, Flac
    public string?       Language { get; init; }  // Language hint
    public string?       Style    { get; init; }  // Emotion/style (provider-dependent)
    public string?       Source   { get; init; }
    public string?       Model    { get; init; }
    public IDictionary<string, object>? VendorOptions { get; init; }
}

public sealed record DescribeOptions
{
    public Modality?         Modality { get; init; }  // Image, Video, Audio
    public DescribeDetail?   Detail   { get; init; }  // Brief, Standard, Detailed
    public DescribePurpose?  Purpose  { get; init; }  // General, AltText, ProductListing, SearchIndex, Caption
    public string?           Focus    { get; init; }  // Free-text focus hint
    public string?           Language { get; init; }  // Output language
    public string?           Source   { get; init; }
    public string?           Model    { get; init; }
    public IDictionary<string, object>? VendorOptions { get; init; }
}

public sealed record ClassifyOptions
{
    public Modality?          Modality   { get; init; }
    public string[]?          Labels     { get; init; }  // Inline label set
    public ClassifyTaxonomy?  Taxonomy   { get; init; }  // Hierarchical labels
    public string?            Prompt     { get; init; }  // Named prompt from catalog
    public bool?              MultiLabel { get; init; }  // Single vs multi-label
    public bool?              Confidence { get; init; }  // Return confidence scores
    public string?            Source     { get; init; }
    public string?            Model      { get; init; }
    public IDictionary<string, object>? VendorOptions { get; init; }
}

public sealed record ExtractOptions
{
    public Modality?  Modality { get; init; }
    public string?    Prompt   { get; init; }  // Named prompt for extraction instructions
    public bool?      Strict   { get; init; }  // Fail if schema can't be fully populated
    public Range?     Pages    { get; init; }  // Page range for documents
    public string?    Source   { get; init; }
    public string?    Model    { get; init; }
    public IDictionary<string, object>? VendorOptions { get; init; }
}

public sealed record RerankOptions
{
    public int?     TopN      { get; init; }  // Return top N only
    public double?  Threshold { get; init; }  // Minimum relevance score
    public string?  Source    { get; init; }
    public string?  Model     { get; init; }
    public IDictionary<string, object>? VendorOptions { get; init; }
}

public sealed record TranslateOptions
{
    public string           Target   { get; init; }  // ISO 639-1 target language (required)
    public TranslateTone?   Tone     { get; init; }  // Formal, Casual, Technical
    public string?          Glossary { get; init; }  // Custom terminology reference
    public string?          Source   { get; init; }
    public string?          Model    { get; init; }
    public IDictionary<string, object>? VendorOptions { get; init; }
}

public sealed record ModerateOptions
{
    public Modality?              Modality   { get; init; }
    public string?                Policy     { get; init; }  // Named policy from catalog
    public ModerationCategory[]?  Categories { get; init; }  // Scope to specific categories
    public double?                Threshold  { get; init; }  // Strictness (0.0–1.0)
    public string?                Source     { get; init; }
    public string?                Model      { get; init; }
    public IDictionary<string, object>? VendorOptions { get; init; }
}

public sealed record EditOptions
{
    public byte[]?        Mask     { get; init; }  // Region mask for targeted edits
    public ImageFormat?   Format   { get; init; }  // Output format
    public double?        Strength { get; init; }  // Edit strength (0.0–1.0)
    public string?        Source   { get; init; }
    public string?        Model    { get; init; }
    public IDictionary<string, object>? VendorOptions { get; init; }
}

public sealed record RenderOptions
{
    public TimeSpan?      Duration    { get; init; }  // Target duration
    public int?           Width       { get; init; }
    public int?           Height      { get; init; }
    public int?           Fps         { get; init; }
    public byte[]?        Reference   { get; init; }  // image-to-video reference
    public bool?          WithAudio   { get; init; }  // Native audio generation
    public VideoFormat?   Format      { get; init; }  // Mp4, WebM
    public int?           Seed        { get; init; }
    public TimeSpan?      Timeout     { get; init; }  // Default: 5 minutes
    public string?        Source      { get; init; }
    public string?        Model       { get; init; }
    public IDictionary<string, object>? VendorOptions { get; init; }
}
```

### Part 5: Client Facade — Verb Surface

Every verb follows the tier escalation established in AI-0021:

| Tier | Pattern | Example |
|------|---------|---------|
| 0 | Simple (zero config) | `Client.Imagine("prompt")` |
| 1 | Options bag | `Client.Imagine("prompt", new ImagineOptions { ... })` |
| 2 | Streaming (where real-time output is meaningful) | `Client.SpeakStream("text")`, `Client.TranscribeStream(audioStream)` |
| 3 | Entity convention | `Client.Imagine("prompt", entity)`, `Client.Describe(entity)` |
| 4 | Rich result | `Client.ImagineResult("prompt")` → `ImagineResult` |
| 5 | Batch | `Client.ImagineBatch(prompts)`, `Client.DescribeBatch(images)` |

Streaming applies only where incremental output is meaningful:
- `Client.SpeakStream(text)` → `IAsyncEnumerable<ReadOnlyMemory<byte>>` (play as generated)
- `Client.TranscribeStream(audioStream)` → `IAsyncEnumerable<TranscribeSegment>` (live captions)
- `Client.Stream(message)` → `IAsyncEnumerable<string>` (existing Chat stream)

**Full verb matrix:**

| Verb | Simple | Options | Stream | Entity | Result | Batch |
|------|--------|---------|--------|--------|--------|-------|
| `Chat` | ✓ | ✓ | ✓ | ✓ | ✓ | — |
| `Embed` | ✓ text / ✓ bytes+modality | ✓ | — | ✓ | ✓ | ✓ |
| `Ocr` | ✓ | ✓ | — | ✓ | ✓ | — |
| `Imagine` | ✓ | ✓ | — | ✓ | ✓ | ✓ |
| `Transcribe` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| `Speak` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| `Describe` | ✓ | ✓ | — | ✓ | ✓ | ✓ |
| `Classify` | ✓ (generic) | ✓ | — | ✓ | ✓ | ✓ |
| `Extract<T>` | ✓ | ✓ | — | ✓ | ✓ | ✓ |
| `Rerank` | ✓ | ✓ | — | ✓ | ✓ | — |
| `Translate` | ✓ | ✓ | — | ✓ | ✓ | ✓ |
| `Moderate` | ✓ | ✓ | — | ✓ | ✓ | ✓ |
| `Edit` | ✓ | ✓ | — | — | ✓ | — |
| `Render` | ✓ | ✓ | — | ✓ | ✓ | — |

### Part 6: Provider Taxonomy (Self-Hosted First)

Provider priority for each Protocol category, ranked by self-hosting viability:

#### Imagine (text → image)
1. ComfyUI (local, node-based, FLUX/SD3.5/SDXL, ControlNet, LoRA)
2. A1111/Forge (local, WebUI, same model ecosystem)
3. LocalAI (local, OpenAI-compatible)
4. Stability AI (cloud, best quality/cost)
5. OpenAI (cloud, GPT-image-1.5)
6. Google (cloud, Gemini 3 Pro Image)

#### Transcribe (audio → text)
1. whisper.cpp (local, C++, GPU-accelerated, OpenAI-compatible API)
2. faster-whisper (local, CTranslate2, 4x Whisper speed)
3. LocalAI (local, bundles Whisper)
4. Deepgram (cloud, Nova-3, lowest WER)
5. AssemblyAI (cloud, Universal-2, 99+ languages)
6. OpenAI (cloud, gpt-4o-transcribe)

#### Speak (text → audio)
1. Coqui XTTS (local, MIT, 1100+ languages, voice cloning)
2. Piper (local, fast, edge-optimized)
3. Bark (local, multilingual, expressive)
4. LocalAI (local, bundles TTS backends)
5. ElevenLabs (cloud, eleven_v3, most expressive)
6. OpenAI (cloud, gpt-4o-mini-tts, steerable)

#### Edit (image + instruction → image)
1. ComfyUI (local, inpainting/outpainting/ControlNet workflows)
2. A1111/Forge (local, img2img/inpaint extensions)
3. Stability AI (cloud, inpainting + background replacement)
4. OpenAI (cloud, GPT-image-1.5 edit mode)
5. Google (cloud, Gemini multi-turn edit)

#### Rerank (query + docs → scored docs)
1. Cross-encoders (local, sentence-transformers, ms-marco)
2. FlashRank (local, ultra-fast, edge-optimized)
3. Jina Reranker v2 (local via HF)
4. Cohere (cloud, Rerank 4.0, market-leading)

#### Render (text → video) — **Experimental**
1. ComfyUI (local, AnimateDiff/SVD, GPU-heavy, early)
2. CogVideo (local, open-source, early)
3. Runway (cloud, Gen-4/4.5, best quality)
4. Kling AI (cloud, cost-efficient, native audio)
5. Google (cloud, Veo 3, ecosystem integration)

#### Embed — Multimodal Extension
Modality-aware routing by adapter capability:

| Modality | Self-Hosted | Cloud |
|----------|------------|-------|
| Text | Ollama (nomic-embed-text, Qwen3-Embed) | OpenAI, Cohere, Mistral |
| Image | SigLIP 2, OpenCLIP, jina-clip-v2 | Google Gemini Embed 2, Cohere Embed 4 |
| Audio | CLAP | Google Gemini Embed 2 |
| Video | Frame-sampling → image embedder | Google Gemini Embed 2 |
| Document | PDF→text→text embedder | Google Gemini Embed 2 |

### Part 7: Scope Expansion

`Client.Scope()` gains parameters for every routable category:

```csharp
public static AiCategoryScope Scope(
    string? all        = null,
    string? chat       = null,
    string? embed      = null,
    string? ocr        = null,
    string? imagine    = null,
    string? transcribe = null,
    string? speak      = null,
    string? describe   = null,
    string? classify   = null,
    string? extract    = null,
    string? rerank     = null,
    string? translate  = null,
    string? moderate   = null,
    string? edit       = null,
    string? render     = null)
```

Task categories that delegate via Chat (Describe, Classify, Extract, Translate, Moderate) respect their own scope parameter first; if not set, fall through to the Chat scope; if not set, fall through to the `all` scope.

### Part 8: Expanded MediaAnalysis Flags

The `MediaAnalysis` flags enum (AI-0027) gains entries for new capabilities that apply to media entity lifecycle:

```csharp
[Flags]
public enum MediaAnalysis
{
    None        = 0,
    Describe    = 1,       // Existing — vision description
    Ocr         = 2,       // Existing — text extraction
    Transcribe  = 4,       // Existing flag — now backed by ITranscribeAdapter
    Classify    = 8,       // Existing flag — now with template support
    Extract     = 16,      // Existing flag — now with typed output
    Moderate    = 32,      // New — auto-moderate on upload
    Translate   = 64,      // New — auto-translate description/transcript
    Imagine     = 128,     // New — auto-generate derivative images (thumbnails, variants)
    All         = Describe | Ocr | Transcribe | Classify | Extract | Moderate | Translate | Imagine
}
```

### Part 9: Implementation Phases

**Phase 1 — Foundation** (highest persona coverage, self-hosted ready)
- `Modality` enum and `IEmbedAdapter.SupportedModalities`
- `Embed(bytes, Modality)` overload + multimodal embed routing
- `Describe` verb + `DescribeOptions` (via Chat, minimal new infra)
- `Transcribe` verb + `ITranscribeAdapter` + whisper.cpp adapter
- `Speak` verb + `ISpeakAdapter` + Coqui/Piper adapter

**Phase 2 — Generation & Intelligence**
- `Imagine` verb + `IImagineAdapter` + ComfyUI adapter
- `Edit` verb + `IEditAdapter` + ComfyUI inpaint adapter
- `Classify` verb with template support (labels, taxonomy, `[Flags]` enum)
- `Extract<T>` verb with typed schema extraction
- Batch overloads for Phase 1 + 2 verbs

**Phase 3 — Search & Safety**
- `Rerank` verb + `IRerankAdapter` + cross-encoder adapter
- `Moderate` verb + `IModerateAdapter` + LlamaGuard adapter
- `Translate` verb + `ITranslateAdapter` + LibreTranslate adapter
- `MediaAnalysis` flag expansion (Moderate, Translate, Imagine)

**Phase 4 — Experimental**
- `Render` verb + `IRenderAdapter` + ComfyUI AnimateDiff adapter
- Scope expansion with full category set
- Cloud adapter implementations (OpenAI, ElevenLabs, Stability, Cohere, Deepgram)

## Consequences

### Positive

- **Unified DX**: Developers learn one pattern (verb + options + result) and it works for 14 capabilities.
- **Self-hosted first**: Every Phase 1-2 capability has a local adapter, enabling zero-cost development and air-gapped deployment.
- **No breaking changes**: Existing `Client.Chat()`, `Client.Embed()`, `Client.Ocr()` remain unchanged. New verbs are additive.
- **Entity-first composability**: `[MediaAnalysis]` flags compose with new verbs — upload a video and get Describe + Transcribe + Classify + Embed automatically.
- **Cross-modal search**: `Embed(bytes, Modality.Image)` and `Embed(text)` in the same vector space enables "find images similar to this text" with zero custom code.

### Negative

- **Surface area growth**: 14 verbs × 5 tiers = ~70 method overloads on `Client`. Mitigated by consistent patterns and IDE discoverability.
- **Adapter explosion**: 6 new Protocol adapter interfaces. Each provider connector must implement relevant interfaces. Mitigated by most connectors implementing 2-3 interfaces (e.g., Ollama: Chat + Embed + Describe).
- **Options bag proliferation**: 12 new `*Options` records. Mitigated by consistent shape (all have Source/Model/VendorOptions) and immutable record design.

### Risks

- **Render maturity**: Video generation ecosystem is early. ComfyUI AnimateDiff is the only viable local option and requires significant GPU resources. Marked experimental.
- **Multimodal embed quality**: Cross-modal embedding quality varies significantly between adapters. SigLIP 2 excels at image-text, but audio-text (CLAP) and video embeddings are less mature. Router should surface adapter modality support clearly.
- **Batch semantics**: `ImagineBatch(prompts)` parallelizes internally, but rate limiting, error handling (partial success), and progress reporting need careful design. Defer detailed batch contract to implementation.

## References

- AI-0021: Category-Driven AI with Convention-Inferred Defaults
- AI-0025: Prompt Primitive
- AI-0026: Chain Composition
- AI-0027: [MediaAnalysis] Attribute
- AI-0032: Intent Capability Resolution with Recipes
- ARCH-0074: Framework Gap Analysis and Incremental Plan
