using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Koan.Orchestration;
using Koan.Orchestration.Planning;
using Koan.Orchestration.Cli.Planning;
using Xunit;

namespace Koan.Orchestration.Cli.Tests;

public class PlannerOverridesTests
{
    [Fact]
    public void FromDraft_uses_Local_mode_tokens_when_mode_is_Local()
    {
        // Arrange
        var draft = new PlanDraft(new[]
        {
            new ServiceRequirement(
                Id: "mongo",
                Image: "mongo:7",
                Env: new Dictionary<string,string?>(),
                ContainerPorts: new[] { 27017 },
                Volumes: new[] { "./Data/mongo:/data/db" },
                AppEnv: new Dictionary<string,string?>
                {
                    ["Koan__Data__Mongo__ConnectionString"] = "{scheme}://{host}:{port}",
                    ["Koan__Data__Mongo__Database"] = "Koan"
                },
                EndpointScheme: "mongodb",
                EndpointHost: "mongo",
                EndpointUriPattern: "mongodb://{host}:{port}",
                LocalScheme: "mongodb",
                LocalHost: "localhost",
                LocalPort: 27017,
                LocalUriPattern: "mongodb://{host}:{port}"
            )
        }, IncludeApp: true, AppHttpPort: 8080);

        // Inject overrides via file so Planner picks Local
        // Create a temp overrides.json so Planner picks Mode=Local
        var orig = Directory.GetCurrentDirectory();
        var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(temp, ".Koan"));
        File.WriteAllText(Path.Combine(temp, ".Koan", "overrides.json"), "{\n  \"Mode\": \"Local\"\n}");
        Directory.SetCurrentDirectory(temp);
        try
        {
            // Act
            var plan = Planner.FromDraft(Profile.Local, draft);

            // Assert
            var api = plan.Services.First(s => s.Id == "api");
            api.Env["Koan__Data__Mongo__ConnectionString"].Should().Be("mongodb://localhost:27017");
            api.DependsOn.Should().Contain("mongo");
        }
        finally
        {
            Directory.SetCurrentDirectory(orig);
            try { Directory.Delete(temp, true); } catch { }
        }
    }

    [Fact]
    public void Overrides_Ports_replace_container_ports_in_plan()
    {
        // Arrange: draft with a service exposing a default container port
        var draft = new PlanDraft(new[]
        {
            new ServiceRequirement(
                Id: "redis",
                Image: "redis:7",
                Env: new Dictionary<string,string?>(),
                ContainerPorts: new[] { 6379 },
                Volumes: Array.Empty<string>(),
                AppEnv: new Dictionary<string,string?>(),
                EndpointScheme: "redis",
                EndpointHost: "redis",
                EndpointUriPattern: null,
                LocalScheme: "redis",
                LocalHost: "localhost",
                LocalPort: 6379,
                LocalUriPattern: null
            )
        }, IncludeApp: false, AppHttpPort: 8080);

        // Build an Overrides object with Ports override and apply to draft
        var pov = new PrivateOverrides
        {
            Services = new Dictionary<string, PrivateOverrides.Service>
            {
                ["redis"] = new PrivateOverrides.Service
                {
                    // No image/env/volumes, just Ports override
                }
            }
        };
        // Manually extend the Planner overrides shape to include Ports via reflection-friendly mapper
        var ov = new PrivateToPlannerOverridesMapper(pov).ToPlannerOverrides();
        // Set Ports on the inner Service (use reflection to set List<int> Ports)
        var overridesType = typeof(Planner).GetNestedType("Overrides", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!;
        var svcType = overridesType.GetNestedType("Service", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!;
        var servicesProp = overridesType.GetProperty("Services")!;
        var dict = (System.Collections.IDictionary)servicesProp.GetValue(ov)!;
        var svc = dict["redis"];
        svcType.GetProperty("Ports")!.SetValue(svc, new List<int> { 16379 });

        var apply = overridesType.GetMethod("Apply", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
        draft = (PlanDraft)apply.Invoke(null, new object?[] { draft, ov })!;

        // Act: build a plan from the overridden draft
        var plan = Planner.FromDraft(Profile.Local, draft);

        // Assert: container ports were replaced and mapped host:container as p:p
        var svcRedis = plan.Services.First(s => s.Id == "redis");
        svcRedis.Ports.Should().ContainSingle();
        svcRedis.Ports[0].Should().Be((16379, 16379));
    }

    [Fact]
    public void FromDraft_applies_service_env_and_volume_overrides()
    {
        // Arrange
        var draft = new PlanDraft(new[]
        {
            new ServiceRequirement(
                Id: "mongo",
                Image: "mongo:7",
                Env: new Dictionary<string,string?>(),
                ContainerPorts: new[] { 27017 },
                Volumes: Array.Empty<string>(),
                AppEnv: new Dictionary<string,string?>(),
                EndpointScheme: "mongodb",
                EndpointHost: "mongo",
                EndpointUriPattern: null,
                LocalScheme: "mongodb",
                LocalHost: "localhost",
                LocalPort: 27017,
                LocalUriPattern: null
            )
        }, IncludeApp: false, AppHttpPort: 8080);

        var orig = Directory.GetCurrentDirectory();
        var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        Directory.SetCurrentDirectory(temp);
        Directory.CreateDirectory(Path.Combine(temp, ".Koan"));
        // Apply overrides directly
        var ov = new PrivateOverrides
        {
            Services = new Dictionary<string, PrivateOverrides.Service>
            {
                ["mongo"] = new PrivateOverrides.Service
                {
                    Env = new Dictionary<string,string?> { ["MONGO_INITDB_ROOT_USERNAME"] = "root" },
                    Volumes = new List<string> { "./Data/mongo:/data/db" }
                }
            }
        };
        draft = PlannerOverridesApply(draft, ov);

        // Act
        var plan = Planner.FromDraft(Profile.Local, draft);

        // Assert backing service
        var mongo = plan.Services.First(s => s.Id == "mongo");
        mongo.Env.Should().ContainKey("MONGO_INITDB_ROOT_USERNAME");
        mongo.Env["MONGO_INITDB_ROOT_USERNAME"].Should().Be("root");
        mongo.Volumes.Should().Contain(v => v.Target == "/data/db" && v.Source.EndsWith("./Data/mongo"));
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }
    // Local aliases to avoid InternalsVisibleTo bleed; mirrors Planner.Overrides shape
    private sealed class PrivateOverrides
    {
        public string? Mode { get; set; }
        public Dictionary<string, Service>? Services { get; set; }
        public sealed class Service
        {
            public string? Image { get; set; }
            public Dictionary<string,string?>? Env { get; set; }
            public List<string>? Volumes { get; set; }
        }
    }

    private static PlanDraft PlannerOverridesApply(PlanDraft draft, PrivateOverrides pov)
    {
        // Map PrivateOverrides to Planner.Overrides and call Apply
        var ov = new PrivateToPlannerOverridesMapper(pov).ToPlannerOverrides();
        var method = typeof(Planner).GetNestedType("Overrides", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        var apply = method!.GetMethod("Apply", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        return (PlanDraft)apply!.Invoke(null, new object?[] { draft, ov })!;
    }

    private sealed class PrivateToPlannerOverridesMapper
    {
        private readonly PrivateOverrides _pov;
        public PrivateToPlannerOverridesMapper(PrivateOverrides pov) => _pov = pov;
        public object ToPlannerOverrides()
        {
            var overridesType = typeof(Planner).GetNestedType("Overrides", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!;
            var svcType = overridesType.GetNestedType("Service", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!;
            var ov = Activator.CreateInstance(overridesType)!;
            if (_pov.Mode is { }) overridesType.GetProperty("Mode")!.SetValue(ov, _pov.Mode);
            if (_pov.Services is { })
            {
                var dictType = typeof(Dictionary<,>).MakeGenericType(typeof(string), svcType);
                var dict = (System.Collections.IDictionary)Activator.CreateInstance(dictType)!;
                foreach (var (k,v) in _pov.Services)
                {
                    var sv = Activator.CreateInstance(svcType)!;
                    if (v.Image is { }) svcType.GetProperty("Image")!.SetValue(sv, v.Image);
                    if (v.Env is { }) svcType.GetProperty("Env")!.SetValue(sv, v.Env);
                    if (v.Volumes is { }) svcType.GetProperty("Volumes")!.SetValue(sv, v.Volumes);
                    dict.Add(k, sv);
                }
                overridesType.GetProperty("Services")!.SetValue(ov, dict);
            }
            return ov;
        }
    }
}
