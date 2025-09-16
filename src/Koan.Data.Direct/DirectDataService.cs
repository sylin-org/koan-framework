using Microsoft.Extensions.Configuration;
using System;

namespace Koan.Data.Direct;

public sealed class DirectDataService(IServiceProvider sp, IConfiguration config) : Koan.Data.Core.Direct.IDirectDataService
{
    public Koan.Data.Core.Direct.IDirectSession Direct(string sourceOrAdapter)
        => new DirectSession(sp, config, sourceOrAdapter);
}