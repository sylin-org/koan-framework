namespace Sora.Orchestration.Abstractions;

public sealed record ExporterCapabilities(bool SecretsRefOnly, bool ReadinessProbes, bool TlsHints);