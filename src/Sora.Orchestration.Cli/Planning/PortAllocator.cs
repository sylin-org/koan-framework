using Sora.Orchestration;
using System.Net;
using System.Net.Sockets;
using Sora.Orchestration.Models;

namespace Sora.Orchestration.Cli.Planning;

internal static class PortAllocator
{
    // Auto-avoid conflicting host ports in non-prod plans. Guard controls how many increments we try before giving up.
    // availabilityProbe: optional for tests; when null, uses a real TcpListener probe on loopback.
    internal static Plan AutoAvoidPorts(Plan plan, int? guard = null, Func<int, bool>? availabilityProbe = null)
    {
        var maxProbe = guard ?? ResolveProbeGuard();
        var assigned = new HashSet<int>();

        bool Available(int port)
        {
            if (assigned.Contains(port)) return false;
            if (availabilityProbe is not null) return availabilityProbe(port);
            try
            {
                using var l = new TcpListener(IPAddress.Loopback, port);
                l.Start();
                l.Stop();
                return true;
            }
            catch { return false; }
        }

        IReadOnlyList<ServiceSpec> Transform()
            => plan.Services.Select(s =>
            {
                var newPorts = new List<(int Host, int Container)>();
                foreach (var (host, container) in s.Ports)
                {
                    var p = host;
                    if (p <= 0)
                    {
                        // container-only mapping; keep as-is and do not reserve 0 in assigned
                        newPorts.Add((0, container));
                        continue;
                    }
                    var tries = 0;
                    while (!Available(p) && tries++ < maxProbe)
                        p++;
                    assigned.Add(p);
                    newPorts.Add((p, container));
                }
                return s with { Ports = newPorts };
            }).ToList();

        return new Plan(plan.Profile, Transform());
    }

    static int ResolveProbeGuard()
    {
        var env = Environment.GetEnvironmentVariable("SORA_PORT_PROBE_MAX");
        return int.TryParse(env, out var v) && v > 0 ? v : 200;
    }
}
