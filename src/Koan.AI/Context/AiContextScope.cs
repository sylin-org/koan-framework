using System;
using System.Collections.Immutable;
using System.Threading;

namespace Koan.AI.Context;

/// <summary>
/// Scoped context for AI operations, providing source/provider/model overrides.
/// Uses AsyncLocal for ambient context that flows across async boundaries.
///
/// Example usage:
/// <code>
/// // Default behavior (uses routing configuration)
/// var response = await Ai.Chat("Hello");
///
/// // Override source for specific operations
/// using (Ai.Context(source: "ollama-primary")) {
///     var response = await Ai.Chat("Process locally");
/// }
///
/// // Nested contexts (inner overrides outer)
/// using (Ai.Context(source: "production-ollama")) {
///     var chat = await Ai.Chat("Question 1");
///
///     using (Ai.Context(source: "cloud-services")) {
///         var vision = await Ai.Understand(imageBytes, "What's this?");
///     }
/// }
/// </code>
/// </summary>
public sealed class AiContextScope : IDisposable
{
    private static readonly AsyncLocal<ImmutableStack<AiContextScope>> _contextStack = new();

    private readonly string? _source;
    private readonly string? _provider;
    private readonly string? _model;
    private readonly ImmutableStack<AiContextScope> _previousStack;
    private bool _disposed;

    /// <summary>
    /// Source or group name override. Examples: "ollama-primary", "production-ollama"
    /// </summary>
    public string? Source => _source;

    /// <summary>
    /// Provider type override. Examples: "ollama", "openai", "anthropic"
    /// </summary>
    public string? Provider => _provider;

    /// <summary>
    /// Model name override. Examples: "llama3.2:70b", "gpt-4o"
    /// </summary>
    public string? Model => _model;

    internal AiContextScope(string? source, string? provider, string? model)
    {
        _source = source;
        _provider = provider;
        _model = model;

        // Push onto async-local stack
        _previousStack = _contextStack.Value ?? ImmutableStack<AiContextScope>.Empty;
        _contextStack.Value = _previousStack.Push(this);
    }

    /// <summary>
    /// Get the current ambient context (top of stack), or null if none
    /// </summary>
    public static AiContextScope? Current
    {
        get
        {
            var stack = _contextStack.Value;
            return stack != null && !stack.IsEmpty ? stack.Peek() : null;
        }
    }

    /// <summary>
    /// Resolve effective source name from context stack.
    /// Walks up the stack to find first non-null source.
    /// </summary>
    public static string? ResolveSource()
    {
        var stack = _contextStack.Value;
        if (stack == null || stack.IsEmpty) return null;

        foreach (var ctx in stack)
        {
            if (!string.IsNullOrWhiteSpace(ctx.Source))
                return ctx.Source;
        }

        return null;
    }

    /// <summary>
    /// Resolve effective provider from context stack.
    /// Walks up the stack to find first non-null provider.
    /// </summary>
    public static string? ResolveProvider()
    {
        var stack = _contextStack.Value;
        if (stack == null || stack.IsEmpty) return null;

        foreach (var ctx in stack)
        {
            if (!string.IsNullOrWhiteSpace(ctx.Provider))
                return ctx.Provider;
        }

        return null;
    }

    /// <summary>
    /// Resolve effective model from context stack.
    /// Walks up the stack to find first non-null model.
    /// </summary>
    public static string? ResolveModel()
    {
        var stack = _contextStack.Value;
        if (stack == null || stack.IsEmpty) return null;

        foreach (var ctx in stack)
        {
            if (!string.IsNullOrWhiteSpace(ctx.Model))
                return ctx.Model;
        }

        return null;
    }

    /// <summary>
    /// Create a merged context from stack and explicit overrides.
    /// Used internally by router to determine final routing parameters.
    /// </summary>
    internal static (string? Source, string? Provider, string? Model) ResolveMerged(
        string? sourceOverride = null,
        string? providerOverride = null,
        string? modelOverride = null)
    {
        // Priority: explicit override > context stack > null
        var source = sourceOverride ?? ResolveSource();
        var provider = providerOverride ?? ResolveProvider();
        var model = modelOverride ?? ResolveModel();

        return (source, provider, model);
    }

    public void Dispose()
    {
        if (_disposed) return;

        // Pop from stack (restore previous stack)
        var current = _contextStack.Value;
        if (current != null && !current.IsEmpty && ReferenceEquals(current.Peek(), this))
        {
            _contextStack.Value = _previousStack;
        }

        _disposed = true;
    }
}
