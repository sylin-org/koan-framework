using System.Runtime.CompilerServices;
using Koan.AI.Orchestration;

namespace Koan.AI.Agents;

/// <summary>
/// Fluent, immutable builder for autonomous AI agents.
/// Each method returns a new builder instance — the original is never mutated.
/// </summary>
public sealed class AgentBuilder
{
    private readonly IReadOnlyList<EntityBinding> _entities;
    private readonly IReadOnlyList<Type> _searchEntities;
    private readonly IReadOnlyList<Tool> _tools;
    private readonly string? _systemMessage;
    private readonly string? _chatModel;
    private readonly string? _embedModel;
    private readonly AgentMemory? _memory;
    private readonly PlanStrategy _strategy;
    private readonly int _maxIterations;
    private readonly int _maxTokens;
    private readonly int _maxToolResultTokens;

    internal AgentBuilder()
        : this([], [], [], null, null, null, null, PlanStrategy.ReAct, 10, 100_000, 4_000) { }

    private AgentBuilder(
        IReadOnlyList<EntityBinding> entities,
        IReadOnlyList<Type> searchEntities,
        IReadOnlyList<Tool> tools,
        string? systemMessage,
        string? chatModel,
        string? embedModel,
        AgentMemory? memory,
        PlanStrategy strategy,
        int maxIterations,
        int maxTokens,
        int maxToolResultTokens)
    {
        _entities = entities;
        _searchEntities = searchEntities;
        _tools = tools;
        _systemMessage = systemMessage;
        _chatModel = chatModel;
        _embedModel = embedModel;
        _memory = memory;
        _strategy = strategy;
        _maxIterations = maxIterations;
        _maxTokens = maxTokens;
        _maxToolResultTokens = maxToolResultTokens;
    }

    /// <summary>Set the system prompt from a string.</summary>
    public AgentBuilder System(string message) =>
        new(_entities, _searchEntities, _tools, message, _chatModel, _embedModel, _memory, _strategy, _maxIterations, _maxTokens, _maxToolResultTokens);

    /// <summary>Set the system prompt from a Prompt instance.</summary>
    public AgentBuilder System(Koan.AI.Prompt.Prompt prompt) =>
        new(_entities, _searchEntities, _tools, prompt.Raw, _chatModel, _embedModel, _memory, _strategy, _maxIterations, _maxTokens, _maxToolResultTokens);

    /// <summary>Load a named prompt from the prompt store.</summary>
    public AgentBuilder WithPrompt(string promptName) =>
        new(_entities, _searchEntities, _tools, promptName, _chatModel, _embedModel, _memory, _strategy, _maxIterations, _maxTokens, _maxToolResultTokens);

    /// <summary>Grant the agent access to entity CRUD operations.</summary>
    public AgentBuilder WithEntities<T>(bool write = false) =>
        AddEntity(new EntityBinding(typeof(T), write));

    /// <summary>Grant the agent access to two entity types.</summary>
    public AgentBuilder WithEntities<T1, T2>(bool write = false) =>
        AddEntity(new EntityBinding(typeof(T1), write))
            .AddEntity(new EntityBinding(typeof(T2), write));

    /// <summary>Grant the agent access to three entity types.</summary>
    public AgentBuilder WithEntities<T1, T2, T3>(bool write = false) =>
        AddEntity(new EntityBinding(typeof(T1), write))
            .AddEntity(new EntityBinding(typeof(T2), write))
            .AddEntity(new EntityBinding(typeof(T3), write));

    /// <summary>Grant the agent access to four entity types.</summary>
    public AgentBuilder WithEntities<T1, T2, T3, T4>(bool write = false) =>
        AddEntity(new EntityBinding(typeof(T1), write))
            .AddEntity(new EntityBinding(typeof(T2), write))
            .AddEntity(new EntityBinding(typeof(T3), write))
            .AddEntity(new EntityBinding(typeof(T4), write));

    /// <summary>Grant the agent vector search capability over an entity type.</summary>
    public AgentBuilder WithSearch<T>() =>
        new(_entities, new List<Type>(_searchEntities) { typeof(T) }.AsReadOnly(), _tools, _systemMessage, _chatModel, _embedModel, _memory, _strategy, _maxIterations, _maxTokens, _maxToolResultTokens);

    /// <summary>Attach additional tools for function calling.</summary>
    public AgentBuilder WithTools(params Tool[] tools) =>
        new(_entities, _searchEntities, new List<Tool>([.. _tools, .. tools]).AsReadOnly(), _systemMessage, _chatModel, _embedModel, _memory, _strategy, _maxIterations, _maxTokens, _maxToolResultTokens);

