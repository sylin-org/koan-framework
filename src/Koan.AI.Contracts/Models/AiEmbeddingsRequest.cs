namespace Koan.AI.Contracts.Models;

public record AiEmbeddingsRequest
{
    public List<string> Input { get; init; } = new();
    public string? Model { get; init; }

    /// <summary>
    /// ADR-0015: Internal property set by router to inject member URL into adapter.
    /// Adapters use this to route to specific endpoints in singleton pattern.
    /// </summary>
    public string? InternalConnectionString { get; set; }

    /// <summary>
    /// AI-0035: Caller-supplied URL override. When set, the router bypasses source / member
    /// resolution and dispatches directly to this endpoint. The caller assumes ownership of the
    /// routing concerns (health, enable/disable, capability tracking) that source-backed
    /// requests inherit from the registry. Pair with <see cref="OverrideProvider"/> to select
    /// the adapter. Mirrors the same field on <c>AiChatRequest</c> per AI-0035.
    /// </summary>
    public string? OverrideUrl { get; init; }

    /// <summary>
    /// AI-0035: Provider identifier used to select the adapter when <see cref="OverrideUrl"/>
    /// is set. Defaults to <c>"ollama"</c> when omitted.
    /// </summary>
    public string? OverrideProvider { get; init; }
}