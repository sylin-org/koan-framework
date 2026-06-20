using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Abstractions.Policies;
using Koan.Data.Core.Model;
using Koan.Jobs;
using Koan.Mcp.Options;
using Koan.Mcp.TestKit;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Mcp.Operations.IntegrationTests;

/// <summary>A trivial job work-type so <c>koan.jobs.trigger</c> reaches the real ledger.</summary>
public sealed class ImportJob : Entity<ImportJob>, IKoanJob<ImportJob>
{
    public string Note { get; set; } = "";
    public static Task Execute(ImportJob job, JobContext ctx, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>A cacheable entity so <c>ICachePolicyRegistry</c> has a policy for <c>koan.cache.flushAll</c> to enumerate.</summary>
[Cacheable(60)]
public sealed class CachedThing : Entity<CachedThing>
{
    public string Value { get; set; } = "";
}

/// <summary>Ops toolsets ENABLED (Koan:Mcp:Operations:{jobs,cache} = true).</summary>
public sealed class OpsEnabledFixture : McpHarnessFixtureBase
{
    protected override void ConfigureMcp(McpServerOptions options)
    {
        options.Operations["jobs"] = true;
        options.Operations["cache"] = true;
    }
}

/// <summary>Ops toolsets DISABLED (the default — no Operations config).</summary>
public sealed class OpsDisabledFixture : McpHarnessFixtureBase
{
}
