# Koan Container Orchestration and DevOps Analysis

## Executive Summary

Koan's Container Orchestration provides intelligent, developer-focused container management that works across local development and production deployment through manifest-driven discovery, zero-configuration service resolution, and multi-runtime provider abstraction.

## Orchestration Architecture and Philosophy

### Core Philosophy: Intelligence-First Container Orchestration

**Declarative-Intelligent Container Management Approach**

**Manifest-Driven Discovery Architecture:**
```csharp
// Services auto-discover capabilities through attributes
[KoanService(ServiceKind.Database, "mongo", "MongoDB")]
[ContainerDefaults("mongo:7", Ports = new[] { 27017 })]
[HealthEndpointDefaults("/health")]
public class MongoAdapter : IServiceAdapter { }
```

**System Components:**
- **Roslyn Source Generators**: Compile-time assembly scanning for unified manifests
- **Zero-Configuration Service Resolution**: Automatic dependency resolution through Provides/Consumes tokens
- **Multi-Runtime Provider Abstraction**: `IHostingProvider` interface abstracts container runtime specifics

**Key Differentiators:**
- **Smart Port Allocation**: Deterministic port assignment using FNV-1a hashing
- **Readiness-First Operations**: Automatic health probing with configurable timeouts
- **Profile-Aware Configuration**: Different behaviors for Local/CI/Staging/Production environments
- **Intelligent Conflict Resolution**: Automatic service skipping when port conflicts detected

## Provider Abstraction and Multi-Runtime Support

### Docker Provider Implementation

**Sophisticated Container Runtime Integration:**
```csharp
public async Task Up(string composePath, Profile profile, RunOptions options, CancellationToken ct = default)
{
    var detach = options.Detach ? "-d" : string.Empty;
    await Run("docker", $"compose -f \"{composePath}\" up {detach}", ct);

    // Advanced readiness probing
    if (options.ReadinessTimeout is { } timeout && timeout > TimeSpan.Zero)
    {
        // Continuous polling until all services are healthy
    }
}
```

**Advanced Features:**
- **JSON Response Parsing**: Handles both JSON arrays and NDJSON formats
- **Port Discovery**: Real-time port binding extraction from running containers
- **Health State Monitoring**: Continuous polling until all services are healthy
- **Version Detection**: Automatic Docker/Podman version detection and capability reporting

### Podman Provider Implementation

**Parallel Implementation with Podman-Specific Optimizations:**
```csharp
public async Task<IReadOnlyList<PortBinding>> LivePorts(CancellationToken ct = default)
{
    var (code, outText, _) = await Run("podman", "compose ps --format json", ct);
    return ParseComposePsPorts(outText); // Handles Podman's JSON format differences
}
```

**Provider Selection Logic:**
```csharp
// Priority-based provider selection with fallback
var providers = new List<IHostingProvider> { new DockerProvider(), new PodmanProvider() };
foreach (var p in OrderByPreference(providers))
{
    var (ok, _) = await p.IsAvailableAsync();
    if (ok) return p;
}
```

## Configuration Generation and Infrastructure-as-Code

### Compose Renderer Architecture

**Sophisticated Docker Compose Generation with Intelligent Defaults:**

**Smart Volume Management:**
```csharp
static ServiceSpec EnsureHostMounts(ServiceSpec svc, Dictionary<string, List<string>> mountMap, Profile profile)
{
    // Profile-specific mount strategies
    if (profile == Profile.Prod) return svc; // No automatic mounts in production

    if (profile == Profile.Ci)
    {
        // CI: ephemeral named volumes
        var volName = $"data_{svc.Id}";
        vols.Add((Source: volName, Target: target, Named: true));
    }
    else
    {
        // Local/Staging: bind mounts
        vols.Add((Source: $"./Data/{svc.Id}", Target: target, Named: false));
    }
}
```

**Automatic Image Detection and Health Checks:**
```csharp
// Intelligent health check generation
if (s.Health is not null && !string.IsNullOrWhiteSpace(s.Health.HttpEndpoint))
{
    var test = $"(curl -fsS {s.Health.HttpEndpoint} || wget -q -O- {s.Health.HttpEndpoint}) >/dev/null 2>&1 || exit 1";
    yaml.Append("    test: [\"CMD-SHELL\", \"").Append(EscapeJson(test)).AppendLine("\"]");
}
```

