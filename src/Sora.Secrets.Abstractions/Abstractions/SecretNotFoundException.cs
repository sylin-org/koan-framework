namespace Sora.Secrets.Abstractions;

public sealed class SecretNotFoundException(string id) : SecretException($"Secret not found: {id}");