using Microsoft.Extensions.Configuration;
using System;

namespace Sora.Data.Direct;

public sealed class DirectDataService(IServiceProvider sp, IConfiguration config) : Sora.Data.Core.Direct.IDirectDataService
{
    public Sora.Data.Core.Direct.IDirectSession Direct(string sourceOrAdapter)
        => new DirectSession(sp, config, sourceOrAdapter);
}