**Repository Context-Aware Building:**
```csharp
// Auto-detect project structure for build contexts
if (string.Equals(svc.Id, "api", StringComparison.OrdinalIgnoreCase))
{
    var hasProject = Directory.EnumerateFiles(cwd, "*.csproj", SearchOption.TopDirectoryOnly).Any();
    if (hasProject)
    {
        var contextDir = FindRepoRoot(cwd) ?? cwd;
        var relContext = ToPosixPath(Path.GetRelativePath(composeDir, contextDir));
    }
}
```

### Orchestration Manifest Generator

**Source Generator Creates Comprehensive Service Manifests:**
```csharp
[Generator(LanguageNames.CSharp)]
public sealed class OrchestrationManifestGenerator : ISourceGenerator
{
    public void Execute(GeneratorExecutionContext context)
    {
        // Scan assemblies for service attributes
        ProcessKoanServiceAttribute(decl, context, candidates);
        ProcessLegacyAttributes(decl, context, candidates);

        // Generate manifest JSON embedded in code
        var json = BuildJson(candidates, app, authProviders);
        context.AddSource("__KoanOrchestrationManifest.g.cs", SourceText.From(src, Encoding.UTF8));
    }
}
```

**Generated Manifest Structure:**
```json
{
  "schemaVersion": 1,
  "app": {
    "code": "s5api",
    "name": "S5 Recs API",
    "defaultPublicPort": 8080,
    "capabilities": ["http", "swagger", "graphql"]
  },
  "services": [
    {
      "id": "mongo",
      "containerImage": "mongo",
      "defaultTag": "7",
      "provides": ["database"],
      "healthEndpoint": "/health"
    }
  ]
}
```

## CLI Integration and Developer Workflow

### Command Architecture

**Unified Interface for All Orchestration Operations:**
```csharp
return cmd switch
{
    "export" => await ExportAsync(rest),    // Generate artifacts
    "doctor" => await DoctorAsync(rest),    // Environment validation
    "up" => await UpAsync(rest),           // Start services
    "down" => await DownAsync(rest),       // Stop services
    "status" => await StatusAsync(rest),    // Runtime status
    "logs" => await LogsAsync(rest),       // Log aggregation
    "inspect" => await InspectAsync(rest), // Project introspection
    _ => Help()
};
```

### Advanced CLI Features

**Intelligent Port Management:**
```csharp
public static Plan AssignAppPublicPort(Plan plan, int? explicitPort = null)
{
    // Precedence: CLI flag > launch manifest > code default > deterministic hash
    var desired = explicitPort ?? lm?.App.AssignedPublicPort ?? defaultFromCode ?? DeterministicFor(app.Id);
    var assigned = PickAvailable(desired);

    // Persist assignments to .Koan/manifest.json for consistency
    lm.App.AssignedPublicPort = assigned;
    LaunchManifest.Save(cwd, lm);
}
```

**Profile-Aware Operations:**
```csharp
static Profile ResolveProfile(string? arg)
{
    // Precedence: --profile > Koan_ENV > Local
    var src = arg ?? Environment.GetEnvironmentVariable("Koan_ENV");
    return src?.ToLowerInvariant() switch
    {
        "ci" => Profile.Ci,
        "staging" => Profile.Staging,
        "prod" or "production" => Profile.Prod,
        _ => Profile.Local
    };
}
```

**Port Conflict Resolution:**
```csharp
// Non-production: skip conflicting services with localhost fallback
var conflicts = FindConflictingPorts(hostPorts).ToHashSet();
foreach (var s in plan.Services)
{
    if (s.Ports.Any(p => conflicts.Contains(p.Host))) toSkip.Add(s.Id);
}
// Rewrites app environment: "://mongo:" -> "://localhost:"
```

### Developer Experience Features

**Project Introspection:**
```bash
Koan inspect --json
{
  "detected": true,
  "project": {"name": "S5.Recs", "profile": "Local"},
  "app": {"ids": ["api"], "ports": [{"host": 5084, "container": 8080}]},
  "services": [
    {"id": "mongo", "type": "database", "health": true}
  ],
  "providers": [
    {"id": "docker", "available": true, "engine": {"version": "24.0.6"}}
  ]
}
```

**Comprehensive Status Reporting:**
```bash
Koan status
provider: docker | engine: 24.0.6
- mongo: running (healthy)
- api: running

endpoints (live):
  => mongo: tcp://localhost:5081
  => api: http://localhost:5084
```

