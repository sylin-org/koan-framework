using System.ComponentModel.DataAnnotations;
using Koan.Core.Adapters;
using Koan.Data.Adapters.Configuration;
using Koan.Data.SearchEngine;

namespace Koan.Data.Connector.OpenSearch;

/// <summary>
/// OpenSearch vector connector options. The shared option surface (endpoint, fields, dimension, auth,
/// page-size) lives on <see cref="SearchEngineVectorOptions"/>; this concrete class adds only the
/// per-package binding members (<see cref="ConnectionString"/>, <see cref="Readiness"/>).
/// </summary>
public sealed class OpenSearchOptions : SearchEngineVectorOptions, IAdapterOptions
{
    [Required]
    public string ConnectionString { get; set; } = "auto"; // DX-first: auto-detect by default

    public IAdapterReadinessConfiguration Readiness { get; set; } = new AdapterReadinessConfiguration();
}
