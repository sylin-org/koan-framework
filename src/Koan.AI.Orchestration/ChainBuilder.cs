using System.Runtime.CompilerServices;

namespace Koan.AI.Orchestration;

/// <summary>
/// Fluent, immutable builder for AI chain composition.
/// Each method returns a new builder instance — the original is never mutated.
/// </summary>
public sealed class ChainBuilder
{
    private readonly IReadOnlyList<ChainStep> _steps;
    private readonly string? _systemMessage;
    private readonly string? _chatModel;
    private readonly string? _embedModel;
    private readonly ChainMemory? _memory;

    internal ChainBuilder()
        : this([], systemMessage: null, chatModel: null, embedModel: null, memory: null) { }

    private ChainBuilder(
        IReadOnlyList<ChainStep> steps,
        string? systemMessage,
        string? chatModel,
        string? embedModel,
        ChainMemory? memory)
    {
        _steps = steps;
        _systemMessage = systemMessage;
        _chatModel = chatModel;
        _embedModel = embedModel;
        _memory = memory;
    }

    /// <summary>Set the system prompt from a string.</summary>
    public ChainBuilder System(string message) =>
        new(_steps, systemMessage: message, _chatModel, _embedModel, _memory);

    /// <summary>Set the system prompt from a Prompt instance.</summary>
    public ChainBuilder System(Koan.AI.Prompt.Prompt prompt) =>
        new(_steps, systemMessage: prompt.Raw, _chatModel, _embedModel, _memory);

    /// <summary>Load a named prompt from the prompt store.</summary>
    public ChainBuilder WithPrompt(string promptName) =>
        AddStep(new ChainStep(ChainStepKind.Prompt, promptName));

    /// <summary>Add a chat completion step with a template string.</summary>
    public ChainBuilder Chat(string template) =>
        AddStep(new ChainStep(ChainStepKind.Chat, template));

    /// <summary>
    /// Add a retrieval step (RAG). Pure-vector by default; pass <paramref name="alpha"/> to run
    /// hybrid (semantic+keyword) search with the query as the lexical side (AI-0036 R1).
    /// </summary>
    /// <param name="query">Natural-language query; embedded for vector search and used as the hybrid lexical side.</param>
    /// <param name="topK">Maximum number of results to retrieve.</param>
    /// <param name="alpha">Hybrid weight (0=keyword, 1=semantic). <c>null</c> = pure-vector.</param>
    /// <param name="rerank">When true, re-score the retrieved passages by relevance inline.</param>
    public ChainBuilder Retrieve<T>(string query, int topK = 5, double? alpha = null, bool rerank = false) =>
        AddStep(new ChainStep(ChainStepKind.Retrieve, query) { TopK = topK, Alpha = alpha, Rerank = rerank, EntityType = typeof(T) });

    /// <summary>Parse the chain output into a typed object.</summary>
    public ChainBuilder Parse<T>() =>
        AddStep(new ChainStep(ChainStepKind.Parse, typeof(T).Name) { EntityType = typeof(T) });

    /// <summary>Classify input text into one of the provided categories.</summary>
    public ChainBuilder Classify(string input, string[] categories) =>
        AddStep(new ChainStep(ChainStepKind.Classify, input) { Categories = categories });

    /// <summary>Add conditional branching based on chain state.</summary>
    public ChainBuilder Branch(params (string Condition, ChainBuilder Builder)[] branches) =>
        AddStep(new ChainStep(ChainStepKind.Branch, "branch") { Branches = branches });

    /// <summary>Run multiple chains in parallel and merge results.</summary>
    public ChainBuilder Parallel(params (string Name, ChainBuilder Builder)[] chains) =>
        AddStep(new ChainStep(ChainStepKind.Parallel, "parallel") { ParallelChains = chains });

    /// <summary>Re-rank retrieved results by relevance.</summary>
    public ChainBuilder Rerank() =>
        AddStep(new ChainStep(ChainStepKind.Rerank, "rerank"));

