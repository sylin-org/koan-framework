# Proposal: Service Mesh Admin Integration

## Overview

Integrate Koan Service Mesh capabilities into Koan.Web.Admin using a hybrid approach that respects the framework's distinction between static configuration (provenance) and runtime state (surface APIs).

## Architecture Decision

### Two-Surface Approach

**1. Services Pillar (Provenance)**
- **What:** Service definitions, capabilities, static configuration
- **When:** Reported at bootstrap via `ServicesAutoRegistrar.Describe()`
- **Where:** Appears in Framework view, pillar accordion, configuration views
- **Pattern:** Same as Data adapters, AI models, etc.

**2. Service Mesh Runtime Surface (API)**
- **What:** Discovered instances, health status, performance metrics
- **When:** Retrieved on-demand via `/api/status/service-mesh`
- **Where:** New "Mesh" view mode in Admin UI
- **Pattern:** Same as Runtime surface (CPU, memory, GC)

## Detailed Design

### 1. Services Pillar Manifest

**File:** `src/Koan.Services/Pillars/ServicesPillarManifest.cs`

```csharp
namespace Koan.Services.Pillars;

public static class ServicesPillarManifest
{
    public const string PillarCode = "services";
    public const string PillarLabel = "Services";
    public const string PillarColorHex = "#22c55e";  // Green (networking/distributed)
    public const string PillarIcon = "🕸️";           // Web/mesh visual

    private static readonly string[] DefaultNamespaces =
    [
        "Koan.Services.",
        "Koan.ServiceMesh.",
        "Koan.ServiceDiscovery."
    ];

    public static void EnsureRegistered()
    {
        var descriptor = new KoanPillarCatalog.PillarDescriptor(
            PillarCode,
            PillarLabel,
            PillarColorHex,
            PillarIcon
        );

        KoanPillarCatalog.RegisterDescriptor(descriptor);

        foreach (var prefix in DefaultNamespaces)
        {
            KoanPillarCatalog.AssociateNamespace(PillarCode, prefix);
        }
    }
}
```

**Call from:** `ServicesAutoRegistrar.Initialize()` before service registration

---

### 2. Enhanced Provenance Reporting

**Update:** `src/Koan.Services/Initialization/ServicesAutoRegistrar.cs`

```csharp
public void Describe(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
{
    module.Describe(ModuleVersion);

    var discoveredServices = DiscoverServices();

    if (!discoveredServices.Any())
        return;

    // Overall summary
    module.SetSetting("Services.Discovered", setting => setting
        .Value(string.Join(", ", discoveredServices.Select(s => s.ServiceId)))
        .Source(ProvenanceSettingSource.Auto));

    // Per-service modules
    foreach (var descriptor in discoveredServices)
    {
        var serviceWriter = ProvenanceRegistry.Instance.RequireModuleWriter(
            ServicesPillarManifest.PillarCode,
            $"Koan.Services.{descriptor.ServiceId.ToPascalCase()}"  // e.g., "Koan.Services.Translation"
        );

        serviceWriter.Describe(descriptor.DisplayName, descriptor.Description);

        // Static configuration
        serviceWriter.SetSetting("ServiceId", setting => setting
            .Value(descriptor.ServiceId)
            .Source(ProvenanceSettingSource.Auto));

        serviceWriter.SetSetting("Port", setting => setting
            .Value(descriptor.Port.ToString())
            .Source(ProvenanceSettingSource.AppSettings)
            .SourceKey($"Koan:Services:{descriptor.ServiceId}:Port"));

        serviceWriter.SetSetting("HeartbeatInterval", setting => setting
            .Value(descriptor.HeartbeatInterval.ToString())
            .Source(ProvenanceSettingSource.AppSettings));

        serviceWriter.SetSetting("StaleThreshold", setting => setting
            .Value(descriptor.StaleThreshold.ToString())
            .Source(ProvenanceSettingSource.AppSettings));

        if (!string.IsNullOrEmpty(descriptor.ContainerImage))
        {
            serviceWriter.SetSetting("ContainerImage", setting => setting
                .Value($"{descriptor.ContainerImage}:{descriptor.DefaultTag}")
                .Source(ProvenanceSettingSource.AppSettings));
        }

        // Capabilities as tools
        foreach (var capability in descriptor.Capabilities)
        {
            serviceWriter.AddTool(
                capability.ToTitleCase(),  // "Translate", "Detect Language"
                $"/api/{descriptor.ServiceId}/{capability}",
                description: $"{descriptor.ServiceId} capability: {capability}",
                capability: $"services.{descriptor.ServiceId}.{capability}"
            );
        }

        // Styling hint
        serviceWriter.Note($"[admin-style] pillar={ServicesPillarManifest.PillarCode} " +
                          $"icon={ServicesPillarManifest.PillarIcon} " +
                          $"color={ServicesPillarManifest.PillarColorHex}");
    }
}
```

