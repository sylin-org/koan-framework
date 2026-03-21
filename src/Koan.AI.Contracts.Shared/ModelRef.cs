namespace Koan.AI.Contracts.Shared;

/// <summary>
/// Lightweight model identity. Shared across all bounded contexts
/// (Model, Training, Eval, Chain, Client).
///
/// <code>
/// ModelRef model = "meta-llama/Llama-3.1-8B-Instruct";
/// ModelRef versioned = new("acme-support", Version: 3);
/// </code>
/// </summary>
public sealed record ModelRef(string Id, int? Version = null)
{
    /// <summary>Implicit conversion from string for convenience.</summary>
    public static implicit operator ModelRef(string id) => new(id);

    public override string ToString() =>
        Version is not null ? $"{Id}:v{Version}" : Id;
}
