using System;
using System.Collections.Generic;
using FluentAssertions;
using Koan.Orchestration;
using Koan.Orchestration.Cli.Planning;
using Koan.Orchestration.Models;
using Xunit;

namespace Koan.Orchestration.Cli.Tests;

public class PortAllocatorTests
{
    [Fact]
    public void AutoAvoidPorts_bumps_conflicting_ports_with_probe_limit()
    {
        // Arrange: two services both ask for 5432; probe says 5432 and 5433 are taken, 5434+ are free
        var plan = new Plan(Profile.Local, new[]
        {
            new ServiceSpec("db1", "postgres", new Dictionary<string,string?>(), new List<(int Host,int Container)>{ (5432, 5432) }, new List<(string Source,string Target,bool Named)>(), null, null, Array.Empty<string>()),
            new ServiceSpec("db2", "postgres", new Dictionary<string,string?>(), new List<(int Host,int Container)>{ (5432, 5432) }, new List<(string Source,string Target,bool Named)>(), null, null, Array.Empty<string>())
        });

        bool Probe(int port) => port >= 5434; // 5432 and 5433 unavailable

        // Act
        var allocated = PortAllocator.AutoAvoidPorts(plan, guard: 10, availabilityProbe: Probe);

        // Assert: first gets 5434, second gets 5435 (deterministic, unique)
        allocated.Services[0].Ports.Should().ContainSingle(p => p.Host == 5434 && p.Container == 5432);
        allocated.Services[1].Ports.Should().ContainSingle(p => p.Host == 5435 && p.Container == 5432);
    }

    [Fact]
    public void AutoAvoidPorts_respects_guard_and_stops_incrementing_beyond_limit()
    {
        // Arrange: any port is unavailable; guard = 2 means we accept host+2
        var plan = new Plan(Profile.Local, new[]
        {
            new ServiceSpec("svc", "image", new Dictionary<string,string?>(), new List<(int Host,int Container)>{ (1000, 80) }, new List<(string Source,string Target,bool Named)>(), null, null, Array.Empty<string>())
        });

        bool Probe(int _) => false; // nothing available

        // Act
        var allocated = PortAllocator.AutoAvoidPorts(plan, guard: 2, availabilityProbe: Probe);

        // Assert: after 2 increments, we stop at base+2
        allocated.Services[0].Ports.Should().ContainSingle(p => p.Host == 1002 && p.Container == 80);
    }

    [Fact]
    public void AutoAvoidPorts_produces_unique_ports_across_services_and_multiple_mappings()
    {
        // Arrange: two services, each with two ports starting at the same base; first four ports unavailable, then free
        var plan = new Plan(Profile.Local, new[]
        {
            new ServiceSpec("svc1", "image", new Dictionary<string,string?>(), new List<(int Host,int Container)>{ (5000, 80), (5001, 443) }, new List<(string Source,string Target,bool Named)>(), null, null, Array.Empty<string>()),
            new ServiceSpec("svc2", "image", new Dictionary<string,string?>(), new List<(int Host,int Container)>{ (5000, 80), (5001, 443) }, new List<(string Source,string Target,bool Named)>(), null, null, Array.Empty<string>())
        });

        var unavailable = new HashSet<int> { 5000, 5001, 5002, 5003 };
        bool Probe(int port) => !unavailable.Contains(port);

        // Act
        var allocated = PortAllocator.AutoAvoidPorts(plan, guard: 10, availabilityProbe: Probe);

        // Assert: ports are unique across all mappings and maintain order
        var all = allocated.Services.SelectMany(s => s.Ports.Select(p => p.Host)).ToList();
        all.Should().OnlyHaveUniqueItems();
        allocated.Services[0].Ports.Should().Contain(p => p.Host == 5004 && p.Container == 80);
        allocated.Services[0].Ports.Should().Contain(p => p.Host == 5005 && p.Container == 443);
        allocated.Services[1].Ports.Should().Contain(p => p.Host == 5006 && p.Container == 80);
        allocated.Services[1].Ports.Should().Contain(p => p.Host == 5007 && p.Container == 443);
    }
}