**Result in Admin UI:**
- Services pillar appears in pillar accordion (🕸️ green)
- Each service is a module (Translation, OCR, etc.)
- Capabilities shown as tools in module detail view
- Configuration shown in settings view

---

### 3. Runtime Mesh Surface Contracts

**File:** `src/Koan.Web.Admin/Contracts/KoanAdminServiceMeshSurface.cs`

```csharp
namespace Koan.Web.Admin.Contracts;

/// <summary>
/// Runtime state snapshot of the Koan Service Mesh.
/// Complements static service definitions from provenance.
/// </summary>
public sealed record KoanAdminServiceMeshSurface(
    bool Enabled,
    DateTimeOffset CapturedAt,
    string OrchestratorChannel,
    int TotalServicesCount,
    int TotalInstancesCount,
    int HealthyInstancesCount,
    int DegradedInstancesCount,
    int UnhealthyInstancesCount,
    IReadOnlyList<KoanAdminServiceSurface> Services
)
{
    public static readonly KoanAdminServiceMeshSurface Empty = new(
        Enabled: false,
        CapturedAt: DateTimeOffset.UtcNow,
        OrchestratorChannel: string.Empty,
        TotalServicesCount: 0,
        TotalInstancesCount: 0,
        HealthyInstancesCount: 0,
        DegradedInstancesCount: 0,
        UnhealthyInstancesCount: 0,
        Services: Array.Empty<KoanAdminServiceSurface>()
    );
}

public sealed record KoanAdminServiceSurface(
    string ServiceId,
    string DisplayName,
    string? Description,
    string[] Capabilities,
    int InstanceCount,
    ServiceHealthDistribution Health,
    LoadBalancingInfo LoadBalancing,
    TimeSpan? MinResponseTime,
    TimeSpan? MaxResponseTime,
    TimeSpan? AvgResponseTime,
    IReadOnlyList<KoanAdminServiceInstanceSurface> Instances
);

public sealed record LoadBalancingInfo(
    string Policy,  // "RoundRobin", "LeastConnections", "HealthAware", "Random"
    string? Description,  // Human-readable policy description
    IReadOnlyDictionary<string, int>? InstanceDistribution  // InstanceId → request count (if tracked)
);

public sealed record ServiceHealthDistribution(
    int Healthy,
    int Degraded,
    int Unhealthy
);

public sealed record KoanAdminServiceInstanceSurface(
    string InstanceId,
    string HttpEndpoint,
    string Status,  // "Healthy", "Degraded", "Unhealthy"
    DateTime LastSeen,
    TimeSpan TimeSinceLastSeen,
    int ActiveConnections,
    TimeSpan AverageResponseTime,
    string DeploymentMode,  // "InProcess", "Container"
    string? ContainerId,
    string[] Capabilities
);
```

---

### 4. Surface Factory

**File:** `src/Koan.Web.Admin/Infrastructure/KoanAdminServiceMeshSurfaceFactory.cs`

