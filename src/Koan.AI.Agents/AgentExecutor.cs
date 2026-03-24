using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Koan.AI.Contracts.Models;
using Koan.AI.Contracts.Options;
using Koan.AI.Orchestration;

namespace Koan.AI.Agents;

/// <summary>
/// Default implementation of <see cref="IAgentExecutor"/>.
/// Implements the ReAct (Reason-Act-Observe) loop: the model reasons about the goal,
/// calls tools when needed, observes results, and repeats until it produces a final answer
/// or hits iteration/token limits.
/// </summary>
internal sealed class AgentExecutor : IAgentExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    public async Task<AgentResult> Execute(
        AgentDefinition definition, string goal, object? context, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var toolRegistry = BuildToolRegistry(definition);
        var messages = BuildInitialMessages(definition, goal, context, toolRegistry);
        var steps = new List<AgentStep>();
        var totalTokens = 0;
        var iterations = 0;

        while (iterations < definition.MaxIterations)
        {
            ct.ThrowIfCancellationRequested();
            iterations++;

            if (totalTokens >= definition.MaxTokens)
            {
                sw.Stop();
                return new AgentResult
                {
                    Text = ExtractLastAssistantText(messages),
                    Status = AgentStatus.BudgetExhausted,
                    Steps = steps,
                    Iterations = iterations,
                    TotalTokens = totalTokens,
                    Duration = sw.Elapsed
                };
            }

            var options = BuildChatOptions(definition, toolRegistry);
            var chatResult = await Koan.AI.Client.ChatResult(
                BuildUserMessage(messages), ct);

            var tokensUsed = chatResult.TokensUsed ?? 0;
            totalTokens += tokensUsed;

            var responseText = chatResult.Text;

            // Try to parse tool calls from the response
            var toolCalls = ParseToolCalls(responseText);

            if (toolCalls.Count == 0)
            {
                // No tool calls — this is the final answer
                steps.Add(new AgentStep
                {
                    Reasoning = responseText,
                    TokensUsed = tokensUsed
                });

                messages.Add(new AiMessage("assistant", responseText));

                sw.Stop();
                return new AgentResult
                {
                    Text = responseText,
                    Status = AgentStatus.Completed,
                    Steps = steps,
                    Iterations = iterations,
                    TotalTokens = totalTokens,
                    Duration = sw.Elapsed
                };
            }

            // Execute each tool call
            messages.Add(new AiMessage("assistant", responseText));

            foreach (var toolCall in toolCalls)
            {
                var observation = await ExecuteTool(toolCall, toolRegistry, ct);

                // Truncate observation if it exceeds the limit
                var truncatedObservation = TruncateToTokenLimit(
                    observation, definition.MaxToolResultTokens);

                steps.Add(new AgentStep
                {
                    Reasoning = ExtractReasoning(responseText, toolCall),
                    ToolCall = toolCall,
                    Observation = truncatedObservation,
                    TokensUsed = tokensUsed / Math.Max(toolCalls.Count, 1)
                });

                // Add observation as a tool/user message for the next iteration
                messages.Add(new AiMessage("user",
                    $"Tool '{toolCall.Name}' returned:\n{truncatedObservation}")
                {
                    Name = toolCall.Name
                });
            }
        }

        // Iteration limit reached
        sw.Stop();
        return new AgentResult
        {
            Text = ExtractLastAssistantText(messages),
            Status = AgentStatus.IterationLimitReached,
            Steps = steps,
            Iterations = iterations,
            TotalTokens = totalTokens,
            Duration = sw.Elapsed
        };
    }

    public async IAsyncEnumerable<AgentStep> Stream(
        AgentDefinition definition, string goal,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var toolRegistry = BuildToolRegistry(definition);
        var messages = BuildInitialMessages(definition, goal, context: null, toolRegistry);
        var totalTokens = 0;
        var iterations = 0;

        while (iterations < definition.MaxIterations)
        {
            ct.ThrowIfCancellationRequested();
            iterations++;

            if (totalTokens >= definition.MaxTokens)
                yield break;

            var chatResult = await Koan.AI.Client.ChatResult(
                BuildUserMessage(messages), ct);

            var tokensUsed = chatResult.TokensUsed ?? 0;
            totalTokens += tokensUsed;

            var responseText = chatResult.Text;
            var toolCalls = ParseToolCalls(responseText);

            if (toolCalls.Count == 0)
            {
                yield return new AgentStep
                {
                    Reasoning = responseText,
                    TokensUsed = tokensUsed
                };
                yield break;
            }

            messages.Add(new AiMessage("assistant", responseText));

            foreach (var toolCall in toolCalls)
            {
                var observation = await ExecuteTool(toolCall, toolRegistry, ct);
                var truncatedObservation = TruncateToTokenLimit(
                    observation, definition.MaxToolResultTokens);

                yield return new AgentStep
                {
                    Reasoning = ExtractReasoning(responseText, toolCall),
                    ToolCall = toolCall,
                    Observation = truncatedObservation,
                    TokensUsed = tokensUsed / Math.Max(toolCalls.Count, 1)
                };

                messages.Add(new AiMessage("user",
                    $"Tool '{toolCall.Name}' returned:\n{truncatedObservation}")
                {
                    Name = toolCall.Name
                });
            }
        }
    }

    // ── Tool registry ──

    private static Dictionary<string, GeneratedTool> BuildToolRegistry(AgentDefinition definition)
    {
        var registry = new Dictionary<string, GeneratedTool>(StringComparer.OrdinalIgnoreCase);

        // Generate entity tools
        foreach (var binding in definition.Entities)
        {
            var tools = EntityToolGenerator.Generate(binding);
            foreach (var tool in tools)
            {
                registry[tool.Name] = tool;
            }
        }

        // Generate search tools
        foreach (var searchType in definition.SearchEntities)
        {
            var tool = EntityToolGenerator.GenerateSearchTool(searchType);
            registry[tool.Name] = tool;
        }

        return registry;
    }

    // ── Message construction ──

    private static List<AiMessage> BuildInitialMessages(
        AgentDefinition definition, string goal, object? context,
        Dictionary<string, GeneratedTool> toolRegistry)
    {
        var messages = new List<AiMessage>();

        // Build the system prompt with tool descriptions
        var systemPrompt = BuildSystemPrompt(definition, toolRegistry);
        messages.Add(new AiMessage("system", systemPrompt));

        // Build the user goal message
        var goalMessage = new StringBuilder();
        goalMessage.AppendLine(goal);

        if (context is not null)
        {
            try
            {
                var contextJson = JsonSerializer.Serialize(context, JsonOptions);
                goalMessage.AppendLine();
                goalMessage.AppendLine("Context:");
                goalMessage.AppendLine(contextJson);
            }
            catch
            {
                goalMessage.AppendLine();
                goalMessage.AppendLine($"Context: {context}");
            }
        }

        messages.Add(new AiMessage("user", goalMessage.ToString()));

        return messages;
    }

    private static string BuildSystemPrompt(
        AgentDefinition definition, Dictionary<string, GeneratedTool> toolRegistry)
    {
        var sb = new StringBuilder();

        // User-provided system message
        if (!string.IsNullOrWhiteSpace(definition.SystemMessage))
        {
            sb.AppendLine(definition.SystemMessage);
            sb.AppendLine();
        }

        // ReAct instructions
        if (definition.Strategy == PlanStrategy.ReAct)
        {
            sb.AppendLine("You are an autonomous agent that reasons step-by-step to achieve goals.");
            sb.AppendLine("Follow this loop: Think -> Act -> Observe -> Repeat until done.");
            sb.AppendLine();
        }
        else if (definition.Strategy == PlanStrategy.PlanAndExecute)
        {
            sb.AppendLine("You are an autonomous agent. First, create a numbered plan. Then execute each step.");
            sb.AppendLine();
        }

        // Tool descriptions
        if (toolRegistry.Count > 0)
        {
            sb.AppendLine("## Available Tools");
            sb.AppendLine();
            sb.AppendLine("To call a tool, respond with a JSON block in this exact format:");
            sb.AppendLine("```tool_call");
            sb.AppendLine("{\"name\": \"tool_name\", \"arguments\": {\"param\": \"value\"}}");
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("You may include reasoning text before the tool call block.");
            sb.AppendLine("When you have gathered enough information, provide your final answer as plain text (no tool_call block).");
            sb.AppendLine();

            foreach (var (name, tool) in toolRegistry)
            {
                sb.AppendLine($"### {name}");
                sb.AppendLine(tool.Description);
                sb.AppendLine($"Parameters: {tool.ParametersSchema}");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private static ChatOptions BuildChatOptions(
        AgentDefinition definition, Dictionary<string, GeneratedTool> toolRegistry)
    {
        return new ChatOptions
        {
            Model = definition.ChatModel,
            Source = definition.ChatModel,
            Temperature = 0.1  // Low temperature for reasoning
        };
    }

    /// <summary>
    /// Builds the message content from the full conversation for passing to Client.ChatResult.
    /// Concatenates messages into a single prompt since Client.ChatResult takes a single string.
    /// </summary>
    private static string BuildUserMessage(List<AiMessage> messages)
    {
        // Find the system message and build a ChatOptions, then build the user portion
        var sb = new StringBuilder();

        foreach (var msg in messages)
        {
            if (msg.Role == "system") continue; // handled via ChatOptions
            sb.AppendLine($"[{msg.Role}]: {msg.Content}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    // ── Tool call parsing ──

    /// <summary>
    /// Parses tool calls from the model response.
    /// Looks for ```tool_call JSON blocks.
    /// </summary>
    private static List<ToolCall> ParseToolCalls(string response)
    {
        var calls = new List<ToolCall>();

        // Look for ```tool_call blocks
        var searchText = response;
        const string startMarker = "```tool_call";
        const string endMarker = "```";

        var startIdx = searchText.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase);
        while (startIdx >= 0)
        {
            var jsonStart = startIdx + startMarker.Length;
            var endIdx = searchText.IndexOf(endMarker, jsonStart, StringComparison.Ordinal);
            if (endIdx < 0) break;

            var jsonText = searchText[jsonStart..endIdx].Trim();
            var toolCall = TryParseToolCall(jsonText);
            if (toolCall is not null)
                calls.Add(toolCall);

            startIdx = searchText.IndexOf(startMarker, endIdx + endMarker.Length, StringComparison.OrdinalIgnoreCase);
        }

        // Fallback: look for {"name": "...", "arguments": {...}} pattern without code fence
        if (calls.Count == 0)
        {
            var toolCall = TryParseToolCall(response);
            if (toolCall is not null)
                calls.Add(toolCall);
        }

        return calls;
    }

    private static ToolCall? TryParseToolCall(string text)
    {
        try
        {
            // Try to find a JSON object in the text
            var jsonStart = text.IndexOf('{');
            var jsonEnd = text.LastIndexOf('}');
            if (jsonStart < 0 || jsonEnd < 0 || jsonEnd <= jsonStart)
                return null;

            var json = text[jsonStart..(jsonEnd + 1)];
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("name", out var nameProp))
                return null;

            var name = nameProp.GetString();
            if (string.IsNullOrEmpty(name))
                return null;

            var arguments = new Dictionary<string, object?>();
            if (root.TryGetProperty("arguments", out var argsProp) && argsProp.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in argsProp.EnumerateObject())
                {
                    arguments[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString(),
                        JsonValueKind.Number => prop.Value.Clone(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.Null => null,
                        _ => prop.Value.Clone()
                    };
                }
            }

            return new ToolCall(name, arguments);
        }
        catch
        {
            return null;
        }
    }

    // ── Tool execution ──

    private static async Task<string> ExecuteTool(
        ToolCall toolCall, Dictionary<string, GeneratedTool> registry, CancellationToken ct)
    {
        if (!registry.TryGetValue(toolCall.Name, out var tool))
            return JsonSerializer.Serialize(new { error = $"Unknown tool: {toolCall.Name}" });

        try
        {
            return await tool.Execute(toolCall.Arguments, ct);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"Tool execution failed: {ex.Message}" });
        }
    }

    // ── Helpers ──

    private static string ExtractReasoning(string fullResponse, ToolCall toolCall)
    {
        // Extract text before the tool call block
        var toolCallIdx = fullResponse.IndexOf("```tool_call", StringComparison.OrdinalIgnoreCase);
        if (toolCallIdx > 0)
            return fullResponse[..toolCallIdx].Trim();

        // Fallback: look for JSON start
        var jsonIdx = fullResponse.IndexOf("{\"name\":", StringComparison.Ordinal);
        if (jsonIdx > 0)
            return fullResponse[..jsonIdx].Trim();

        return fullResponse;
    }

    private static string ExtractLastAssistantText(List<AiMessage> messages)
    {
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            if (messages[i].Role == "assistant")
                return messages[i].Content;
        }
        return "";
    }

    /// <summary>
    /// Rough truncation to approximate token limit (4 chars ~ 1 token).
    /// </summary>
    private static string TruncateToTokenLimit(string text, int maxTokens)
    {
        var maxChars = maxTokens * 4;
        if (text.Length <= maxChars)
            return text;

        return text[..maxChars] + "\n... [truncated]";
    }
}
