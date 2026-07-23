using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Koan.AI.Contracts;
using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Models;
using Koan.AI.Contracts.Options;
using Koan.AI.Contracts.Routing;
using Koan.AI.Context;
using Koan.Core;
using Koan.Core.Hosting.App;

namespace Koan.AI;

/// <summary>
/// Static client for Koan AI operations.
/// Single facade for Chat, Embed, OCR, Imagine, Transcribe, Speak, Describe,
/// Classify, Extract, Rerank, Translate, Moderate, Edit, and Render
/// with category-aware routing.
/// </summary>
public static class Client
{
    private const string ClientOperation = "AI client";
    private const string AdapterSelectionOperation = "AI adapter selection";

    private static readonly AsyncLocal<IAiPipeline?> _override = new();

    /// <summary>
    /// Override the AI pipeline for the current async context (useful for testing).
    /// </summary>
    public static IDisposable With(IAiPipeline @override)
    {
        var prev = _override.Value;
        _override.Value = @override;
        return new Reset(() => _override.Value = prev);
    }

    // ========================================================================
    // Chat
    // ========================================================================

    /// <summary>
    /// Chat with AI using a simple message.
    /// </summary>
    public static async Task<string> Chat(string message, CancellationToken ct = default)
    {
        var response = await Resolve().Prompt(BuildChatRequest(message, null), ct);
        return response.Text;
    }

    /// <summary>
    /// Chat with AI using detailed options.
    /// </summary>
    public static async Task<string> Chat(string message, ChatOptions options, CancellationToken ct = default)
    {
        var response = await Resolve().Prompt(BuildChatRequest(message, options), ct);
        return response.Text;
    }

    /// <summary>
    /// Chat with AI and return a rich result with metadata.
    /// </summary>
    public static async Task<ChatResult> ChatResult(string message, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var response = await Resolve().Prompt(BuildChatRequest(message, null), ct);
        sw.Stop();

        return new ChatResult
        {
            Text = response.Text,
            Model = response.Model,
            TokensIn = response.TokensIn,
            TokensOut = response.TokensOut,
            TokensUsed = (response.TokensIn ?? 0) + (response.TokensOut ?? 0),
            Latency = sw.Elapsed,
            AdapterId = response.AdapterId,
            FinishReason = response.FinishReason
        };
    }

    /// <summary>
    /// Chat with AI using detailed options and return a rich result with metadata.
    /// </summary>
    public static async Task<ChatResult> ChatResult(string message, ChatOptions options, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var response = await Resolve().Prompt(BuildChatRequest(message, options), ct);
        sw.Stop();

        return new ChatResult
        {
            Text = response.Text,
            Model = response.Model,
            TokensIn = response.TokensIn,
            TokensOut = response.TokensOut,
            TokensUsed = (response.TokensIn ?? 0) + (response.TokensOut ?? 0),
            Latency = sw.Elapsed,
            AdapterId = response.AdapterId,
            FinishReason = response.FinishReason
        };
    }

    // ========================================================================
    // Chat with Prompt
    // ========================================================================

    /// <summary>
    /// Chat with AI using a Prompt and variables for resolution.
    /// </summary>
    public static async Task<string> Chat(
        Prompt.Prompt prompt, object? variables = null, CancellationToken ct = default)
    {
        var message = prompt.Resolve(variables);
        var options = BuildOptionsFromPrompt(prompt);
        var response = await Resolve().Prompt(BuildChatRequest(message, options), ct);
        return response.Text;
    }

    /// <summary>
    /// Chat with AI using a Prompt and return a typed, parsed response.
    /// JSON schema constraint is sent to the model from Prompt.OutputFormat.
    /// </summary>
    public static async Task<T> Chat<T>(
        Prompt.Prompt prompt, object? variables = null, CancellationToken ct = default)
    {
        var message = prompt.Resolve(variables);
        var options = BuildOptionsFromPrompt(prompt);

        // Add JSON response format constraint if OutputSpec has a schema
        if (prompt.OutputFormat?.JsonSchema is not null)
        {
            options = options with { ResponseFormat = "json_object" };
        }

        var response = await Resolve().Prompt(BuildChatRequest(message, options), ct);
        return System.Text.Json.JsonSerializer.Deserialize<T>(response.Text)
            ?? throw new InvalidOperationException($"Failed to parse AI response as {typeof(T).Name}");
    }

    /// <summary>
    /// Chat with AI using a Prompt and return a typed, parsed response.
    /// Convenience overload: uses the prompt's raw text as the message.
    /// </summary>
    public static async Task<T> Chat<T>(string message, CancellationToken ct = default)
    {
        var options = new ChatOptions { ResponseFormat = "json_object" };
        var response = await Resolve().Prompt(BuildChatRequest(message, options), ct);
        return System.Text.Json.JsonSerializer.Deserialize<T>(response.Text)
            ?? throw new InvalidOperationException($"Failed to parse AI response as {typeof(T).Name}");
    }

    private static ChatOptions BuildOptionsFromPrompt(Prompt.Prompt prompt)
    {
        return new ChatOptions
        {
            SystemPrompt = prompt.System
        };
    }

    // ========================================================================
    // Stream
    // ========================================================================