```csharp
namespace Koan.Web.Admin.Infrastructure;

internal static class KoanAdminServiceMeshSurfaceFactory
{
    public static async Task<KoanAdminServiceMeshSurface> CaptureAsync(
        IKoanServiceMesh mesh,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        var capturedAt = DateTimeOffset.UtcNow;
        var serviceIds = mesh.GetDiscoveredServices();

        if (!serviceIds.Any())
        {
            return KoanAdminServiceMeshSurface.Empty with { Enabled = true, CapturedAt = capturedAt };
        }

        var services = new List<KoanAdminServiceSurface>();
        int totalInstances = 0;
        int healthyCount = 0;
        int degradedCount = 0;
        int unhealthyCount = 0;

        foreach (var serviceId in serviceIds)
        {
            var instances = mesh.GetAllInstances(serviceId);
            if (!instances.Any())
                continue;

            var instanceSurfaces = instances.Select(inst =>
            {
                var timeSince = capturedAt - inst.LastSeen;
                totalInstances++;

                switch (inst.Status)
                {
                    case ServiceInstanceStatus.Healthy:
                        healthyCount++;
                        break;
                    case ServiceInstanceStatus.Degraded:
                        degradedCount++;
                        break;
                    case ServiceInstanceStatus.Unhealthy:
                        unhealthyCount++;
                        break;
                }

                return new KoanAdminServiceInstanceSurface(
                    inst.InstanceId,
                    inst.HttpEndpoint,
                    inst.Status.ToString(),
                    inst.LastSeen,
                    timeSince,
                    inst.ActiveConnections,
                    inst.AverageResponseTime,
                    inst.DeploymentMode.ToString(),
                    inst.ContainerId,
                    inst.Capabilities
                );
            }).ToList();

            var health = new ServiceHealthDistribution(
                instanceSurfaces.Count(i => i.Status == "Healthy"),
                instanceSurfaces.Count(i => i.Status == "Degraded"),
                instanceSurfaces.Count(i => i.Status == "Unhealthy")
            );

            var responseTimes = instanceSurfaces
                .Where(i => i.AverageResponseTime > TimeSpan.Zero)
                .Select(i => i.AverageResponseTime)
                .ToList();

            // Try to get descriptor for display name/description
            var descriptor = TryGetServiceDescriptor(serviceProvider, serviceId);

            services.Add(new KoanAdminServiceSurface(
                serviceId,
                descriptor?.DisplayName ?? serviceId.ToTitleCase(),
                descriptor?.Description,
                instances.First().Capabilities,  // All instances have same capabilities
                instanceSurfaces.Count,
                health,
                responseTimes.Any() ? responseTimes.Min() : null,
                responseTimes.Any() ? responseTimes.Max() : null,
                responseTimes.Any() ? TimeSpan.FromTicks((long)responseTimes.Average(t => t.Ticks)) : null,
                instanceSurfaces
            ));
        }

        // Get orchestrator channel from configuration
        var orchestratorChannel = "239.255.42.1:42001";  // Default, could read from config

        return new KoanAdminServiceMeshSurface(
            Enabled: true,
            CapturedAt: capturedAt,
            OrchestratorChannel: orchestratorChannel,
            TotalServicesCount: services.Count,
            TotalInstancesCount: totalInstances,
            HealthyInstancesCount: healthyCount,
            DegradedInstancesCount: degradedCount,
            UnhealthyInstancesCount: unhealthyCount,
            Services: services
        );
    }

    private static KoanServiceDescriptor? TryGetServiceDescriptor(
        IServiceProvider serviceProvider,
        string serviceId)
    {
        // Attempt to resolve descriptor from DI if available
        // This is optional - fallback to serviceId if not found
        try
        {
            // Logic to get descriptor from ServicesAutoRegistrar cache
            return null;  // Placeholder
        }
        catch
        {
            return null;
        }
    }
}
```

---

### 5. API Endpoint

**Update:** `src/Koan.Web.Admin/Controllers/KoanAdminStatusController.cs`

