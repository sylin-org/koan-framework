using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Couchbase;
using Couchbase.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Koan.Core;
using Koan.Core.Orchestration;
using Koan.Data.Connector.Couchbase.Infrastructure;

namespace Koan.Data.Connector.Couchbase.Orchestration;

public sealed class CouchbaseOrchestrationEvaluator : BaseOrchestrationEvaluator
{
    public CouchbaseOrchestrationEvaluator(ILogger<CouchbaseOrchestrationEvaluator>? logger = null)
        : base(logger)
    {
    }

    public override string ServiceName => Constants.Discovery.ServiceName;
    public override int StartupPriority => 160;

    protected override bool IsServiceEnabled(IConfiguration configuration)
    {
        var cs = Configuration.ReadFirst(configuration, string.Empty,
            Constants.Configuration.Keys.ConnectionString,
            Constants.Configuration.Keys.AltConnectionString,
            Constants.Configuration.Keys.ConnectionStringsCouchbase,
            Constants.Configuration.Keys.ConnectionStringsDefault);
        return !string.IsNullOrWhiteSpace(cs);
    }

    protected override bool HasExplicitConfiguration(IConfiguration configuration)
    {
        var cs = Configuration.ReadFirst(configuration, string.Empty,
            Constants.Configuration.Keys.ConnectionString,
            Constants.Configuration.Keys.AltConnectionString,
            Constants.Configuration.Keys.ConnectionStringsCouchbase,
            Constants.Configuration.Keys.ConnectionStringsDefault);
        return !string.IsNullOrWhiteSpace(cs) && !string.Equals(cs.Trim(), "auto", StringComparison.OrdinalIgnoreCase);
    }

    protected override int GetDefaultPort() => Constants.Discovery.ManagerPort;

    protected override string[] GetAdditionalHostCandidates(IConfiguration configuration)
    {
        var hosts = new List<string>();
        var configured = configuration.GetSection("Koan:Data:Couchbase:Hosts").Get<string[]>() ?? Array.Empty<string>();
        hosts.AddRange(configured.Where(h => !string.IsNullOrWhiteSpace(h))!);
        var env = Environment.GetEnvironmentVariable("COUCHBASE_HOSTS");
        if (!string.IsNullOrWhiteSpace(env))
        {
            hosts.AddRange(env.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(h => h.Trim())
                .Where(h => !string.IsNullOrWhiteSpace(h))!);
        }
        return hosts.ToArray();
    }

    protected override async Task<bool> ValidateHostCredentials(IConfiguration configuration, HostDetectionResult hostResult)
    {
        if (string.IsNullOrWhiteSpace(hostResult.HostEndpoint))
        {
            return false;
        }

        try
        {
            var (host, _) = ParseHost(hostResult.HostEndpoint);
            var username = Configuration.ReadFirst(configuration, "", Constants.Configuration.Keys.Username, "Koan:Data:Username");
            var password = Configuration.ReadFirst(configuration, "", Constants.Configuration.Keys.Password, "Koan:Data:Password");
            var connectionString = $"couchbase://{host}";
            var options = new global::Couchbase.ClusterOptions();
            if (!string.IsNullOrWhiteSpace(username))
            {
                options.UserName = username;
                options.Password = password ?? string.Empty;
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await using var cluster = await Cluster.ConnectAsync(connectionString, options).ConfigureAwait(false);
            await cluster.PingAsync(new PingOptions().CancellationToken(cts.Token)).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    protected override Task<DependencyDescriptor> CreateDependencyDescriptorAsync(IConfiguration configuration, OrchestrationContext context)
    {
        var bucket = Configuration.ReadFirst(configuration, "Koan",
            Constants.Configuration.Keys.Bucket,
            "Koan:Data:Bucket",
            "ConnectionStrings:Database");
        var username = Configuration.ReadFirst(configuration, "Administrator",
            Constants.Configuration.Keys.Username,
            "Koan:Data:Username");
        var password = Configuration.ReadFirst(configuration, "couchbase",
            Constants.Configuration.Keys.Password,
            "Koan:Data:Password");

        var descriptor = new DependencyDescriptor
        {
            Name = ServiceName,
            Image = "couchbase:community",
            Port = Constants.Discovery.ManagerPort,
            Ports = new Dictionary<int, int>
            {
                [8092] = 8092,
                [8093] = 8093,
                [8094] = 8094,
                [11210] = 11210
            },
            StartupPriority = StartupPriority,
            HealthTimeout = TimeSpan.FromSeconds(60),
            HealthCheckCommand = "curl -s http://localhost:8091/pools | grep couchbase",
            Environment = new Dictionary<string, string>
            {
                ["COUCHBASE_ADMINISTRATOR_USERNAME"] = username,
                ["COUCHBASE_ADMINISTRATOR_PASSWORD"] = password,
                ["COUCHBASE_BUCKET"] = bucket
            },
            Volumes = new List<string>
            {
                $"koan-couchbase-{context.SessionId}:/opt/couchbase/var"
            }
        };

        return Task.FromResult(descriptor);
    }

    private static (string host, int port) ParseHost(string endpoint)
    {
        var parts = endpoint.Split(':');
        var host = parts[0];
        var port = parts.Length > 1 && int.TryParse(parts[1], out var parsed) ? parsed : Constants.Discovery.ManagerPort;
        return (host, port);
    }
}