    /// <summary>
    /// Stream chat responses from AI token-by-token.
    /// </summary>
    public static async IAsyncEnumerable<string> Stream(
        string message,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var chunk in Resolve().Stream(BuildChatRequest(message, null), ct))
        {
            if (!string.IsNullOrEmpty(chunk.DeltaText))
                yield return chunk.DeltaText;
        }
    }

    /// <summary>
    /// Stream chat responses from AI with detailed options.
    /// </summary>
    public static async IAsyncEnumerable<string> Stream(
        string message,
        ChatOptions options,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var chunk in Resolve().Stream(BuildChatRequest(message, options), ct))
        {
            if (!string.IsNullOrEmpty(chunk.DeltaText))
                yield return chunk.DeltaText;
        }
    }

    // ========================================================================
    // Embed
    // ========================================================================

    /// <summary>
    /// Generate an embedding vector for text.
    /// </summary>
    public static async Task<float[]> Embed(string text, CancellationToken ct = default)
    {
        var response = await Resolve().Embed(new AiEmbeddingsRequest
        {
            Input = new() { text }
        }, ct);
        return response.Vectors.FirstOrDefault() ?? [];
    }

    /// <summary>
    /// Generate an embedding vector for text with options.
    /// </summary>
    public static async Task<float[]> Embed(string text, EmbedOptions options, CancellationToken ct = default)
    {
        using var _source = options.Source is not null ? Scope(embed: options.Source) : null;
        var response = await Resolve().Embed(new AiEmbeddingsRequest
            {
                Input = new() { text },
                Model = options.Model,
                OverrideUrl = options.OverrideUrl,
                OverrideProvider = options.OverrideProvider,
            }, ct)
            .ConfigureAwait(false);
        return response.Vectors.FirstOrDefault() ?? [];
    }

    /// <summary>
    /// Generate embeddings for multiple texts in a single batch.
    /// </summary>
    public static async Task<float[][]> EmbedBatch(string[] texts, CancellationToken ct = default)
    {
        if (texts is null || texts.Length == 0)
            throw new ArgumentException("At least one text must be provided", nameof(texts));

        var response = await Resolve().Embed(new AiEmbeddingsRequest
        {
            Input = texts.ToList()
        }, ct);

        return response.Vectors.ToArray();
    }

    /// <summary>
    /// Generate an embedding and return a rich result with metadata.
    /// </summary>
    public static async Task<EmbedResult> EmbedResult(string text, CancellationToken ct = default)
    {
        var response = await Resolve().Embed(new AiEmbeddingsRequest
        {
            Input = new() { text }
        }, ct);

        var vector = response.Vectors.FirstOrDefault() ?? [];
        return new EmbedResult
        {
            Vector = vector,
            Model = response.Model,
            Dimension = vector.Length
        };
    }

    /// <summary>
    /// Low-level embed access for pipeline/internal use.
    /// </summary>
    public static Task<AiEmbeddingsResponse> Embed(AiEmbeddingsRequest req, CancellationToken ct = default)
        => Resolve().Embed(req, ct);

    // ========================================================================
    // OCR
    // ========================================================================

    /// <summary>
    /// Extract text from an image using OCR (delegates through Chat with vision model).
    /// </summary>
    public static async Task<string> Ocr(byte[] image, CancellationToken ct = default)
    {
        return await Ocr(image, new OcrOptions(), ct);
    }

    /// <summary>
    /// Extract text from an image using OCR with options.
    /// </summary>
    public static async Task<string> Ocr(byte[] image, OcrOptions options, CancellationToken ct = default)
    {
        if (image is null || image.Length == 0)
            throw new ArgumentException("Image data is required", nameof(image));

        var prompt = GetOcrPrompt(options.Format);
        var chatOptions = new ChatOptions
        {
            Image = image,
            ImageMimeType = options.MimeType,
            Model = options.Model,
            Source = options.Source
        };

        return await Chat(prompt, chatOptions, ct);
    }

    /// <summary>
    /// Extract text from an image and return a rich result.
    /// </summary>
    public static async Task<OcrResult> OcrResult(byte[] image, CancellationToken ct = default)
    {
        var text = await Ocr(image, ct);
        return new OcrResult { Text = text };
    }

    /// <summary>
    /// Extract text from an image with options and return a rich result.
    /// </summary>
    public static async Task<OcrResult> OcrResult(byte[] image, OcrOptions options, CancellationToken ct = default)
    {
        var text = await Ocr(image, options, ct);
        return new OcrResult
        {
            Text = text,
            Format = options.Format,
            Model = options.Model
        };
    }

    // ========================================================================
    // Imagine (text → image) — Protocol verb via IImagineAdapter
    // ========================================================================

    /// <summary>
    /// Generate an image from a text prompt.
    /// </summary>
    public static async Task<byte[]> Imagine(string prompt, CancellationToken ct = default)
    {
        var adapter = FindAdapter<IImagineAdapter>(AiCapability.Imagine);
        var response = await adapter.Imagine(new ImagineRequest { Prompt = prompt }, ct);
        return response.Image;
    }

    /// <summary>
    /// Generate an image from a text prompt with options.
    /// </summary>
    public static async Task<byte[]> Imagine(string prompt, ImagineOptions options, CancellationToken ct = default)
    {
        var adapter = FindAdapter<IImagineAdapter>(AiCapability.Imagine, options.Source);
        var response = await adapter.Imagine(new ImagineRequest
        {
            Prompt = prompt,
            Model = options.Model,
            Negative = options.Negative,
            Width = options.Width,
            Height = options.Height,
            Guidance = options.Guidance,
            Steps = options.Steps,
            Seed = options.Seed,
            Reference = options.Reference,
            ReferenceWeight = options.ReferenceWeight,
            Format = options.Format ?? ImageFormat.Png,
            VendorOptions = options.VendorOptions
        }, ct);
        return response.Image;
    }

    /// <summary>
    /// Generate an image and return a rich result with metadata.
    /// </summary>
    public static async Task<ImagineResult> ImagineResult(string prompt, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var adapter = FindAdapter<IImagineAdapter>(AiCapability.Imagine);
        var response = await adapter.Imagine(new ImagineRequest { Prompt = prompt }, ct);
        sw.Stop();

        return new ImagineResult
        {
            Image = response.Image,
            Format = response.Format,
            Model = response.Model,
            Seed = response.Seed,
            Width = response.Width,
            Height = response.Height,
            RevisedPrompt = response.RevisedPrompt,
            Latency = sw.Elapsed
        };
    }

    /// <summary>
    /// Generate an image with options and return a rich result with metadata.
    /// </summary>
    public static async Task<ImagineResult> ImagineResult(string prompt, ImagineOptions options, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var adapter = FindAdapter<IImagineAdapter>(AiCapability.Imagine, options.Source);
        var response = await adapter.Imagine(new ImagineRequest
        {
            Prompt = prompt,
            Model = options.Model,
            Negative = options.Negative,
            Width = options.Width,
            Height = options.Height,
            Guidance = options.Guidance,
            Steps = options.Steps,
            Seed = options.Seed,
            Reference = options.Reference,
            ReferenceWeight = options.ReferenceWeight,
            Format = options.Format ?? ImageFormat.Png,
            VendorOptions = options.VendorOptions
        }, ct);
        sw.Stop();

        return new ImagineResult
        {
            Image = response.Image,
            Format = response.Format,
            Model = response.Model,
            Seed = response.Seed,
            Width = response.Width,
            Height = response.Height,
            RevisedPrompt = response.RevisedPrompt,
            Latency = sw.Elapsed
        };
    }

    // ========================================================================
    // Transcribe (audio → text) — Protocol verb via ITranscribeAdapter
    // ========================================================================

    /// <summary>
    /// Transcribe audio to text.
    /// </summary>
    public static async Task<string> Transcribe(byte[] audio, CancellationToken ct = default)
    {
        if (audio is null || audio.Length == 0)
            throw new ArgumentException("Audio data is required", nameof(audio));

        var adapter = FindAdapter<ITranscribeAdapter>(AiCapability.Transcribe);
        var response = await adapter.Transcribe(new TranscribeRequest { Audio = audio }, ct);
        return response.Text;
    }

    /// <summary>
    /// Transcribe audio to text with options.
    /// </summary>
    public static async Task<string> Transcribe(byte[] audio, TranscribeOptions options, CancellationToken ct = default)
    {
        if (audio is null || audio.Length == 0)
            throw new ArgumentException("Audio data is required", nameof(audio));

        var adapter = FindAdapter<ITranscribeAdapter>(AiCapability.Transcribe, options.Source);
        var response = await adapter.Transcribe(new TranscribeRequest
        {
            Audio = audio,
            Model = options.Model,
            Language = options.Language,
            Format = options.Format ?? TranscriptFormat.PlainText,
            Diarize = options.Diarize,
            Timestamps = options.Timestamps,
            VendorOptions = options.VendorOptions
        }, ct);
        return response.Text;
    }

    /// <summary>
    /// Transcribe audio and return a rich result with segments and metadata.
    /// </summary>
    public static async Task<TranscribeResult> TranscribeResult(byte[] audio, CancellationToken ct = default)
    {
        if (audio is null || audio.Length == 0)
            throw new ArgumentException("Audio data is required", nameof(audio));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var adapter = FindAdapter<ITranscribeAdapter>(AiCapability.Transcribe);
        var response = await adapter.Transcribe(new TranscribeRequest { Audio = audio }, ct);
        sw.Stop();

        return new TranscribeResult
        {
            Text = response.Text,
            Language = response.Language,
            Duration = response.Duration,
            Model = response.Model,
            Segments = response.Segments,
            Latency = sw.Elapsed
        };
    }

    /// <summary>
    /// Transcribe audio with options and return a rich result with segments and metadata.
    /// </summary>
    public static async Task<TranscribeResult> TranscribeResult(byte[] audio, TranscribeOptions options, CancellationToken ct = default)
    {
        if (audio is null || audio.Length == 0)
            throw new ArgumentException("Audio data is required", nameof(audio));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var adapter = FindAdapter<ITranscribeAdapter>(AiCapability.Transcribe, options.Source);
        var response = await adapter.Transcribe(new TranscribeRequest
        {
            Audio = audio,
            Model = options.Model,
            Language = options.Language,
            Format = options.Format ?? TranscriptFormat.PlainText,
            Diarize = options.Diarize,
            Timestamps = options.Timestamps,
            VendorOptions = options.VendorOptions
        }, ct);
        sw.Stop();

        return new TranscribeResult
        {
            Text = response.Text,
            Language = response.Language,
            Duration = response.Duration,
            Model = response.Model,
            Segments = response.Segments,
            Latency = sw.Elapsed
        };
    }

    /// <summary>
    /// Stream transcription segments from audio.
    /// Returns null if the adapter does not support streaming transcription.
    /// </summary>
    public static IAsyncEnumerable<TranscribeSegment>? StreamTranscribe(
        System.IO.Stream audioStream,
        TranscribeOptions? options = null,
        CancellationToken ct = default)
    {
        var adapter = FindAdapter<ITranscribeAdapter>(AiCapability.Transcribe, options?.Source);
        return adapter.StreamTranscribe(audioStream, new TranscribeRequest
        {
            Audio = [], // Not used for streaming — audio comes from the stream parameter.
            Model = options?.Model,
            Language = options?.Language,
            Format = options?.Format ?? TranscriptFormat.PlainText,
            Diarize = options?.Diarize,
            Timestamps = options?.Timestamps,
            VendorOptions = options?.VendorOptions
        }, ct);
    }

    // ========================================================================
    // Speak (text → audio) — Protocol verb via ISpeakAdapter
    // ========================================================================

    /// <summary>
    /// Generate speech audio from text.
    /// </summary>
    public static async Task<byte[]> Speak(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text is required", nameof(text));

        var adapter = FindAdapter<ISpeakAdapter>(AiCapability.Speak);
        var response = await adapter.Speak(new SpeakRequest { Text = text }, ct);
        return response.Audio;
    }

    /// <summary>
    /// Generate speech audio from text with options.
    /// </summary>
    public static async Task<byte[]> Speak(string text, SpeakOptions options, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text is required", nameof(text));

        var adapter = FindAdapter<ISpeakAdapter>(AiCapability.Speak, options.Source);
        var response = await adapter.Speak(new SpeakRequest
        {
            Text = text,
            Model = options.Model,
            Voice = options.Voice,
            Speed = options.Speed,
            Format = options.Format ?? AudioFormat.Mp3,
            Language = options.Language,
            Style = options.Style,
            VendorOptions = options.VendorOptions
        }, ct);
        return response.Audio;
    }

    /// <summary>
    /// Generate speech audio and return a rich result with metadata.
    /// </summary>
    public static async Task<SpeakResult> SpeakResult(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text is required", nameof(text));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var adapter = FindAdapter<ISpeakAdapter>(AiCapability.Speak);
        var response = await adapter.Speak(new SpeakRequest { Text = text }, ct);
        sw.Stop();

        return new SpeakResult
        {
            Audio = response.Audio,
            Format = response.Format,
            Model = response.Model,
            Voice = response.Voice,
            Duration = response.Duration,
            Latency = sw.Elapsed
        };
    }

    /// <summary>
    /// Generate speech audio with options and return a rich result with metadata.
    /// </summary>
    public static async Task<SpeakResult> SpeakResult(string text, SpeakOptions options, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text is required", nameof(text));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var adapter = FindAdapter<ISpeakAdapter>(AiCapability.Speak, options.Source);
        var response = await adapter.Speak(new SpeakRequest
        {
            Text = text,
            Model = options.Model,
            Voice = options.Voice,
            Speed = options.Speed,
            Format = options.Format ?? AudioFormat.Mp3,
            Language = options.Language,
            Style = options.Style,
            VendorOptions = options.VendorOptions
        }, ct);
        sw.Stop();

        return new SpeakResult
        {
            Audio = response.Audio,
            Format = response.Format,
            Model = response.Model,
            Voice = response.Voice,
            Duration = response.Duration,
            Latency = sw.Elapsed
        };
    }

    /// <summary>
    /// Stream speech audio chunks as they are generated.
    /// Returns null if the adapter does not support streaming.
    /// </summary>
    public static IAsyncEnumerable<ReadOnlyMemory<byte>>? StreamSpeak(
        string text,
        SpeakOptions? options = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text is required", nameof(text));

        var adapter = FindAdapter<ISpeakAdapter>(AiCapability.Speak, options?.Source);
        return adapter.StreamSpeak(new SpeakRequest
        {
            Text = text,
            Model = options?.Model,
            Voice = options?.Voice,
            Speed = options?.Speed,
            Format = options?.Format ?? AudioFormat.Mp3,
            Language = options?.Language,
            Style = options?.Style,
            VendorOptions = options?.VendorOptions
        }, ct);
    }

    // ========================================================================
    // Describe (media → text) — Task verb delegating via Chat with vision
    // ========================================================================

    /// <summary>
    /// Describe media content (image, video, audio) using AI vision.
    /// </summary>
    public static async Task<string> Describe(byte[] content, CancellationToken ct = default)
    {
        if (content is null || content.Length == 0)
            throw new ArgumentException("Content data is required", nameof(content));

        return await Chat(
            "Describe this image in detail.",
            new ChatOptions { Image = content },
            ct);
    }

    /// <summary>
    /// Describe media content with options controlling detail, purpose, and modality.
    /// </summary>
    public static async Task<string> Describe(byte[] content, DescribeOptions options, CancellationToken ct = default)
    {
        if (content is null || content.Length == 0)
            throw new ArgumentException("Content data is required", nameof(content));

        var prompt = BuildDescribePrompt(options);
        var chatOptions = new ChatOptions
        {
            Image = content,
            Model = options.Model,
            Source = options.Source
        };

        return await Chat(prompt, chatOptions, ct);
    }

    /// <summary>
    /// Describe media content and return a rich result with metadata.
    /// </summary>
    public static async Task<DescribeResult> DescribeResult(byte[] content, CancellationToken ct = default)
    {
        if (content is null || content.Length == 0)
            throw new ArgumentException("Content data is required", nameof(content));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var text = await Describe(content, ct);
        sw.Stop();

        return new DescribeResult
        {
            Text = text,
            Modality = Modality.Image,
            Latency = sw.Elapsed
        };
    }

    /// <summary>
    /// Describe media content with options and return a rich result with metadata.
    /// </summary>
    public static async Task<DescribeResult> DescribeResult(byte[] content, DescribeOptions options, CancellationToken ct = default)
    {
        if (content is null || content.Length == 0)
            throw new ArgumentException("Content data is required", nameof(content));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var text = await Describe(content, options, ct);
        sw.Stop();

        return new DescribeResult
        {
            Text = text,
            Model = options.Model,
            Modality = options.Modality ?? Modality.Image,
            Latency = sw.Elapsed
        };
    }

    // ========================================================================
    // Classify (content → label) — Task verb delegating via Chat with JSON mode
    // ========================================================================

    /// <summary>
    /// Classify content into one of the provided labels.
    /// </summary>
    public static async Task<string> Classify(string content, string[] labels, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Content is required", nameof(content));
        if (labels is null || labels.Length == 0)
            throw new ArgumentException("At least one label is required", nameof(labels));

        var prompt = BuildClassifyPrompt(content, labels, multiLabel: false);
        var result = await Chat<ClassifyResponse>(prompt, ct);
        return result.Label;
    }

    /// <summary>
    /// Classify content with options (multi-label, confidence scores, custom labels).
    /// </summary>
    public static async Task<string[]> Classify(string content, ClassifyOptions options, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Content is required", nameof(content));
        if (options.Labels is null || options.Labels.Length == 0)
            throw new ArgumentException("At least one label is required via ClassifyOptions.Labels");

        var prompt = BuildClassifyPrompt(content, options.Labels, options.MultiLabel ?? false);
        var chatOptions = new ChatOptions
        {
            ResponseFormat = "json_object",
            Model = options.Model,
            Source = options.Source
        };
        var response = await Resolve().Prompt(BuildChatRequest(prompt, chatOptions), ct);
        var parsed = System.Text.Json.JsonSerializer.Deserialize<ClassifyMultiResponse>(response.Text)
            ?? throw new InvalidOperationException("Failed to parse classification response");
        return parsed.Labels;
    }

    /// <summary>
    /// Classify content and return a rich typed result with confidence scores.
    /// </summary>
    public static async Task<ClassifyResult<string>> ClassifyResult(
        string content, ClassifyOptions options, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Content is required", nameof(content));
        if (options.Labels is null || options.Labels.Length == 0)
            throw new ArgumentException("At least one label is required via ClassifyOptions.Labels");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var prompt = BuildClassifyPrompt(content, options.Labels, options.MultiLabel ?? false, withConfidence: true);
        var chatOptions = new ChatOptions
        {
            ResponseFormat = "json_object",
            Model = options.Model,
            Source = options.Source
        };
        var response = await Resolve().Prompt(BuildChatRequest(prompt, chatOptions), ct);
        sw.Stop();

        var parsed = System.Text.Json.JsonSerializer.Deserialize<ClassifyDetailedResponse>(response.Text)
            ?? throw new InvalidOperationException("Failed to parse classification response");

        return new ClassifyResult<string>
        {
            Label = parsed.Label,
            Confidence = parsed.Confidence,
            AllScores = parsed.Scores,
            Model = options.Model,
            Latency = sw.Elapsed
        };
    }

    // ========================================================================
    // Extract (content → typed object) — Task verb delegating via Chat with JSON mode
    // ========================================================================

    /// <summary>
    /// Extract a typed object from text content using AI.
    /// The generic type parameter defines the extraction schema.
    /// </summary>
    public static async Task<T> Extract<T>(string content, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Content is required", nameof(content));

        var prompt = $"Extract structured data from the following content. " +
                     $"Return a JSON object matching the {typeof(T).Name} schema.\n\nContent:\n{content}";

        return await Chat<T>(prompt, ct);
    }

    /// <summary>
    /// Extract a typed object from text content with options.
    /// </summary>
    public static async Task<T> Extract<T>(string content, ExtractOptions options, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Content is required", nameof(content));

        var systemPrompt = options.Strict == true
            ? "You are a strict data extraction agent. All fields must be populated or return an error."
            : "You are a data extraction agent. Populate as many fields as possible from the content.";

        var userPrompt = options.Prompt is not null
            ? $"{options.Prompt}\n\nContent:\n{content}"
            : $"Extract structured data from the following content. " +
              $"Return a JSON object matching the {typeof(T).Name} schema.\n\nContent:\n{content}";

        var chatOptions = new ChatOptions
        {
            SystemPrompt = systemPrompt,
            ResponseFormat = "json_object",
            Model = options.Model,
            Source = options.Source
        };

        var response = await Resolve().Prompt(BuildChatRequest(userPrompt, chatOptions), ct);
        return System.Text.Json.JsonSerializer.Deserialize<T>(response.Text)
            ?? throw new InvalidOperationException($"Failed to parse extraction response as {typeof(T).Name}");
    }

    /// <summary>
    /// Extract a typed object and return a rich result with confidence metadata.
    /// </summary>
    public static async Task<ExtractResult<T>> ExtractResult<T>(
        string content, ExtractOptions? options = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Content is required", nameof(content));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var value = options is not null
            ? await Extract<T>(content, options, ct)
            : await Extract<T>(content, ct);
        sw.Stop();

        return new ExtractResult<T>
        {
            Value = value,
            Model = options?.Model,
            Latency = sw.Elapsed
        };
    }

    // ========================================================================
    // Rerank (query + docs → scored docs) — Protocol verb via IRerankAdapter
    // ========================================================================

    /// <summary>
    /// Rerank documents by relevance to a query.
    /// </summary>
    public static async Task<RankedDocument[]> Rerank(
        string query, IReadOnlyList<string> documents, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query is required", nameof(query));
        if (documents is null || documents.Count == 0)
            throw new ArgumentException("At least one document is required", nameof(documents));

        var adapter = FindAdapter<IRerankAdapter>(AiCapability.Rerank);
        var response = await adapter.Rerank(new RerankRequest
        {
            Query = query,
            Documents = documents
        }, ct);
        return response.Documents.ToArray();
    }

    /// <summary>
    /// Rerank documents by relevance with options (top-N, threshold).
    /// </summary>
    public static async Task<RankedDocument[]> Rerank(
        string query, IReadOnlyList<string> documents, RerankOptions options, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query is required", nameof(query));
        if (documents is null || documents.Count == 0)
            throw new ArgumentException("At least one document is required", nameof(documents));

        var adapter = FindAdapter<IRerankAdapter>(AiCapability.Rerank, options.Source);
        var response = await adapter.Rerank(new RerankRequest
        {
            Query = query,
            Documents = documents,
            Model = options.Model,
            TopN = options.TopN,
            Threshold = options.Threshold,
            VendorOptions = options.VendorOptions
        }, ct);
        return response.Documents.ToArray();
    }

    /// <summary>
    /// Rerank documents and return a rich result with metadata.
    /// </summary>
    public static async Task<RerankResult> RerankResult(
        string query, IReadOnlyList<string> documents, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query is required", nameof(query));
        if (documents is null || documents.Count == 0)
            throw new ArgumentException("At least one document is required", nameof(documents));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var adapter = FindAdapter<IRerankAdapter>(AiCapability.Rerank);
        var response = await adapter.Rerank(new RerankRequest
        {
            Query = query,
            Documents = documents
        }, ct);
        sw.Stop();

        return new RerankResult
        {
            Documents = response.Documents,
            Model = response.Model,
            Latency = sw.Elapsed
        };
    }

    /// <summary>
    /// Rerank documents with options and return a rich result with metadata.
    /// </summary>
    public static async Task<RerankResult> RerankResult(
        string query, IReadOnlyList<string> documents, RerankOptions options, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query is required", nameof(query));
        if (documents is null || documents.Count == 0)
            throw new ArgumentException("At least one document is required", nameof(documents));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var adapter = FindAdapter<IRerankAdapter>(AiCapability.Rerank, options.Source);
        var response = await adapter.Rerank(new RerankRequest
        {
            Query = query,
            Documents = documents,
            Model = options.Model,
            TopN = options.TopN,
            Threshold = options.Threshold,
            VendorOptions = options.VendorOptions
        }, ct);
        sw.Stop();

        return new RerankResult
        {
            Documents = response.Documents,
            Model = response.Model,
            Latency = sw.Elapsed
        };
    }

    // ========================================================================
    // Translate (text → translated text) — Task verb delegating via Chat
    // ========================================================================

    /// <summary>
    /// Translate text to a target language.
    /// </summary>
    public static async Task<string> Translate(string text, string targetLanguage, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text is required", nameof(text));
        if (string.IsNullOrWhiteSpace(targetLanguage))
            throw new ArgumentException("Target language is required", nameof(targetLanguage));

        var prompt = $"Translate the following text to {targetLanguage}. " +
                     "Return only the translated text, nothing else.\n\n" + text;
        return await Chat(prompt, ct);
    }

    /// <summary>
    /// Translate text with options (tone, source language hint).
    /// </summary>
    public static async Task<string> Translate(string text, TranslateOptions options, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text is required", nameof(text));

        var prompt = BuildTranslatePrompt(text, options);
        var chatOptions = new ChatOptions
        {
            Model = options.Model,
            Source = options.Source
        };

        return await Chat(prompt, chatOptions, ct);
    }

    /// <summary>
    /// Translate text with options and return a rich result with metadata.
    /// </summary>
    public static async Task<TranslateResult> TranslateResult(
        string text, TranslateOptions options, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text is required", nameof(text));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var translated = await Translate(text, options, ct);
        sw.Stop();

        return new TranslateResult
        {
            Text = translated,
            Target = options.Target,
            Model = options.Model,
            Latency = sw.Elapsed
        };
    }

    // ========================================================================
    // Moderate (content → verdict) — Task verb delegating via Chat with JSON mode
    // ========================================================================

    /// <summary>
    /// Moderate content and return a simple verdict.
    /// </summary>
    public static async Task<ModerationVerdict> Moderate(string content, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Content is required", nameof(content));

        var prompt = BuildModeratePrompt(content, policy: null, threshold: null);
        var result = await Chat<ModerationVerdictInternal>(prompt, ct);
        return new ModerationVerdict
        {
            Allowed = result.Allowed,
            Flags = result.Flags ?? []
        };
    }

    /// <summary>
    /// Moderate content with options (custom policy, threshold).
    /// </summary>
    public static async Task<ModerationVerdict> Moderate(string content, ModerateOptions options, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Content is required", nameof(content));

        var prompt = BuildModeratePrompt(content, options.Policy, options.Threshold);
        var chatOptions = new ChatOptions
        {
            ResponseFormat = "json_object",
            Model = options.Model,
            Source = options.Source
        };
        var response = await Resolve().Prompt(BuildChatRequest(prompt, chatOptions), ct);
        var result = System.Text.Json.JsonSerializer.Deserialize<ModerationVerdictInternal>(response.Text)
            ?? throw new InvalidOperationException("Failed to parse moderation response");

        return new ModerationVerdict
        {
            Allowed = result.Allowed,
            Flags = result.Flags ?? []
        };
    }

    /// <summary>
    /// Moderate content and return a rich result with category scores.
    /// </summary>
    public static async Task<ModerateResult> ModerateResult(string content, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Content is required", nameof(content));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var prompt = BuildModeratePrompt(content, policy: null, threshold: null, detailed: true);
        var result = await Chat<ModerateDetailedResponse>(prompt, ct);
        sw.Stop();

        return new ModerateResult
        {
            Allowed = result.Allowed,
            Flags = result.Flags,
            CategoryScores = result.CategoryScores,
            Latency = sw.Elapsed
        };
    }

    /// <summary>
    /// Moderate content with options and return a rich result with category scores.
    /// </summary>
    public static async Task<ModerateResult> ModerateResult(string content, ModerateOptions options, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Content is required", nameof(content));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var prompt = BuildModeratePrompt(content, options.Policy, options.Threshold, detailed: true);
        var chatOptions = new ChatOptions
        {
            ResponseFormat = "json_object",
            Model = options.Model,
            Source = options.Source
        };
        var response = await Resolve().Prompt(BuildChatRequest(prompt, chatOptions), ct);
        sw.Stop();

        var result = System.Text.Json.JsonSerializer.Deserialize<ModerateDetailedResponse>(response.Text)
            ?? throw new InvalidOperationException("Failed to parse moderation response");

        return new ModerateResult
        {
            Allowed = result.Allowed,
            Flags = result.Flags,
            CategoryScores = result.CategoryScores,
            Model = options.Model,
            Latency = sw.Elapsed
        };
    }

    // ========================================================================
    // Edit (image + instruction → image) — Protocol verb via IEditAdapter
    // ========================================================================

    /// <summary>
    /// Edit an image based on a text instruction.
    /// </summary>
    public static async Task<byte[]> Edit(byte[] image, string instruction, CancellationToken ct = default)
    {
        if (image is null || image.Length == 0)
            throw new ArgumentException("Image data is required", nameof(image));
        if (string.IsNullOrWhiteSpace(instruction))
            throw new ArgumentException("Instruction is required", nameof(instruction));

        var adapter = FindAdapter<IEditAdapter>(AiCapability.Edit);
        var response = await adapter.Edit(new EditRequest
        {
            Image = image,
            Instruction = instruction
        }, ct);
        return response.Image;
    }

    /// <summary>
    /// Edit an image with options (mask, strength, format).
    /// </summary>
    public static async Task<byte[]> Edit(byte[] image, string instruction, EditOptions options, CancellationToken ct = default)
    {
        if (image is null || image.Length == 0)
            throw new ArgumentException("Image data is required", nameof(image));
        if (string.IsNullOrWhiteSpace(instruction))
            throw new ArgumentException("Instruction is required", nameof(instruction));

        var adapter = FindAdapter<IEditAdapter>(AiCapability.Edit, options.Source);
        var response = await adapter.Edit(new EditRequest
        {
            Image = image,
            Instruction = instruction,
            Model = options.Model,
            Mask = options.Mask,
            Strength = options.Strength,
            Format = options.Format ?? ImageFormat.Png,
            VendorOptions = options.VendorOptions
        }, ct);
        return response.Image;
    }

    /// <summary>
    /// Edit an image and return a rich result with metadata.
    /// </summary>
    public static async Task<EditResult> EditResult(byte[] image, string instruction, CancellationToken ct = default)
    {
        if (image is null || image.Length == 0)
            throw new ArgumentException("Image data is required", nameof(image));
        if (string.IsNullOrWhiteSpace(instruction))
            throw new ArgumentException("Instruction is required", nameof(instruction));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var adapter = FindAdapter<IEditAdapter>(AiCapability.Edit);
        var response = await adapter.Edit(new EditRequest
        {
            Image = image,
            Instruction = instruction
        }, ct);
        sw.Stop();

        return new EditResult
        {
            Image = response.Image,
            Format = response.Format,
            Model = response.Model,
            Latency = sw.Elapsed
        };
    }

    /// <summary>
    /// Edit an image with options and return a rich result with metadata.
    /// </summary>
    public static async Task<EditResult> EditResult(byte[] image, string instruction, EditOptions options, CancellationToken ct = default)
    {
        if (image is null || image.Length == 0)
            throw new ArgumentException("Image data is required", nameof(image));
        if (string.IsNullOrWhiteSpace(instruction))
            throw new ArgumentException("Instruction is required", nameof(instruction));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var adapter = FindAdapter<IEditAdapter>(AiCapability.Edit, options.Source);
        var response = await adapter.Edit(new EditRequest
        {
            Image = image,
            Instruction = instruction,
            Model = options.Model,
            Mask = options.Mask,
            Strength = options.Strength,
            Format = options.Format ?? ImageFormat.Png,
            VendorOptions = options.VendorOptions
        }, ct);
        sw.Stop();

        return new EditResult
        {
            Image = response.Image,
            Format = response.Format,
            Model = response.Model,
            Latency = sw.Elapsed
        };
    }

    // ========================================================================
    // Render (text → video) — Protocol verb via IRenderAdapter [Experimental]
    // ========================================================================

    /// <summary>
    /// Generate a video from a text prompt. Experimental.
    /// </summary>
    public static async Task<byte[]> Render(string prompt, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("Prompt is required", nameof(prompt));

        var adapter = FindAdapter<IRenderAdapter>(AiCapability.Render);
        var response = await adapter.Render(new RenderRequest { Prompt = prompt }, ct);
        return response.Video;
    }

    /// <summary>
    /// Generate a video from a text prompt with options. Experimental.
    /// </summary>
    public static async Task<byte[]> Render(string prompt, RenderOptions options, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("Prompt is required", nameof(prompt));

        var adapter = FindAdapter<IRenderAdapter>(AiCapability.Render, options.Source);
        var response = await adapter.Render(new RenderRequest
        {
            Prompt = prompt,
            Model = options.Model,
            Duration = options.Duration,
            Width = options.Width,
            Height = options.Height,
            Fps = options.Fps,
            Reference = options.Reference,
            WithAudio = options.WithAudio,
            Seed = options.Seed,
            Format = options.Format ?? VideoFormat.Mp4,
            Timeout = options.Timeout,
            VendorOptions = options.VendorOptions
        }, ct);
        return response.Video;
    }

    /// <summary>
    /// Generate a video and return a rich result with metadata. Experimental.
    /// </summary>
    public static async Task<RenderResult> RenderResult(string prompt, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("Prompt is required", nameof(prompt));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var adapter = FindAdapter<IRenderAdapter>(AiCapability.Render);
        var response = await adapter.Render(new RenderRequest { Prompt = prompt }, ct);
        sw.Stop();

        return new RenderResult
        {
            Video = response.Video,
            Format = response.Format,
            Model = response.Model,
            Duration = response.Duration,
            Width = response.Width,
            Height = response.Height,
            Fps = response.Fps,
            Seed = response.Seed,
            Latency = sw.Elapsed
        };
    }

    /// <summary>
    /// Generate a video with options and return a rich result with metadata. Experimental.
    /// </summary>
    public static async Task<RenderResult> RenderResult(string prompt, RenderOptions options, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("Prompt is required", nameof(prompt));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var adapter = FindAdapter<IRenderAdapter>(AiCapability.Render, options.Source);
        var response = await adapter.Render(new RenderRequest
        {
            Prompt = prompt,
            Model = options.Model,
            Duration = options.Duration,
            Width = options.Width,
            Height = options.Height,
            Fps = options.Fps,
            Reference = options.Reference,
            WithAudio = options.WithAudio,
            Seed = options.Seed,
            Format = options.Format ?? VideoFormat.Mp4,
            Timeout = options.Timeout,
            VendorOptions = options.VendorOptions
        }, ct);
        sw.Stop();

        return new RenderResult
        {
            Video = response.Video,
            Format = response.Format,
            Model = response.Model,
            Duration = response.Duration,
            Width = response.Width,
            Height = response.Height,
            Fps = response.Fps,
            Seed = response.Seed,
            Latency = sw.Elapsed
        };
    }

    // ========================================================================
    // Scope (replaces Context — per-category routing)
    // ========================================================================

    /// <summary>
    /// Create a scoped routing context with per-category overrides.
    /// Categories: "all" applies to all categories; named parameters target specific categories.
    /// </summary>
    public static AiCategoryScope Scope(
        string? all = null,
        string? chat = null,
        string? embed = null,
        string? ocr = null,
        string? imagine = null,
        string? transcribe = null,
        string? speak = null,
        string? describe = null,
        string? classify = null,
        string? extract = null,
        string? rerank = null,
        string? translate = null,
        string? moderate = null,
        string? edit = null,
        string? render = null)
    {
        return new AiCategoryScope(
            all: all,
            chatSource: chat,
            embedSource: embed,
            ocrSource: ocr,
            imagineSource: imagine,
            transcribeSource: transcribe,
            speakSource: speak,
            describeSource: describe,
            classifySource: classify,
            extractSource: extract,
            rerankSource: rerank,
            translateSource: translate,
            moderateSource: moderate,
            editSource: edit,
            renderSource: render);
    }

    // ========================================================================
    // Conversation Builder
    // ========================================================================

    public static AiConversationBuilder Conversation()
        => new(Resolve());

    // ========================================================================
    // Discovery
    // ========================================================================

    public static bool IsAvailable
        => _override.Value is not null || TryResolveCurrent() is not null;

    public static IAiPipeline? TryResolve()
        => _override.Value ?? TryResolveCurrent();

    // ========================================================================
    // Internal — Pipeline & Chat
    // ========================================================================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static IAiPipeline Resolve()
    {
        if (_override.Value is IAiPipeline o) return o;
        return AppHost.GetRequiredService<IAiPipeline>(ClientOperation);
    }

    private static IAiPipeline? TryResolveCurrent()
    {
        var services = AppHost.Current;
        if (services is null) return null;

        try
        {
            return services.GetService(typeof(IAiPipeline)) as IAiPipeline;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    private static AiChatRequest BuildChatRequest(string message, ChatOptions? options)
    {
        var messages = new List<AiMessage>();

        if (options?.Messages is { Count: > 0 })
        {
            messages.AddRange(options.Messages);
        }
        else
        {
            messages.Add(new AiMessage("user", message));
        }

        if (!string.IsNullOrWhiteSpace(options?.SystemPrompt))
        {
            messages.Insert(0, new AiMessage("system", options.SystemPrompt));
        }

        // Handle multimodal (image)
        if (options?.Image is { Length: > 0 })
        {
            var idx = messages.FindLastIndex(m => m.Role == "user");
            if (idx >= 0)
            {
                var userMsg = messages[idx];
                messages[idx] = userMsg with
                {
                    Parts = new List<AiMessagePart>
                    {
                        new() { Type = "text", Text = userMsg.Content },
                        new() { Type = "image", Data = options.Image, MimeType = options.ImageMimeType ?? "image/jpeg" }
                    }
                };
            }
        }

        AiPromptOptions? promptOpts = null;
        if (options is not null)
        {
            promptOpts = new AiPromptOptions
            {
                Temperature = options.Temperature,
                MaxOutputTokens = options.MaxTokens,
                TopP = options.TopP,
                Stop = options.Stop,
                Seed = options.Seed,
                Think = options.Think,
                ResponseFormat = options.ResponseFormat
            };
        }

        var (scopeSource, _) = AiCategoryScope.ResolveMerged("Chat", options?.Source);
        var model = options?.Model;

        return new AiChatRequest
        {
            Messages = messages,
            Model = model,
            Options = promptOpts,
            Route = scopeSource is not null
                ? new AiRouteHints { Source = scopeSource }
                : null
        };
    }

    private static string GetOcrPrompt(OcrFormat format) => format switch
    {
        OcrFormat.Markdown =>
            "Extract all text from this image. Format the output as Markdown, preserving headings, lists, and structure.",
        OcrFormat.Structured =>
            "Extract all text from this image. Return a JSON object with regions, each containing: text, confidence (0-1), and bounding_box (x, y, width, height).",
        _ =>
            "Extract all text from this image. Return only the extracted text, preserving the original formatting."
    };

    // ========================================================================
    // Internal — Adapter Resolution
    // ========================================================================

    /// <summary>
    /// Resolve a typed adapter from the registry, optionally filtering by source.
    /// Used by Protocol verbs (Imagine, Transcribe, Speak, Rerank, Edit, Render)
    /// that bypass the pipeline and call adapters directly.
    /// </summary>
    private static TAdapter FindAdapter<TAdapter>(string capability, string? sourceOverride = null)
        where TAdapter : class, IAiAdapter
    {
        var registry = AppHost.GetRequiredService<IAiAdapterRegistry>(AdapterSelectionOperation);

        var (source, _) = AiCategoryScope.ResolveMerged(capability, sourceOverride);

        // If a source is specified, try to find that specific adapter first.
        if (source is not null)
        {
            var byId = registry.Get(source);
            if (byId is TAdapter typed)
                return typed;
        }

        // Fall back to first adapter implementing the requested interface.
        var adapter = registry.All.OfType<TAdapter>().FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"No adapter registered for {typeof(TAdapter).Name}. " +
                $"Add a provider that supports the '{capability}' capability.");

        return adapter;
    }

    // ========================================================================
    // Internal — Prompt Builders for Task Verbs
    // ========================================================================

    private static string BuildDescribePrompt(DescribeOptions options)
    {
        var detail = options.Detail switch
        {
            DescribeDetail.Brief => "briefly",
            DescribeDetail.Detailed => "in extensive detail",
            _ => "in a clear and thorough manner"
        };

        var purpose = options.Purpose switch
        {
            DescribePurpose.AltText => "Write alt text suitable for screen readers.",
            DescribePurpose.Caption => "Write a concise caption.",
            DescribePurpose.ProductListing => "Describe for an e-commerce product listing.",
            DescribePurpose.SearchIndex => "Describe for search indexing. Include all visible text, objects, colors, and spatial relationships.",
            _ => "Describe this image."
        };

        var focus = options.Focus is not null ? $" Focus on: {options.Focus}." : "";
        var language = options.Language is not null ? $" Respond in {options.Language}." : "";

        return $"{purpose} Describe {detail}.{focus}{language}";
    }

    private static string BuildClassifyPrompt(string content, string[] labels, bool multiLabel, bool withConfidence = false)
    {
        var labelsJoined = string.Join(", ", labels.Select(l => $"\"{l}\""));

        if (withConfidence)
        {
            return multiLabel
                ? $"Classify the following content into one or more of these labels: [{labelsJoined}]. " +
                  "Return a JSON object with: \"label\" (best match), \"labels\" (array of matching labels), " +
                  "\"confidence\" (0.0-1.0 for best match), \"scores\" (object mapping each label to its score).\n\nContent:\n" + content
                : $"Classify the following content into exactly one of these labels: [{labelsJoined}]. " +
                  "Return a JSON object with: \"label\" (the chosen label), " +
                  "\"confidence\" (0.0-1.0), \"scores\" (object mapping each label to its score).\n\nContent:\n" + content;
        }

        return multiLabel
            ? $"Classify the following content into one or more of these labels: [{labelsJoined}]. " +
              "Return a JSON object with a \"labels\" array containing the matching labels.\n\nContent:\n" + content
            : $"Classify the following content into exactly one of these labels: [{labelsJoined}]. " +
              "Return a JSON object with a \"label\" field containing the chosen label.\n\nContent:\n" + content;
    }

    private static string BuildTranslatePrompt(string text, TranslateOptions options)
    {
        var tone = options.Tone switch
        {
            TranslateTone.Formal => " Use formal register.",
            TranslateTone.Casual => " Use casual/informal register.",
            TranslateTone.Technical => " Preserve technical terminology accurately.",
            _ => ""
        };

        var sourceLang = options.SourceLanguage is not null
            ? $" The source language is {options.SourceLanguage}."
            : "";

        return $"Translate the following text to {options.Target}.{sourceLang}{tone} " +
               "Return only the translated text, nothing else.\n\n" + text;
    }

    private static string BuildModeratePrompt(string content, string? policy, double? threshold, bool detailed = false)
    {
        var policyClause = policy is not null ? $" Apply moderation policy: \"{policy}\"." : "";
        var thresholdClause = threshold.HasValue
            ? $" Flag content with a severity score above {threshold.Value:F2}."
            : "";

        if (detailed)
        {
            return "You are a content moderation system. Analyze the following content for policy violations." +
                   policyClause + thresholdClause +
                   " Return a JSON object with: \"allowed\" (boolean), \"flags\" (array of violation categories), " +
                   "\"category_scores\" (object mapping categories like \"violence\", \"hate\", \"sexual\", " +
                   "\"self_harm\", \"dangerous\" to severity scores 0.0-1.0).\n\nContent:\n" + content;
        }

        return "You are a content moderation system. Analyze the following content for policy violations." +
               policyClause + thresholdClause +
               " Return a JSON object with: \"allowed\" (boolean), \"flags\" (array of violation categories like " +
               "\"violence\", \"hate\", \"sexual\", \"self_harm\", \"dangerous\").\n\nContent:\n" + content;
    }

    // ========================================================================
    // Internal — JSON Deserialization Models for Task Verbs
    // ========================================================================

    private sealed record ClassifyResponse
    {
        public string Label { get; init; } = "";
    }

    private sealed record ClassifyMultiResponse
    {
        public string[] Labels { get; init; } = [];
    }

    private sealed record ClassifyDetailedResponse
    {
        public string Label { get; init; } = "";
        public double? Confidence { get; init; }
        public IReadOnlyDictionary<string, double>? Scores { get; init; }
    }

    private sealed record ModerationVerdictInternal
    {
        public bool Allowed { get; init; }
        public IReadOnlyList<string>? Flags { get; init; }
    }

    private sealed record ModerateDetailedResponse
    {
        public bool Allowed { get; init; }
        public IReadOnlyList<string>? Flags { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("category_scores")]
        public IReadOnlyDictionary<string, double>? CategoryScores { get; init; }
    }

    // ========================================================================
    // Internal — Infrastructure
    // ========================================================================

    private sealed class Reset : IDisposable
    {
        private readonly Action _onDispose;
        public Reset(Action onDispose) => _onDispose = onDispose;
        public void Dispose() => _onDispose();
    }
}
