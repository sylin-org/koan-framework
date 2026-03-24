using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Koan.AI.Contracts.Options;

namespace Koan.AI.Orchestration;

/// <summary>
/// Default implementation of <see cref="IChainExecutor"/>.
/// Walks a <see cref="ChainDefinition"/> step-by-step, maintaining a variable context
/// that flows data between steps. Each step kind maps to a discrete operation
/// (chat completion, retrieval, parsing, classification, branching, etc.).
/// </summary>
internal sealed class ChainExecutor : IChainExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public async Task<ChainResult> Execute(
        ChainDefinition definition, object? variables, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var context = ChainContext.Create(definition, variables);

        foreach (var step in definition.Steps)
        {
            ct.ThrowIfCancellationRequested();
            await ExecuteStep(step, context, ct);
        }

        sw.Stop();

        return new ChainResult
        {
            Text = context.LastOutput ?? "",
            Citations = context.Citations.Count > 0 ? context.Citations : null,
            Metrics = new ChainMetrics(context.TotalTokens, sw.Elapsed, context.StepCount)
        };
    }

    public async IAsyncEnumerable<ChainChunk> Stream(
        ChainDefinition definition, object? variables,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var context = ChainContext.Create(definition, variables);
        var steps = definition.Steps;

        // Execute all non-final steps normally (they produce intermediate results)
        for (var i = 0; i < steps.Count - 1; i++)
        {
            ct.ThrowIfCancellationRequested();
            await ExecuteStep(steps[i], context, ct);
            yield return new ChainChunk { Step = steps[i].Kind.ToString() };
        }

        // Stream the final step if it is a Chat step
        if (steps.Count > 0)
        {
            var finalStep = steps[^1];

            if (finalStep.Kind == ChainStepKind.Chat)
            {
                yield return new ChainChunk { Step = "Chat" };

                var message = ResolveTemplate(finalStep.Value, context);
                var options = BuildChatOptions(context);

                var buffer = new StringBuilder();
                await foreach (var token in Koan.AI.Client.Stream(message, options, ct))
                {
                    buffer.Append(token);
                    yield return new ChainChunk { Text = token };
                }

                context.SetOutput(buffer.ToString());
            }
            else
            {
                // Non-chat final step: execute normally, emit result as single chunk
                await ExecuteStep(finalStep, context, ct);
                yield return new ChainChunk
                {
                    Step = finalStep.Kind.ToString(),
                    Text = context.LastOutput
                };
            }
        }
    }

    // ── Step dispatch ──

    private static async Task ExecuteStep(
        ChainStep step, ChainContext context, CancellationToken ct)
    {
        context.IncrementStep();

        switch (step.Kind)
        {
            case ChainStepKind.Chat:
                await ExecuteChat(step, context, ct);
                break;

            case ChainStepKind.Prompt:
                await ExecutePrompt(step, context, ct);
                break;

            case ChainStepKind.Retrieve:
                await ExecuteRetrieve(step, context, ct);
                break;

            case ChainStepKind.Parse:
                ExecuteParse(step, context);
                break;

            case ChainStepKind.Classify:
                await ExecuteClassify(step, context, ct);
                break;

            case ChainStepKind.Branch:
                await ExecuteBranch(step, context, ct);
                break;

            case ChainStepKind.Parallel:
                await ExecuteParallel(step, context, ct);
                break;

            case ChainStepKind.Rerank:
                await ExecuteRerank(step, context, ct);
                break;

            case ChainStepKind.Compress:
                await ExecuteCompress(step, context, ct);
                break;

            case ChainStepKind.Moderate:
                await ExecuteModerate(step, context, ct);
                break;

            case ChainStepKind.Tools:
                // Tools are attached to context for subsequent Chat steps
                if (step.Tools is { Length: > 0 })
                    context.SetTools(step.Tools);
                break;
        }
    }

    // ── Chat ──

    private static async Task ExecuteChat(
        ChainStep step, ChainContext context, CancellationToken ct)
    {
        var message = ResolveTemplate(step.Value, context);
        var options = BuildChatOptions(context);

        var result = await Koan.AI.Client.ChatResult(message, options, ct);

        context.SetOutput(result.Text);
        context.AddTokens(result.TokensUsed ?? 0);
    }

    // ── Prompt ──

    private static async Task ExecutePrompt(
        ChainStep step, ChainContext context, CancellationToken ct)
    {
        var prompt = await Koan.AI.Prompt.Prompt.Load(step.Value, ct);
        var resolved = prompt.Resolve(context.VariablesAsObject());

        // If the prompt has a system directive, use it
        if (prompt.System is not null)
            context.SetSystemMessage(prompt.System);

        // Store the resolved template for the next Chat step
        context.SetVariable("prompt", resolved);
    }

    // ── Retrieve ──

    private static async Task ExecuteRetrieve(
        ChainStep step, ChainContext context, CancellationToken ct)
    {
        if (step.EntityType is null)
            throw new InvalidOperationException("Retrieve step requires an entity type.");

        var queryText = ResolveTemplate(step.Value, context);

        // Generate embedding for the query
        var embedding = await Koan.AI.Client.Embed(queryText, ct);

        // Invoke Vector<T>.Search via reflection (entity type is runtime-determined)
        var results = await InvokeVectorSearch(
            step.EntityType, embedding, step.TopK, ct);

        // Store retrieved context and citations
        var contextParts = new List<string>();
        var citations = new List<Citation>();

        foreach (var (id, score, metadata) in results)
        {
            var excerpt = metadata?.ToString() ?? id;
            contextParts.Add(excerpt);
            citations.Add(new Citation(id, excerpt, score));
        }

        context.AddCitations(citations);
        context.SetVariable("retrieved", string.Join("\n\n---\n\n", contextParts));
        context.SetOutput(string.Join("\n\n---\n\n", contextParts));
    }

    // ── Parse ──

    private static void ExecuteParse(ChainStep step, ChainContext context)
    {
        if (step.EntityType is null)
            throw new InvalidOperationException("Parse step requires a target type.");

        var text = context.LastOutput
            ?? throw new InvalidOperationException("Parse step has no input — no prior Chat output.");

        try
        {
            var parsed = JsonSerializer.Deserialize(text, step.EntityType, JsonOptions);
            context.SetOutput(JsonSerializer.Serialize(parsed, JsonOptions));
            context.SetVariable("parsed", context.LastOutput!);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Failed to parse Chat output as {step.EntityType.Name}: {ex.Message}", ex);
        }
    }

    // ── Classify ──

    private static async Task ExecuteClassify(
        ChainStep step, ChainContext context, CancellationToken ct)
    {
        var categories = step.Categories
            ?? throw new InvalidOperationException("Classify step requires categories.");

        var input = ResolveTemplate(step.Value, context);
        var categoryList = string.Join(", ", categories);

        var classificationPrompt =
            $"Classify the following text into exactly one of these categories: [{categoryList}].\n\n" +
            $"Text: {input}\n\n" +
            "Respond with only the category name, nothing else.";

        var options = new ChatOptions
        {
            SystemPrompt = "You are a precise text classifier. Respond with only the category name.",
            Temperature = 0.0
        };

        var result = await Koan.AI.Client.Chat(classificationPrompt, options, ct);
        var category = result.Trim();

        context.SetVariable("classification", category);
        context.SetOutput(category);
    }

    // ── Branch ──

    private static async Task ExecuteBranch(
        ChainStep step, ChainContext context, CancellationToken ct)
    {
        var branches = step.Branches
            ?? throw new InvalidOperationException("Branch step requires branch definitions.");

        var classification = context.GetVariable("classification")
            ?? context.LastOutput
            ?? throw new InvalidOperationException("Branch step has no classification result to branch on.");

        // Find matching branch (case-insensitive) or default (wildcard "*")
        ChainBuilder? matchedBuilder = null;
        ChainBuilder? defaultBuilder = null;

        foreach (var (condition, builder) in branches)
        {
            if (condition == "*")
            {
                defaultBuilder = builder;
                continue;
            }

            if (string.Equals(condition, classification, StringComparison.OrdinalIgnoreCase))
            {
                matchedBuilder = builder;
                break;
            }
        }

        var selectedBuilder = matchedBuilder ?? defaultBuilder;
        if (selectedBuilder is null)
            throw new InvalidOperationException(
                $"No branch matched classification '{classification}' and no default ('*') branch defined.");

        var subDefinition = selectedBuilder.Build();
        var executor = new ChainExecutor();
        var subResult = await executor.Execute(subDefinition, context.VariablesAsObject(), ct);

        context.SetOutput(subResult.Text);
        context.AddTokens(subResult.Metrics.TotalTokens);
    }

    // ── Parallel ──

    private static async Task ExecuteParallel(
        ChainStep step, ChainContext context, CancellationToken ct)
    {
        var chains = step.ParallelChains
            ?? throw new InvalidOperationException("Parallel step requires chain definitions.");

        var tasks = new List<(string Name, Task<ChainResult> Task)>();
        var executor = new ChainExecutor();

        foreach (var (name, builder) in chains)
        {
            var subDefinition = builder.Build();
            tasks.Add((name, executor.Execute(subDefinition, context.VariablesAsObject(), ct)));
        }

        await Task.WhenAll(tasks.Select(t => t.Task));

        var mergedParts = new List<string>();
        foreach (var (name, task) in tasks)
        {
            var result = await task;
            context.SetVariable(name, result.Text);
            context.AddTokens(result.Metrics.TotalTokens);
            mergedParts.Add($"[{name}]\n{result.Text}");
        }

        context.SetOutput(string.Join("\n\n", mergedParts));
    }

    // ── Rerank ──

    private static async Task ExecuteRerank(
        ChainStep step, ChainContext context, CancellationToken ct)
    {
        // Rerank uses the AI model to re-score retrieved results
        var retrieved = context.GetVariable("retrieved");
        if (string.IsNullOrEmpty(retrieved))
            return; // Nothing to rerank

        var query = context.GetVariable("input") ?? context.LastOutput ?? "";

        var rerankPrompt =
            $"Given the query: \"{query}\"\n\n" +
            $"Rerank the following passages by relevance (most relevant first). " +
            $"Return only the passages in order, separated by '---'.\n\n{retrieved}";

        var options = new ChatOptions
        {
            SystemPrompt = "You are a passage relevance ranker. Reorder passages by relevance to the query.",
            Temperature = 0.0
        };

        var result = await Koan.AI.Client.Chat(rerankPrompt, options, ct);

        context.SetVariable("retrieved", result);
        context.SetOutput(result);
    }

    // ── Compress ──

    private static async Task ExecuteCompress(
        ChainStep step, ChainContext context, CancellationToken ct)
    {
        var text = context.LastOutput;
        if (string.IsNullOrEmpty(text))
            return;

        var compressPrompt =
            "Compress the following text to its essential information, removing redundancy " +
            "while preserving all key facts and details:\n\n" + text;

        var options = new ChatOptions
        {
            SystemPrompt = "You are a text compressor. Preserve all key information in fewer words.",
            Temperature = 0.0
        };

        var result = await Koan.AI.Client.Chat(compressPrompt, options, ct);
        context.SetOutput(result);
    }

    // ── Moderate ──

    private static async Task ExecuteModerate(
        ChainStep step, ChainContext context, CancellationToken ct)
    {
        var target = ResolveTemplate(step.Value, context);

        var moderationPrompt =
            "Analyze the following text for safety concerns. " +
            "Respond with a JSON object: {\"safe\": true/false, \"categories\": [...], \"reason\": \"...\"}.\n\n" +
            $"Text: {target}";

        var options = new ChatOptions
        {
            SystemPrompt = "You are a content moderator. Analyze text for safety.",
            Temperature = 0.0,
            ResponseFormat = "json_object"
        };

        var result = await Koan.AI.Client.Chat(moderationPrompt, options, ct);

        context.SetVariable("moderation", result);
        context.SetOutput(result);
    }

    // ── Helpers ──

    private static string ResolveTemplate(string template, ChainContext context)
    {
        var result = template;
        foreach (var (key, value) in context.Variables)
        {
            result = result.Replace($"{{{key}}}", value, StringComparison.OrdinalIgnoreCase);
        }
        return result;
    }

    private static ChatOptions BuildChatOptions(ChainContext context)
    {
        return new ChatOptions
        {
            SystemPrompt = context.SystemMessage,
            Model = context.ChatModel,
            Source = context.ChatModel  // Scope routing
        };
    }

    /// <summary>
    /// Invokes Vector&lt;T&gt;.Search via reflection since the entity type is known only at runtime.
    /// Returns (Id, Score, Metadata) tuples.
    /// </summary>
    private static async Task<List<(string Id, double Score, object? Metadata)>> InvokeVectorSearch(
        Type entityType, float[] embedding, int topK, CancellationToken ct)
    {
        var results = new List<(string Id, double Score, object? Metadata)>();

        try
        {
            // Build Vector<T> where T = entityType
            var vectorType = typeof(Koan.Data.Vector.Vector<>).MakeGenericType(entityType);

            // Check IsAvailable
            var isAvailableProp = vectorType.GetProperty("IsAvailable")
                ?? throw new InvalidOperationException($"Vector<{entityType.Name}>.IsAvailable not found.");

            var isAvailable = (bool)(isAvailableProp.GetValue(null) ?? false);
            if (!isAvailable)
            {
                // Vector search not configured for this entity — return empty
                return results;
            }

            // Call Search(float[], text: null, alpha: null, topK: topK, ...)
            var searchMethod = vectorType.GetMethod("Search", [
                typeof(float[]),
                typeof(string),
                typeof(double?),
                typeof(int?),
                typeof(object),
                typeof(string),
                typeof(string),
                typeof(CancellationToken)
            ]);

            if (searchMethod is null)
                return results;

            var task = searchMethod.Invoke(null, [embedding, null, null, topK, null, null, null, ct]);
            if (task is null)
                return results;

            // Await the Task<VectorQueryResult<string>>
            await (Task)task;

            // Get Result property
            var resultProp = task.GetType().GetProperty("Result");
            var queryResult = resultProp?.GetValue(task);
            if (queryResult is null)
                return results;

            // Get Matches property
            var matchesProp = queryResult.GetType().GetProperty("Matches");
            var matches = matchesProp?.GetValue(queryResult) as System.Collections.IEnumerable;
            if (matches is null)
                return results;

            foreach (var match in matches)
            {
                var matchType = match.GetType();
                var id = matchType.GetProperty("Id")?.GetValue(match)?.ToString() ?? "";
                var score = (double)(matchType.GetProperty("Score")?.GetValue(match) ?? 0.0);
                var metadata = matchType.GetProperty("Metadata")?.GetValue(match);
                results.Add((id, score, metadata));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Retrieval failure is non-fatal — chain continues with empty retrieval
            results.Clear();
        }

        return results;
    }
}

