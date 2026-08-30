using System.Text.Json.Serialization;

namespace Koan.Core.Hosting.Bootstrap;

/// <summary>The closed shape of the machine-oriented assembly-scan diagnostic payload emitted under
/// <c>KOAN_VERBOSE_ASSEMBLIES=1</c> (H9). Property names serialize camelCase, matching the wire
/// contract the earlier anonymous-object payload produced.</summary>
internal sealed record AssemblyScanSummary(
    string Event,
    int Loaded,
    Dictionary<string, int> Categories,
    string[] Discovered);

/// <summary>Source-generated serializer for the boot diagnostic payload. The bootstrap runs before
/// any logger exists and under NativeAOT reflection-based JsonSerializer is disabled entirely, so
/// this shape must stay on source-gen (the KoanLockfileJsonContext rule: new closed JSON shapes are
/// source-generated, never reflection-serialized).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AssemblyScanSummary))]
internal sealed partial class AssemblyScanJsonContext : JsonSerializerContext;