```csharp
[HttpGet("service-mesh")]
[Produces("application/json")]
public async Task<ActionResult<KoanAdminServiceMeshSurface>> GetServiceMesh(CancellationToken cancellationToken)
{
    var snapshot = _features.Current;
    if (!snapshot.Enabled || !snapshot.WebEnabled)
    {
        return NotFound();
    }

    // Check if service mesh is registered
    var serviceMesh = _serviceProvider.GetService<IKoanServiceMesh>();
    if (serviceMesh == null)
    {
        return Ok(KoanAdminServiceMeshSurface.Empty);
    }

    try
    {
        var surface = await KoanAdminServiceMeshSurfaceFactory.CaptureAsync(
            serviceMesh,
            _serviceProvider,
            cancellationToken
        );

        return Ok(surface);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to capture service mesh surface");
        return StatusCode(500, KoanAdminServiceMeshSurface.Empty);
    }
}
```

---

### 6. UI Integration

**Update:** `src/Koan.Web.Admin/wwwroot/app.js`

Add new view mode:

```javascript
// Add to navigation menu
const meshNavItem = `
  <a href="#/mesh" class="nav-item" data-nav="mesh">
    <span class="nav-icon">🕸️</span>
    <span class="nav-label">Mesh</span>
  </a>
`;

// Add mesh view HTML
const meshViewHTML = `
  <div class="view mesh-view" id="mesh-view">
    <div class="view-header">
      <div class="view-title-group">
        <h2 class="view-title">Service Mesh</h2>
        <p class="view-subtitle">Runtime service discovery and health monitoring</p>
      </div>
    </div>

    <section class="panel mesh-overview-panel">
      <header class="panel-header">
        <div>
          <h3>Mesh Overview</h3>
          <p class="panel-subtitle">Orchestrator channel and instance distribution</p>
        </div>
      </header>
      <div class="panel-body" id="mesh-overview-body">
        <!-- Dynamically populated -->
      </div>
    </section>

    <section class="panel services-panel">
      <header class="panel-header">
        <div>
          <h3>Discovered Services</h3>
          <p class="panel-subtitle">All services and instances in the mesh</p>
        </div>
      </header>
      <div class="panel-body" id="services-body">
        <!-- Dynamically populated -->
      </div>
    </section>
  </div>
