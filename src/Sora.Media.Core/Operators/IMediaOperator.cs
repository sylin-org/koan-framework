using Sora.Storage.Abstractions;

namespace Sora.Media.Core.Operators;

using Microsoft.Extensions.Primitives;
using Sora.Media.Core.Options;
using Sora.Storage;

public interface IMediaOperator
{
    // e.g., "resize@1"
    string Id { get; }
    MediaOperatorPlacement Placement { get; }

    // Canonical parameter names with their alias sets (lowercase)
    IReadOnlyDictionary<string, IReadOnlySet<string>> ParameterAliases { get; }

    // Supported media types (prefix match allowed, e.g., "image/")
    IReadOnlyList<string> SupportedContentTypes { get; }

    // Normalize and validate parameters (first-wins for duplicates already applied)
    // Returns null if the operator should be skipped (e.g., no relevant params present)
    // Throws ArgumentException for invalid combinations when Strict.
    IReadOnlyDictionary<string, string>? Normalize(IDictionary<string, StringValues> query, ObjectStat sourceStat, MediaTransformOptions options, bool strict);

    // Execute on a source stream and write to destination; return content-type if changed, else null
    Task<(string? ContentType, long BytesWritten)> Execute(Stream source, string sourceContentType, Stream destination, IReadOnlyDictionary<string, string> parameters, MediaTransformOptions options, CancellationToken ct);
}

public interface IMediaOperatorRegistry
{
    IReadOnlyList<IMediaOperator> Operators { get; }
    IMediaOperator? FindById(string id);
    // Given an incoming query bag, select operators and normalized params per operator in precedence order
    IReadOnlyList<(IMediaOperator Op, IReadOnlyDictionary<string, string> Params)> ResolveOperators(IDictionary<string, StringValues> query, ObjectStat sourceStat, string? contentType, MediaTransformOptions options);
}