/// <summary>
/// Mutable execution context that flows through chain steps.
/// Encapsulates variables, system message, citations, and metrics.
/// </summary>
internal sealed class ChainContext
{
    private readonly Dictionary<string, string> _variables = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Citation> _citations = [];
    private Tool[]? _tools;
    private int _stepCount;
    private int _totalTokens;

    public string? SystemMessage { get; private set; }
    public string? ChatModel { get; private set; }
    public string? EmbedModel { get; private set; }
    public string? LastOutput { get; private set; }
    public IReadOnlyDictionary<string, string> Variables => _variables;
    public IReadOnlyList<Citation> Citations => _citations;
    public int StepCount => _stepCount;
    public int TotalTokens => _totalTokens;

    public static ChainContext Create(ChainDefinition definition, object? variables)
    {
        var ctx = new ChainContext
        {
            SystemMessage = definition.SystemMessage,
            ChatModel = definition.ChatModel,
            EmbedModel = definition.EmbedModel
        };

        // Seed variables from caller-provided object
        if (variables is not null)
        {
            foreach (var prop in variables.GetType().GetProperties())
            {
                if (prop.CanRead)
                {
                    var value = prop.GetValue(variables)?.ToString() ?? "";
                    ctx._variables[prop.Name] = value;
                }
            }
        }

        return ctx;
    }

    public void SetOutput(string output)
    {
        LastOutput = output;
        _variables["output"] = output;
    }

    public void SetVariable(string key, string value)
    {
        _variables[key] = value;
    }

    public string? GetVariable(string key)
    {
        return _variables.TryGetValue(key, out var value) ? value : null;
    }

    public void SetSystemMessage(string message)
    {
        SystemMessage = message;
    }

    public void SetTools(Tool[] tools)
    {
        _tools = tools;
    }

    public void AddCitations(IEnumerable<Citation> citations)
    {
        _citations.AddRange(citations);
    }

    public void AddTokens(int tokens)
    {
        _totalTokens += tokens;
    }

    public void IncrementStep()
    {
        _stepCount++;
    }

    /// <summary>
    /// Returns the variable dictionary as an anonymous-like object for Prompt.Resolve().
    /// Since Prompt.Resolve accepts IDictionary&lt;string, string&gt;, we return that directly.
    /// </summary>
    public IDictionary<string, string> VariablesAsObject()
    {
        return new Dictionary<string, string>(_variables, StringComparer.OrdinalIgnoreCase);
    }
}
