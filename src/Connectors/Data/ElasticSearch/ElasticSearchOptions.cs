using Koan.Data.SearchEngine;

namespace Koan.Data.Connector.ElasticSearch;

/// <summary>
/// Elasticsearch vector connector options. The shared option surface (endpoint, fields, dimension,
/// auth, timeout, connection, and readiness) lives on <see cref="SearchEngineVectorOptions"/>.
/// </summary>
public sealed class ElasticSearchOptions : SearchEngineVectorOptions
{
}
