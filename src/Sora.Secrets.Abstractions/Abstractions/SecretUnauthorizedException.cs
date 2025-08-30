namespace Sora.Secrets.Abstractions;

public sealed class SecretUnauthorizedException(string id) : SecretException($"Unauthorized to access secret: {id}");