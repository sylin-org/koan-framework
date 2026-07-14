namespace Koan.Core.Diagnostics;

/// <summary>A versioned, immutable snapshot projected to humans, operators, and agents.</summary>
public sealed record KoanFactEnvelope(
    int Schema,
    long Sequence,
    string SessionId,
    DateTimeOffset CapturedAtUtc,
    bool Complete,
    IReadOnlyList<KoanFact> Facts);
