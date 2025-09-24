namespace Koan.Secrets.Abstractions;

public sealed class SecretProviderUnavailableException(string provider, string? reason = null)
    : SecretException($"Secret provider unavailable: {provider}{(reason is null ? string.Empty : $" - {reason}")}");