    /// <summary>Attach conversational memory to the agent.</summary>
    public AgentBuilder WithMemory(AgentMemory memory) =>
        new(_entities, _searchEntities, _tools, _systemMessage, _chatModel, _embedModel, memory, _strategy, _maxIterations, _maxTokens, _maxToolResultTokens);

    /// <summary>Set the planning/reasoning strategy.</summary>
    public AgentBuilder WithPlanning(PlanStrategy strategy = PlanStrategy.ReAct) =>
        new(_entities, _searchEntities, _tools, _systemMessage, _chatModel, _embedModel, _memory, strategy, _maxIterations, _maxTokens, _maxToolResultTokens);

    /// <summary>Set the maximum number of reasoning iterations.</summary>
    public AgentBuilder WithMaxIterations(int maxIterations = 10) =>
        new(_entities, _searchEntities, _tools, _systemMessage, _chatModel, _embedModel, _memory, _strategy, maxIterations, _maxTokens, _maxToolResultTokens);

    /// <summary>Set the maximum total tokens budget.</summary>
    public AgentBuilder WithMaxTokens(int maxTokens = 100_000) =>
        new(_entities, _searchEntities, _tools, _systemMessage, _chatModel, _embedModel, _memory, _strategy, _maxIterations, maxTokens, _maxToolResultTokens);

    /// <summary>Set the maximum tokens per tool result.</summary>
    public AgentBuilder WithMaxToolResultTokens(int maxToolResultTokens = 4_000) =>
        new(_entities, _searchEntities, _tools, _systemMessage, _chatModel, _embedModel, _memory, _strategy, _maxIterations, _maxTokens, maxToolResultTokens);

    /// <summary>Scope the agent to specific AI providers.</summary>
    public AgentBuilder Scope(string? chat = null, string? embed = null) =>
        new(_entities, _searchEntities, _tools, _systemMessage, chat ?? _chatModel, embed ?? _embedModel, _memory, _strategy, _maxIterations, _maxTokens, _maxToolResultTokens);

    /// <summary>Execute the agent with a goal and optional context.</summary>
    public async Task<AgentResult> Run(
        string goal,
        object? context = null,
        CancellationToken ct = default)
    {
        var executor = ResolveExecutor();
        return await executor.ExecuteAsync(Build(), goal, context, ct);
    }

    /// <summary>Stream the agent's reasoning steps as they occur.</summary>
    public async IAsyncEnumerable<AgentStep> Stream(
        string goal,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var executor = ResolveExecutor();
        await foreach (var step in executor.StreamAsync(Build(), goal, ct))
        {
            yield return step;
        }
    }

    // ── Internal ──

    private AgentBuilder AddEntity(EntityBinding binding)
    {
        var newEntities = new List<EntityBinding>(_entities) { binding };
        return new AgentBuilder(newEntities.AsReadOnly(), _searchEntities, _tools, _systemMessage, _chatModel, _embedModel, _memory, _strategy, _maxIterations, _maxTokens, _maxToolResultTokens);
    }

    internal AgentDefinition Build() =>
        new(_entities, _searchEntities, _tools, _systemMessage, _chatModel, _embedModel, _memory, _strategy, _maxIterations, _maxTokens, _maxToolResultTokens);

    private static IAgentExecutor ResolveExecutor()
    {
        var provider = Koan.Core.Hosting.App.AppHost.Current
            ?? throw new InvalidOperationException(
                "Agent executor not configured; call services.AddKoan() and ensure " +
                "AppHost.Current is set during startup before using Agent.*");

        return Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<IAgentExecutor>(provider);
    }
}

// ── Internal types ──

public sealed record EntityBinding(Type EntityType, bool AllowWrite);

public sealed record AgentDefinition(
    IReadOnlyList<EntityBinding> Entities,
    IReadOnlyList<Type> SearchEntities,
    IReadOnlyList<Tool> Tools,
    string? SystemMessage,
    string? ChatModel,
    string? EmbedModel,
    AgentMemory? Memory,
    PlanStrategy Strategy,
    int MaxIterations,
    int MaxTokens,
    int MaxToolResultTokens);

/// <summary>
/// Internal interface for agent execution. Resolved via DI.
/// </summary>
public interface IAgentExecutor
{
    Task<AgentResult> ExecuteAsync(AgentDefinition definition, string goal, object? context, CancellationToken ct);
    IAsyncEnumerable<AgentStep> StreamAsync(AgentDefinition definition, string goal, CancellationToken ct);
}
