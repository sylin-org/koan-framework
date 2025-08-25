using Microsoft.Extensions.Primitives;
using Sora.Media.Core.Options;
using Sora.Storage.Abstractions;

namespace Sora.Media.Core.Operators;

public interface IMediaOperatorRegistry
{
    IReadOnlyList<IMediaOperator> Operators { get; }
    IMediaOperator? FindById(string id);
    // Given an incoming query bag, select operators and normalized params per operator in precedence order
    IReadOnlyList<(IMediaOperator Op, IReadOnlyDictionary<string, string> Params)> ResolveOperators(IDictionary<string, StringValues> query, ObjectStat sourceStat, string? contentType, MediaTransformOptions options);
}