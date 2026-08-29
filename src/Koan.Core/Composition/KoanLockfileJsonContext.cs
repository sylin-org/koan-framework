using System.Text.Json.Serialization;

namespace Koan.Core.Composition;

/// <summary>
/// Source-generated JSON contract for the lockfile, so serialization survives NativeAOT without
/// reflection-based <c>JsonSerializer</c> (IL2026/IL3050). The options mirror the serializer's
/// deterministic contract: camelCase properties, verbatim dictionary keys (no DictionaryKeyPolicy),
/// 2-space indent, null sections omitted — a regenerated lockfile stays byte-stable.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(KoanLockfile))]
internal sealed partial class KoanLockfileJsonContext : JsonSerializerContext;
