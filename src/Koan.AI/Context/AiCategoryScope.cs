using System;
using System.Collections.Immutable;
using System.Threading;
using Koan.AI.Contracts;

namespace Koan.AI.Context;

/// <summary>
/// Scoped routing context for AI operations with per-category source/model overrides.
/// Uses AsyncLocal for ambient context that flows across async boundaries.
///
/// Example usage:
/// <code>
/// // Route Chat and Embed to different sources
/// using (Client.Scope(chat: "ollama-gpu", embed: "openai-prod"))
/// {
///     var answer = await Client.Chat("Analyze this");
///     var vector = await Client.Embed("text to embed");
/// }
///
/// // Override all categories at once
/// using (Client.Scope(all: "local-ollama"))
/// {
///     var answer = await Client.Chat("Question");
/// }
///
/// // Route generation verbs to a dedicated GPU source
/// using (Client.Scope(imagine: "comfyui-local", speak: "piper-local", render: "comfyui-local"))
/// {
///     var image = await Client.Imagine("A cat in space");
///     var audio = await Client.Speak("Hello world");
/// }
/// </code>
/// </summary>
public sealed class AiCategoryScope : IDisposable
{
    private static readonly AsyncLocal<ImmutableStack<AiCategoryScope>> _contextStack = new();

    private readonly string? _all;
    private readonly CategoryOverride? _chat;
    private readonly CategoryOverride? _embed;
    private readonly CategoryOverride? _ocr;
    private readonly CategoryOverride? _imagine;
    private readonly CategoryOverride? _transcribe;
    private readonly CategoryOverride? _speak;
    private readonly CategoryOverride? _describe;
    private readonly CategoryOverride? _classify;
    private readonly CategoryOverride? _extract;
    private readonly CategoryOverride? _rerank;
    private readonly CategoryOverride? _translate;
    private readonly CategoryOverride? _moderate;
    private readonly CategoryOverride? _edit;
    private readonly CategoryOverride? _render;
    private readonly ImmutableStack<AiCategoryScope> _previousStack;
    private bool _disposed;

    internal AiCategoryScope(
        string? all = null,
        string? chatSource = null, string? chatModel = null,
        string? embedSource = null, string? embedModel = null,
        string? ocrSource = null, string? ocrModel = null,
        string? imagineSource = null, string? imagineModel = null,
        string? transcribeSource = null, string? transcribeModel = null,
        string? speakSource = null, string? speakModel = null,
        string? describeSource = null, string? describeModel = null,
        string? classifySource = null, string? classifyModel = null,
        string? extractSource = null, string? extractModel = null,
        string? rerankSource = null, string? rerankModel = null,
        string? translateSource = null, string? translateModel = null,
        string? moderateSource = null, string? moderateModel = null,
        string? editSource = null, string? editModel = null,
        string? renderSource = null, string? renderModel = null)
    {
        _all = all;
        _chat = (chatSource ?? chatModel) is not null ? new CategoryOverride(chatSource, chatModel) : null;
        _embed = (embedSource ?? embedModel) is not null ? new CategoryOverride(embedSource, embedModel) : null;
        _ocr = (ocrSource ?? ocrModel) is not null ? new CategoryOverride(ocrSource, ocrModel) : null;
        _imagine = (imagineSource ?? imagineModel) is not null ? new CategoryOverride(imagineSource, imagineModel) : null;
        _transcribe = (transcribeSource ?? transcribeModel) is not null ? new CategoryOverride(transcribeSource, transcribeModel) : null;
        _speak = (speakSource ?? speakModel) is not null ? new CategoryOverride(speakSource, speakModel) : null;
        _describe = (describeSource ?? describeModel) is not null ? new CategoryOverride(describeSource, describeModel) : null;
        _classify = (classifySource ?? classifyModel) is not null ? new CategoryOverride(classifySource, classifyModel) : null;
        _extract = (extractSource ?? extractModel) is not null ? new CategoryOverride(extractSource, extractModel) : null;
        _rerank = (rerankSource ?? rerankModel) is not null ? new CategoryOverride(rerankSource, rerankModel) : null;
        _translate = (translateSource ?? translateModel) is not null ? new CategoryOverride(translateSource, translateModel) : null;
        _moderate = (moderateSource ?? moderateModel) is not null ? new CategoryOverride(moderateSource, moderateModel) : null;
        _edit = (editSource ?? editModel) is not null ? new CategoryOverride(editSource, editModel) : null;
        _render = (renderSource ?? renderModel) is not null ? new CategoryOverride(renderSource, renderModel) : null;

        _previousStack = _contextStack.Value ?? ImmutableStack<AiCategoryScope>.Empty;
        _contextStack.Value = _previousStack.Push(this);
    }

    /// <summary>
    /// Resolve the effective source for a given category.
    /// Priority: explicit category override > "all" override > parent scope > null.
    /// </summary>
    public static string? ResolveSource(string category)
    {
        var stack = _contextStack.Value;
        if (stack is null || stack.IsEmpty) return null;

        foreach (var scope in stack)
        {
            var categoryOverride = scope.GetCategoryOverride(category);
            if (categoryOverride?.Source is not null)
                return categoryOverride.Source;

            if (scope._all is not null)
                return scope._all;
        }

        return null;
    }

    /// <summary>
    /// Resolve the effective model for a given category.
    /// Priority: explicit category override > parent scope > null.
    /// </summary>
    public static string? ResolveModel(string category)
    {
        var stack = _contextStack.Value;
        if (stack is null || stack.IsEmpty) return null;

        foreach (var scope in stack)
        {
            var categoryOverride = scope.GetCategoryOverride(category);
            if (categoryOverride?.Model is not null)
                return categoryOverride.Model;
        }

        return null;
    }

    /// <summary>
    /// Resolve merged source/model for a category from scope stack + explicit overrides.
    /// Used internally by the category router.
    /// </summary>
    internal static (string? Source, string? Model) ResolveMerged(
        string category,
        string? sourceOverride = null,
        string? modelOverride = null)
    {
        var source = sourceOverride ?? ResolveSource(category);
        var model = modelOverride ?? ResolveModel(category);
        return (source, model);
    }

    private CategoryOverride? GetCategoryOverride(string category) => category switch
    {
        AiCapability.Chat => _chat,
        AiCapability.Embed => _embed,
        AiCapability.Ocr => _ocr,
        AiCapability.Imagine => _imagine,
        AiCapability.Transcribe => _transcribe,
        AiCapability.Speak => _speak,
        AiCapability.Rerank => _rerank,
        AiCapability.Translate => _translate,
        AiCapability.Moderate => _moderate,
        AiCapability.Edit => _edit,
        AiCapability.Render => _render,
        // Task verbs that delegate through Chat use their own scope entry
        // so they can be routed independently from raw Chat calls.
        "Describe" => _describe,
        "Classify" => _classify,
        "Extract" => _extract,
        _ => null
    };

    public void Dispose()
    {
        if (_disposed) return;

        var current = _contextStack.Value;
        if (current is not null && !current.IsEmpty && ReferenceEquals(current.Peek(), this))
        {
            _contextStack.Value = _previousStack;
        }

        _disposed = true;
    }

    private sealed record CategoryOverride(string? Source, string? Model);
}