    /// <summary>Compress context to fit within token limits.</summary>
    public ChainBuilder Compress() =>
        AddStep(new ChainStep(ChainStepKind.Compress, "compress"));

    /// <summary>Run content moderation on the specified target.</summary>
    public ChainBuilder Moderate(string target = "{input}") =>
        AddStep(new ChainStep(ChainStepKind.Moderate, target));

    /// <summary>Attach tools for function calling within the chain.</summary>
    public ChainBuilder WithTools(params Tool[] tools) =>
        AddStep(new ChainStep(ChainStepKind.Tools, "tools") { Tools = tools });

    /// <summary>Attach conversational memory to the chain.</summary>
    public ChainBuilder WithMemory(ChainMemory memory) =>
        new(_steps, _systemMessage, _chatModel, _embedModel, memory);

    /// <summary>Scope the chain to specific AI providers (chat and/or embed).</summary>
    public ChainBuilder Scope(string? chat = null, string? embed = null) =>
        new(_steps, _systemMessage, chat ?? _chatModel, embed ?? _embedModel, _memory);

    /// <summary>Execute the chain and return the final result.</summary>
    public async Task<ChainResult> Run(object? variables = null, CancellationToken ct = default)
    {
        var executor = ResolveExecutor();
        return await executor.Execute(Build(), variables, ct);
    }

    /// <summary>Stream the chain output as chunks.</summary>
    public async IAsyncEnumerable<ChainChunk> Stream(
        object? variables = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var executor = ResolveExecutor();
        await foreach (var chunk in executor.Stream(Build(), variables, ct))
        {
            yield return chunk;
        }
    }

    // ── Internal ──

    private ChainBuilder AddStep(ChainStep step)
    {
        var newSteps = new List<ChainStep>(_steps) { step };
        return new ChainBuilder(newSteps.AsReadOnly(), _systemMessage, _chatModel, _embedModel, _memory);
    }

    internal ChainDefinition Build() =>
        new(_steps, _systemMessage, _chatModel, _embedModel, _memory);

    private static IChainExecutor ResolveExecutor()
    {
        var provider = Koan.Core.Hosting.App.AppHost.Current
            ?? throw new InvalidOperationException(
                "Chain executor not configured; call services.AddKoan() and ensure " +
                "AppHost.Current is set during startup before using Chain.*");

        return Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<IChainExecutor>(provider);
    }
}

// ── Internal types ──

public sealed record ChainDefinition(
    IReadOnlyList<ChainStep> Steps,
    string? SystemMessage,
    string? ChatModel,
    string? EmbedModel,
    ChainMemory? Memory);

public enum ChainStepKind
{
    Chat,
    Prompt,
    Retrieve,
    Parse,
    Classify,
    Branch,
    Parallel,
    Rerank,
    Compress,
    Moderate,
    Tools
}

public sealed record ChainStep(ChainStepKind Kind, string Value)
{
    public int TopK { get; init; }
    /// <summary>
    /// Hybrid semantic-vs-keyword weight (AI-0036 R1). <c>null</c> = pure-vector search (the default);
    /// when set, the retrieve runs hybrid with the query as the lexical side. Mirrors
    /// <c>Vector&lt;T&gt;.Search</c>'s own <c>double? alpha</c> — null means "no hybrid", not 0.
    /// </summary>
    public double? Alpha { get; init; }
    public bool Rerank { get; init; }
    public Type? EntityType { get; init; }
    public string[]? Categories { get; init; }
    public (string, ChainBuilder)[]? Branches { get; init; }
    public (string, ChainBuilder)[]? ParallelChains { get; init; }
    public Tool[]? Tools { get; init; }
}

/// <summary>
/// Internal interface for chain execution. Resolved via DI.
/// </summary>
public interface IChainExecutor
{
    Task<ChainResult> Execute(ChainDefinition definition, object? variables, CancellationToken ct);
    IAsyncEnumerable<ChainChunk> Stream(ChainDefinition definition, object? variables, CancellationToken ct);
}
