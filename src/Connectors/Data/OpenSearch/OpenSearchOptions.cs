using Koan.Data.SearchEngine;

namespace Koan.Data.Connector.OpenSearch;

/// <summary>
/// OpenSearch vector connector options. The shared option surface (endpoint, fields, dimension, auth,
/// timeout, connection, and readiness) lives on <see cref="SearchEngineVectorOptions"/>.
/// </summary>
public sealed class OpenSearchOptions : SearchEngineVectorOptions
{
}
