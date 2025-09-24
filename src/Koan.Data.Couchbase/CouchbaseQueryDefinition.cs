using System;
using System.Collections.Generic;

namespace Koan.Data.Couchbase;

/// <summary>
/// Simple representation of a Couchbase N1QL query that can be passed through <see cref="IDataRepository{TEntity, TKey}"/> APIs.
/// </summary>
public sealed record CouchbaseQueryDefinition(string Statement)
{
    public IDictionary<string, object?>? Parameters { get; init; }
    public TimeSpan? Timeout { get; init; }
}
