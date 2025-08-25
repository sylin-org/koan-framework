namespace Sora.Messaging;

public sealed record DispatchOutcome(DispatchResultKind Kind, int Attempt, string IdempotencyKey);