`;

// Add fetch and render logic
async function loadMeshView() {
    try {
        const response = await fetch(`${baseUrl}/api/status/service-mesh`);
        const meshData = await response.json();

        renderMeshOverview(meshData);
        renderServices(meshData.services);
    } catch (error) {
        console.error('Failed to load mesh data:', error);
    }
}

function renderMeshOverview(meshData) {
    const overview = document.getElementById('mesh-overview-body');
    if (!meshData.enabled) {
        overview.innerHTML = '<p>Service Mesh not enabled</p>';
        return;
    }

    overview.innerHTML = `
        <div class="mesh-stats-grid">
            <div class="stat-card">
                <div class="stat-label">Orchestrator Channel</div>
                <div class="stat-value">${meshData.orchestratorChannel}</div>
            </div>
            <div class="stat-card">
                <div class="stat-label">Services</div>
                <div class="stat-value">${meshData.totalServicesCount}</div>
            </div>
            <div class="stat-card">
                <div class="stat-label">Instances</div>
                <div class="stat-value">${meshData.totalInstancesCount}</div>
            </div>
            <div class="stat-card stat-success">
                <div class="stat-label">Healthy</div>
                <div class="stat-value">${meshData.healthyInstancesCount}</div>
            </div>
            <div class="stat-card stat-warning">
                <div class="stat-label">Degraded</div>
                <div class="stat-value">${meshData.degradedInstancesCount}</div>
            </div>
            <div class="stat-card stat-danger">
                <div class="stat-label">Unhealthy</div>
                <div class="stat-value">${meshData.unhealthyInstancesCount}</div>
            </div>
        </div>
    `;
}

function renderServices(services) {
    const container = document.getElementById('services-body');

    if (!services || services.length === 0) {
        container.innerHTML = '<p>No services discovered</p>';
        return;
    }

    container.innerHTML = services.map(service => `
        <details class="service-accordion" open>
            <summary class="service-summary">
                <span class="service-icon">🕸️</span>
                <span class="service-name">${service.displayName}</span>
                <span class="service-meta">
                    ${service.instanceCount} instance${service.instanceCount !== 1 ? 's' : ''}
                    | ${service.health.healthy} healthy
                    ${service.health.degraded > 0 ? `| ${service.health.degraded} degraded` : ''}
                    ${service.health.unhealthy > 0 ? `| ${service.health.unhealthy} unhealthy` : ''}
                </span>
            </summary>
            <div class="service-content">
                ${service.description ? `<p class="service-description">${service.description}</p>` : ''}

                <div class="capabilities-list">
                    <strong>Capabilities:</strong> ${service.capabilities.join(', ')}
                </div>

                <div class="load-balancing-info">
                    <strong>Load Balancing:</strong>
                    <span class="policy-badge">${service.loadBalancing.policy}</span>
                    ${service.loadBalancing.description ? `<span class="policy-desc">${service.loadBalancing.description}</span>` : ''}
                </div>

                ${service.avgResponseTime ? `
                    <div class="performance-stats">
                        <span>Avg Response: ${formatDuration(service.avgResponseTime)}</span>
                        <span>Min: ${formatDuration(service.minResponseTime)}</span>
                        <span>Max: ${formatDuration(service.maxResponseTime)}</span>
                    </div>
                ` : ''}

                <div class="instances-table">
                    <table>
                        <thead>
                            <tr>
                                <th>Instance ID</th>
                                <th>Endpoint</th>
                                <th>Status</th>
                                <th>Last Seen</th>
                                <th>Connections</th>
                                <th>Avg Response</th>
                                <th>Mode</th>
                            </tr>
                        </thead>
                        <tbody>
                            ${service.instances.map(inst => `
                                <tr class="instance-row instance-${inst.status.toLowerCase()}">
                                    <td><code>${inst.instanceId.substring(0, 8)}...</code></td>
                                    <td><code>${inst.httpEndpoint}</code></td>
                                    <td>
                                        <span class="status-badge status-${inst.status.toLowerCase()}">
                                            ${inst.status}
                                        </span>
                                    </td>
                                    <td>${formatTimeSince(inst.timeSinceLastSeen)}</td>
                                    <td>${inst.activeConnections}</td>
                                    <td>${formatDuration(inst.averageResponseTime)}</td>
                                    <td>
                                        <span class="deployment-mode">${inst.deploymentMode}</span>
                                        ${inst.containerId ? `<br><code class="container-id">${inst.containerId.substring(0, 12)}</code>` : ''}
                                    </td>
                                </tr>
                            `).join('')}
                        </tbody>
                    </table>
                </div>
            </div>
        </details>
    `).join('');
}

function formatDuration(duration) {
    // Parse duration string or TimeSpan
    if (!duration || duration === '00:00:00') return 'N/A';
    // Format as "123ms" or "1.2s"
    return duration;  // Simplified
}

function formatTimeSince(timespan) {
    // Format timespan as "5 seconds ago", "2 minutes ago", etc.
    return timespan;  // Simplified
}
```

---

## Benefits of This Approach

### 1. **Follows Framework Patterns**
- Static config → Provenance (like Data adapters, AI models)
- Runtime state → Surface API (like CPU/memory/GC)
- Pillar system for organization
- Self-reporting via IKoanAutoRegistrar

### 2. **Progressive Disclosure**
- Developers see service definitions in Framework view
- Operators see runtime mesh state in Mesh view
- Configuration view shows all service settings
- No information overload

### 3. **Extensible**
- Easy to add more runtime metrics later
- Could add service dependency graph
- Could add request tracing
- Could add cost aggregation

