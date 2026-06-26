using System;
using Microsoft.Extensions.Options;
using Koan.Data.Relational.Orchestration;

namespace Koan.Data.Connector.Cockroach;

internal sealed class CockroachRelationalMaterializationOptionsConfigurator : IConfigureOptions<RelationalMaterializationOptions>
{
    private readonly IOptions<CockroachOptions> _cockroachOptions;

    public CockroachRelationalMaterializationOptionsConfigurator(IOptions<CockroachOptions> cockroachOptions)
    {
        _cockroachOptions = cockroachOptions;
    }

    public void Configure(RelationalMaterializationOptions options)
    {
        var searchPath = _cockroachOptions.Value.SearchPath;
        if (string.IsNullOrWhiteSpace(searchPath))
        {
            searchPath = "public";
        }

        if (string.IsNullOrWhiteSpace(options.DefaultSchema) || string.Equals(options.DefaultSchema, "dbo", StringComparison.OrdinalIgnoreCase))
        {
            options.DefaultSchema = searchPath;
        }
    }
}
