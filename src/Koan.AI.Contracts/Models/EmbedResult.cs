namespace Koan.AI.Contracts.Models;

/// <summary>
/// Rich result from an embedding operation, returned by <c>Client.EmbedResult()</c>.
/// </summary>
public sealed record EmbedResult
{
    /// <summary>Generated embedding vector.</summary>
    public float[] Vector { get; init; } = [];

    /// <summary>Model that served the request.</summary>
    public string? Model { get; init; }

    /// <summary>Dimension of the vector.</summary>
    public int Dimension { get; init; }

    /// <summary>Tokens consumed by the input.</summary>
    public int? TokensUsed { get; init; }
}
