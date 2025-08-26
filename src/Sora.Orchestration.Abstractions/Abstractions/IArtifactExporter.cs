namespace Sora.Orchestration;

public interface IArtifactExporter
{
    string Id { get; }
    bool Supports(string format);
    Task GenerateAsync(Plan plan, Profile profile, string outPath, CancellationToken ct = default);
    ExporterCapabilities Capabilities { get; }
}

public sealed record ExporterCapabilities(bool SecretsRefOnly, bool ReadinessProbes, bool TlsHints);