### 4. **Zero Disruption**
- No changes to existing pillars
- Mesh data is additive
- Works whether mesh is enabled or not

### 5. **Consistent UX**
- Mesh feels native to Admin UI
- Same navigation patterns
- Same visual styling
- Same data presentation

---

## Implementation Phases

### Phase 1: Static Service Catalog (Week 1)
- [ ] Create ServicesPillarManifest
- [ ] Call EnsureRegistered() from ServicesAutoRegistrar
- [ ] Enhance Describe() to create per-service modules
- [ ] Report capabilities as ProvenanceTools
- [ ] Test in S8.PolyglotShop
- ✅ Result: Services appear in Framework view, pillar accordion

### Phase 2: Runtime Mesh Surface (Week 2)
- [ ] Create KoanAdminServiceMeshSurface contracts
- [ ] Implement KoanAdminServiceMeshSurfaceFactory
- [ ] Add GET /api/status/service-mesh endpoint
- [ ] Test with Translation service
- ✅ Result: API returns mesh state

### Phase 3: UI Visualization (Week 3)
- [ ] Add Mesh view mode to Admin UI
- [ ] Implement mesh overview panel
- [ ] Implement service/instance table
- [ ] Add health status styling
- [ ] Add auto-refresh capability
- ✅ Result: Full mesh observability in UI

### Phase 4: Polish & Advanced Features (Week 4)
- [ ] Add capability → endpoint mapping view
- [ ] Add discovery event history
- [ ] Add load balancing insights
- [ ] Performance testing
- [ ] Documentation

---

## Success Criteria

1. ✅ Services appear in pillar accordion with proper styling
2. ✅ Capabilities visible as tools in module view
3. ✅ Mesh runtime state accessible via API
4. ✅ UI shows real-time instance health
5. ✅ Consistent with other pillars (Data, AI, Web)
6. ✅ Zero configuration required (auto-discovery)
7. ✅ Works in both in-process and containerized scenarios

---

## Scope Decisions

1. **Service dependencies** - ❌ Out of scope
   - Not tracking which services invoke which capabilities
   - No dependency graph visualization

2. **Capability invocation from Admin UI** - ❌ Out of scope
   - Tools are view-only (no clickable invocation)
   - Admin is for observability, not orchestration

3. **Load balancing policy visibility** - ✅ In scope
   - Show which policy is active per service (RoundRobin, LeastConnections, etc.)
   - Show instance distribution statistics
   - Display policy effectiveness metrics
   - View-only (no policy changes from UI)

4. **Distributed tracing integration** - ⏸️ Deferred
   - Not in initial implementation
   - Could be added later if needed

---

## Files to Create/Modify

### New Files
- `src/Koan.Services/Pillars/ServicesPillarManifest.cs`
- `src/Koan.Web.Admin/Contracts/KoanAdminServiceMeshSurface.cs`
- `src/Koan.Web.Admin/Infrastructure/KoanAdminServiceMeshSurfaceFactory.cs`

### Modified Files
- `src/Koan.Services/Initialization/ServicesAutoRegistrar.cs` (enhanced Describe())
- `src/Koan.Web.Admin/Controllers/KoanAdminStatusController.cs` (new endpoint)
- `src/Koan.Web.Admin/wwwroot/app.js` (new Mesh view)
- `src/Koan.Web.Admin/wwwroot/index.html` (add Mesh nav item)
- `src/Koan.Web.Admin/wwwroot/styles.css` (mesh-specific styles)

---

## Conclusion

This hybrid approach provides:
- **Immediate value**: Static service catalog visible in existing views
- **Enhanced observability**: Runtime mesh state for operational monitoring
- **Framework consistency**: Follows established patterns
- **Future extensibility**: Foundation for advanced features

The key insight is recognizing that Service Mesh has **both static and dynamic aspects**, just like the Data pillar (adapter definitions vs query execution) or AI pillar (model registration vs usage stats). We respect this distinction by using the appropriate framework mechanism for each aspect.