## Service Discovery and Container Networking

### Network Architecture

**Dual-Network Strategy for Secure Service Communication:**
```yaml
networks:
  Koan_internal:
    internal: true    # No external access
  Koan_external: {}   # Internet access

services:
  api:
    networks:
      - Koan_internal  # Can reach backing services
      - Koan_external  # Can reach internet
  mongo:
    networks:
      - Koan_internal  # Isolated from internet
```

### Service Discovery Patterns

**DNS-Based Internal Resolution:**
```csharp
// Services reference each other by container name
var appEnv = new Dictionary<string, string?>
{
    ["ConnectionStrings__Default"] = "mongodb://mongo:27017/appdb",
    ["VectorStore__Endpoint"] = "http://weaviate:8080",
    ["AI__Endpoint"] = "http://ollama:11434"
};
```

**Token-Based Dependency Resolution:**
```csharp
// Automatic dependency graph construction
var providersByToken = new Dictionary<string, List<string>>();
foreach (var r in draft.Services)
{
    foreach (var token in r.Provides)
    {
        providersByToken[token] = r.Id;
    }
}

// Generate depends_on relationships
foreach (var token in r.Consumes)
{
    if (providersByToken.TryGetValue(token, out var provs))
        depends.AddRange(provs);
}
```

### Dynamic Port Binding and Discovery

**Real-time Port Discovery:**
```csharp
public async Task<IReadOnlyList<PortBinding>> LivePorts(CancellationToken ct = default)
{
    var (code, outText, _) = await Run("docker", "compose ps --format json", ct);
    return ParseComposePsPorts(outText); // Parses: "0.0.0.0:8080->80/tcp"
}
```

## Deployment and Scaling Patterns

### Environment-Specific Deployment Strategies

**Profile-Based Configuration:**
```csharp
public enum Profile
{
    Local,    // Development with bind mounts, exposed ports
    Ci,       // Named volumes, isolated testing
    Staging,  // Production-like with staging overrides
    Prod      // Minimal surface area, no auto-mounts
}
```

**Health Check Strategies:**
```csharp
// Multi-layer health checking
var test = $"(curl -fsS {endpoint} || wget -q -O- {endpoint} || bash -lc 'exec 3<>/dev/tcp/{host}/{port}') >/dev/null 2>&1 || exit 1";

healthcheck:
  test: ["CMD-SHELL", "health-check-command"]
  interval: 30s
  timeout: 10s
  retries: 3
```

**Dependency-Aware Startup:**
```yaml
# Generated dependencies with health awareness
depends_on:
  mongo:
    condition: service_healthy  # Wait for health check
  weaviate:
    condition: service_started  # No health check defined
```

### Scaling and Resource Management

**Intelligent Port Allocation:**
```csharp
// Deterministic port assignment prevents conflicts across environments
int DeterministicFor(string serviceId)
{
    var key = cwd + ":" + serviceId;
    var seed = Fnv1a32(key);  // FNV-1a hash for consistency
    var basePort = 30000 + (int)(seed % 20001);
    return PickAvailable(basePort);
}
```

**Conflict-Aware Deployment:**
```csharp
// Non-production: skip conflicting services with localhost fallback
if (profile != Profile.Prod && conflicts.Count > 0)
{
    var newPlan = Planner.ApplyPortConflictSkip(plan, profile, out skipped);
    // Rewrites app config: "://mongo:" -> "://localhost:"
    Console.WriteLine($"Skipping services with port conflicts: {string.Join(", ", skipped)}");
}
```

## Configuration Management and Environment Handling

### Multi-Layer Configuration System

**Configuration Precedence:**
1. CLI Flags (--port, --profile)
2. Environment Variables (Koan_ENV)
3. Launch Manifest (.Koan/manifest.json)
4. Generated Defaults (attributes)
5. Deterministic Fallbacks

**Launch Manifest Persistence:**
```csharp
public sealed class LaunchManifest
{
    public sealed class Model
    {
        public App App { get; set; } = new();
        public Dictionary<string, Allocation> Allocations { get; set; } = new();
    }
}
```

**Override System:**
```json
// .Koan/overrides.json
{
  "mode": "Container",
  "services": {
    "mongo": {
      "image": "mongo:7-custom",
      "env": {"CUSTOM_CONFIG": "value"},
      "ports": [27018]
    }
  }
}
```

