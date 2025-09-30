using Microsoft.Extensions.Configuration;
using System;

namespace Koan.Data.Direct;

public sealed class DirectDataService(IServiceProvider sp, IConfiguration config) : Koan.Data.Core.Direct.IDirectDataService
{
    public Koan.Data.Core.Direct.IDirectSession Direct(string? source = null, string? adapter = null)
    {
        // Validate source XOR adapter constraint
        if (!string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(adapter))
        {
            throw new InvalidOperationException(
                "Cannot specify both 'source' and 'adapter'. Sources define their own adapter selection.");
        }

        return new DirectSession(sp, config, source, adapter);
    }
}