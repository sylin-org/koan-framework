using Koan.Orchestration.Models;

namespace Koan.Orchestration.Abstractions;

public interface IArtifactExporter
{
    string Id { get; }
    bool Supports(string format);
    Task GenerateAsync(Plan plan, Profile profile, string outPath, CancellationToken ct = default);
    ExporterCapabilities Capabilities { get; }
}