### Environment Variable Management

**Profile-Aware Environment Injection:**
```csharp
var appEnv = new Dictionary<string, string?>
{
    ["ASPNETCORE_ENVIRONMENT"] = profile == Profile.Local ? "Development" : null,
    ["ASPNETCORE_URLS"] = $"http://+:{draft.AppHttpPort}"
};

// Apply service-specific app environment tokens
foreach (var r in draft.Services)
{
    foreach (var kv in r.AppEnv)
    {
        var val = kv.Value?.Replace("{serviceId}", r.Id)
                           .Replace("{port}", port.ToString())
                           .Replace("{scheme}", scheme)
                           .Replace("{host}", r.Id);
        appEnv[kv.Key] = val;
    }
}
```

## Integration with Koan Framework Components

### Framework-Wide Orchestration Integration

**Data Layer Integration:**
```csharp
// Auto-discovery of data providers through assembly scanning
var assemblies = AppDomain.CurrentDomain.GetAssemblies()
    .Where(a => a.GetName().Name?.StartsWith("Koan.", StringComparison.OrdinalIgnoreCase) == true);

foreach (var t in types.Where(t => t.GetCustomAttributes<KoanServiceAttribute>().Any()))
{
    var serviceAttr = t.GetCustomAttribute<KoanServiceAttribute>();
    candidates.Add(new ServiceCandidate(serviceAttr.ShortCode, serviceAttr.ContainerImage, ...));
}
```

**Authentication System Integration:**
```csharp
// Assembly-level auth provider discovery
foreach (var a in asm.GetAttributes().Where(attr =>
    attr.AttributeClass?.ToDisplayString() == "Koan.Web.Auth.Attributes.AuthProviderDescriptorAttribute"))
{
    var id = a.ConstructorArguments[0].Value?.ToString();
    var name = a.ConstructorArguments[1].Value?.ToString();
    authProviders.Add(new AuthProviderCandidate(id, name, protocol, icon));
}
```

**Zero-Configuration Service Resolution:**
```csharp
// Framework services automatically register orchestration metadata
[assembly: OrchestrationServiceManifest("mongo", Type = ServiceType.Database)]
[assembly: OrchestrationServiceManifest("weaviate", Type = ServiceType.Vector)]

// Planner automatically resolves dependencies
var draft = ProjectDependencyAnalyzer.DiscoverDraft(profile);
var plan = FromDraft(profile, draft);
```

## Module Breakdown

### Container Orchestration (6 modules)
- **Koan.Orchestration.Abstractions** - Container orchestration abstractions and interfaces
- **Koan.Orchestration.Cli** - Command-line orchestration tools and utilities
- **Koan.Orchestration.Generators** - Configuration generators and infrastructure-as-code
- **Koan.Orchestration.Provider.Docker** - Docker container provider implementation
- **Koan.Orchestration.Provider.Podman** - Podman container provider implementation
- **Koan.Orchestration.Renderers.Compose** - Docker Compose renderer and configuration generation

## Key Architectural Advantages

### vs. Docker Compose
- **Intelligence**: Auto-discovery vs. manual YAML configuration
- **Environment Awareness**: Profile-specific behavior vs. static configs
- **Conflict Resolution**: Automatic port management vs. manual debugging
- **Health Integration**: Sophisticated readiness probing vs. basic health checks
- **Developer Experience**: Rich CLI with inspection vs. basic compose commands

### vs. Kubernetes
- **Simplicity**: Single binary vs. complex cluster management
- **Local Development**: Optimized for dev workflows vs. production orchestration
- **Zero Configuration**: Attribute-driven vs. extensive YAML manifests
- **Framework Integration**: Deep Koan ecosystem integration vs. generic container orchestration

### vs. Terraform/Pulumi
- **Application Focused**: Service-centric vs. infrastructure-centric
- **Real-time Adaptation**: Dynamic conflict resolution vs. static planning
- **Developer Workflow**: Local development optimization vs. infrastructure provisioning

## Conclusion

Koan's orchestration provides intelligent, developer-focused container management that works across local development and production deployment. The system combines zero-configuration ease-of-use with enterprise deployment capabilities through manifest-driven discovery, intelligent conflict resolution, and multi-runtime provider abstraction.