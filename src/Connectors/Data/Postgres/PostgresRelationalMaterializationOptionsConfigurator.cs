using System;
using Microsoft.Extensions.Options;
using Koan.Data.Relational.Orchestration;

namespace Koan.Data.Connector.Postgres;

internal sealed class PostgresRelationalMaterializationOptionsConfigurator : IConfigureOptions<RelationalMaterializationOptions>
{
    private readonly IOptions<PostgresOptions> _postgresOptions;

    public PostgresRelationalMaterializationOptionsConfigurator(IOptions<PostgresOptions> postgresOptions)
    {
        _postgresOptions = postgresOptions;
    }

    public void Configure(RelationalMaterializationOptions options)
    {
        var searchPath = _postgresOptions.Value.SearchPath;
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
