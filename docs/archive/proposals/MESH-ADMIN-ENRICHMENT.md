# Service Mesh Admin UI Enrichment Proposal

**Status:** Proposed
**Date:** 2025-11-01
**Author:** System Analysis
**Related:** MESH-ADMIN-INTEGRATION.md

## Executive Summary

The current Service Mesh Admin UI implementation (completed in MESH-ADMIN-INTEGRATION.md) provides basic runtime visibility but lacks critical configuration details and operational metrics needed for effective mesh monitoring and debugging. This proposal outlines a phased enrichment strategy to transform the "bare bones" UI into an operator-ready dashboard.

**Current State:** Displays basic instance counts, health status, and minimal instance details.
**Proposed State:** Comprehensive mesh observability with configuration visibility, operational metrics, and temporal context.

---

## Problem Statement

### Current Limitations

**1. Missing Configuration Visibility**
- Orchestrator channel is hardcoded (`239.255.42.1:42001`) instead of reading from mesh
- Load balancing policy is hardcoded (`RoundRobin`) instead of from descriptor
- Service configuration not displayed (ports, endpoints, intervals, thresholds)
- Service channel endpoints (Tier 2 multicast) not shown
- Container deployment details missing
- .NET service type not exposed

**2. Limited Operational Metrics**
- No capacity/utilization metrics
- No mesh-wide health percentages or trends
- No instance comparison or sorting capabilities
- No absolute timestamps (only relative "X seconds ago")
- No heartbeat or uptime information

**3. Lack of Temporal Context**
- No instance lifecycle tracking (when joined, uptime)
- No health change history
- No response time trends
- No heartbeat statistics (received, missed)

### Impact on Operators

- **Debugging is difficult:** Can't see critical configuration that affects behavior
- **Monitoring is limited:** No trending or historical context
- **Capacity planning is impossible:** No visibility into utilization
- **Troubleshooting is manual:** Must correlate data from logs and code

---

## Available Data Analysis

### Data Currently Available (from exploration)

**ServiceInstance fields:**
- InstanceId, HttpEndpoint, Status, LastSeen, ActiveConnections, AverageResponseTime
- DeploymentMode, ContainerId, Capabilities
- **ServiceChannelEndpoint** ⚠️ NOT displayed
- Raw DateTime and TimeSpan values ⚠️ Only formatted versions shown

**KoanServiceDescriptor fields:**
- ServiceId, DisplayName, Description, ServiceType
- **Port, HealthEndpoint, ManifestEndpoint** ⚠️ NOT displayed
- **HeartbeatInterval, StaleThreshold** ⚠️ NOT displayed
- **OrchestratorMulticastGroup, OrchestratorMulticastPort** ⚠️ Hardcoded instead
- **EnableServiceChannel, ServiceMulticastGroup, ServiceMulticastPort** ⚠️ NOT displayed
- **ContainerImage, DefaultTag** ⚠️ NOT displayed

**IKoanServiceMesh methods:**
- GetDiscoveredServices(), GetAllInstances(serviceId)
- **GetSelfInstanceId()** ⚠️ NOT exposed in API
- Internal round-robin state ⚠️ NOT exposed

**Computed metrics (possible but not implemented):**
- Health percentages, capacity utilization
- Total connections across mesh
- Request distribution variance
- Geographic/network distribution from IPs

---

## Proposed Solution

### Three-Phase Enhancement Strategy

**Phase 1: Configuration Visibility** (Quick Wins - 2-3 hours)
- Add mesh configuration panel
- Expose service configuration details
- Fix hardcoded values with actual data
- Show container deployment information

**Phase 2: Operational Metrics** (Medium Effort - 3-4 hours)
- Add health distribution visualizations
- Show capacity and load metrics
- Add instance comparison/sorting
- Display absolute timestamps

**Phase 3: Temporal Tracking** (Higher Effort - 6-8 hours)
- Track instance lifecycle events
- Display uptime and health history
- Show heartbeat statistics
- Add response time trends

---

## Detailed Implementation Specifications

### Phase 1: Configuration Visibility

#### 1.1 Mesh Configuration Panel

**Location:** Top of Service Mesh view, above overview stats

**UI Structure:**
```html
<section class="panel mesh-config-panel">
  <header class="panel-header">
    <div>
      <h3>Mesh Configuration</h3>
      <p class="panel-subtitle">Global mesh settings and discovery</p>
    </div>
    <button class="collapse-btn" data-target="mesh-config-body">
      <span class="collapse-icon">▼</span>
    </button>
  </header>
  <div class="panel-body collapsible" id="mesh-config-body">
    <div class="config-grid">
      <div class="config-item">
        <span class="config-label">Orchestrator Channel</span>
        <span class="config-value">{orchestratorChannel}</span>
      </div>
      <div class="config-item">
        <span class="config-label">Heartbeat Interval</span>
        <span class="config-value">{globalHeartbeat}</span>
      </div>
      <div class="config-item">
        <span class="config-label">Stale Threshold</span>
        <span class="config-value">{globalStaleThreshold}</span>
      </div>
      <div class="config-item">
        <span class="config-label">Self Instance</span>
        <span class="config-value mono">{selfInstanceId}</span>
      </div>
    </div>
  </div>
</section>
```

**CSS Additions:**
```css
.mesh-config-panel {
  margin-bottom: 1.5rem;
}

.config-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(250px, 1fr));
  gap: 1rem;
}

.config-item {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.config-label {
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.config-value {
  font-size: 0.95rem;
  font-weight: 600;
  color: var(--text-primary);
}

.config-value.mono {
  font-family: var(--font-mono);
}

.collapse-btn {
  background: none;
  border: none;
  cursor: pointer;
  padding: 0.5rem;
  color: var(--text-secondary);
  transition: transform 0.2s ease;
}

.collapse-btn.collapsed .collapse-icon {
  transform: rotate(-90deg);
}

.collapsible {
  max-height: 500px;
  overflow: hidden;
  transition: max-height 0.3s ease;
}

.collapsible.collapsed {
  max-height: 0;
}
```

**Data Structure Changes:**

Extend `KoanAdminServiceMeshSurface`:
```csharp
public sealed record KoanAdminServiceMeshSurface(
    bool Enabled,
    DateTimeOffset CapturedAt,
    string OrchestratorChannel,

    // NEW: Add mesh configuration
    string? SelfInstanceId,
    TimeSpan GlobalHeartbeatInterval,
    TimeSpan GlobalStaleThreshold,

    int TotalServicesCount,
    int TotalInstancesCount,
    int HealthyInstancesCount,
    int DegradedInstancesCount,
    int UnhealthyInstancesCount,
    IReadOnlyList<KoanAdminServiceSurface> Services
);
```

**Factory Changes (`KoanAdminServiceMeshSurfaceFactory.cs`):**
```csharp
public static async Task<KoanAdminServiceMeshSurface> CaptureAsync(
    IKoanServiceMesh mesh,
    IServiceProvider serviceProvider,
    CancellationToken cancellationToken)
{
    await Task.CompletedTask;
    var capturedAt = DateTimeOffset.UtcNow;
    var serviceIds = mesh.GetDiscoveredServices();

    // NEW: Get mesh-level configuration from first service descriptor
    var firstDescriptor = TryGetServiceDescriptor(serviceProvider, serviceIds.FirstOrDefault() ?? "");
    var orchestratorChannel = firstDescriptor != null
        ? $"{firstDescriptor.OrchestratorMulticastGroup}:{firstDescriptor.OrchestratorMulticastPort}"
        : "239.255.42.1:42001"; // fallback

    var globalHeartbeat = firstDescriptor?.HeartbeatInterval ?? TimeSpan.FromSeconds(30);
    var globalStaleThreshold = firstDescriptor?.StaleThreshold ?? TimeSpan.FromSeconds(90);

    // NEW: Try to get self instance ID (may require mesh enhancement)
    var selfInstanceId = TryGetSelfInstanceId(mesh);

    // ... existing instance collection logic ...

    return new KoanAdminServiceMeshSurface(
        Enabled: true,
        CapturedAt: capturedAt,
        OrchestratorChannel: orchestratorChannel,

        // NEW: Populate mesh config
        SelfInstanceId: selfInstanceId,
        GlobalHeartbeatInterval: globalHeartbeat,
        GlobalStaleThreshold: globalStaleThreshold,

        TotalServicesCount: services.Count,
        // ... rest of fields ...
    );
}

private static string? TryGetSelfInstanceId(IKoanServiceMesh mesh)
{
    // If IKoanServiceMesh has GetSelfInstanceId(), use it
    // Otherwise, return null or try reflection
    try
    {
        var method = mesh.GetType().GetMethod("GetSelfInstanceId");
        if (method != null)
        {
            return method.Invoke(mesh, null) as string;
        }
    }
    catch { }

    return null;
}
```

**JavaScript Rendering:**
```javascript
function renderMeshConfiguration(meshData) {
  const configPanel = document.getElementById('mesh-config-panel');
  if (!configPanel) return;

  const heartbeat = formatDuration(meshData.globalHeartbeatInterval);
  const staleThreshold = formatDuration(meshData.globalStaleThreshold);
  const selfInstanceId = meshData.selfInstanceId
    ? truncateId(meshData.selfInstanceId)
    : 'N/A';

  configPanel.innerHTML = `
    <div class="config-grid">
      <div class="config-item">
        <span class="config-label">Orchestrator Channel</span>
        <span class="config-value">${escapeHtml(meshData.orchestratorChannel)}</span>
      </div>
      <div class="config-item">
        <span class="config-label">Heartbeat Interval</span>
        <span class="config-value">${heartbeat}</span>
      </div>
      <div class="config-item">
        <span class="config-label">Stale Threshold</span>
        <span class="config-value">${staleThreshold}</span>
      </div>
      <div class="config-item">
        <span class="config-label">Self Instance</span>
        <span class="config-value mono" title="${escapeHtml(meshData.selfInstanceId || '')}">${selfInstanceId}</span>
      </div>
    </div>
  `;
}

function truncateId(id, length = 12) {
  return id.length > length ? id.substring(0, length) + '...' : id;
}
```

#### 1.2 Service Configuration Details

**Location:** Expandable section within each service card

**UI Structure:**
```html
<div class="service-card">
  <div class="service-card-header">
    <!-- existing header content -->
  </div>

  <!-- NEW: Configuration section -->
  <div class="service-config-section">
    <button class="section-toggle" data-target="service-config-{serviceId}">
      <span class="toggle-icon">▶</span>
      <span class="toggle-label">Configuration</span>
    </button>
    <div class="section-content collapsed" id="service-config-{serviceId}">
      <div class="config-subsection">
        <h5>HTTP Configuration</h5>
        <div class="config-row">
          <span class="config-key">Port:</span>
          <span class="config-val">{port}</span>
        </div>
        <div class="config-row">
          <span class="config-key">Health Endpoint:</span>
          <span class="config-val">{healthEndpoint}</span>
        </div>
        <div class="config-row">
          <span class="config-key">Manifest Endpoint:</span>
          <span class="config-val">{manifestEndpoint}</span>
        </div>
      </div>

      <div class="config-subsection">
        <h5>Mesh Configuration</h5>
        <div class="config-row">
          <span class="config-key">Heartbeat Interval:</span>
          <span class="config-val">{heartbeatInterval}</span>
        </div>
        <div class="config-row">
          <span class="config-key">Stale Threshold:</span>
          <span class="config-val">{staleThreshold}</span>
        </div>
        <div class="config-row">
          <span class="config-key">Service Channel:</span>
          <span class="config-val">{serviceChannel}</span>
        </div>
      </div>

      <div class="config-subsection">
        <h5>Deployment</h5>
        <div class="config-row">
          <span class="config-key">Service Type:</span>
          <span class="config-val mono">{serviceType}</span>
        </div>
        <div class="config-row" data-if="containerImage">
          <span class="config-key">Container Image:</span>
          <span class="config-val mono">{containerImage}:{defaultTag}</span>
        </div>
      </div>
    </div>
  </div>

  <!-- existing capabilities, instances, etc. -->
</div>
```

**CSS Additions:**
```css
.service-config-section {
  border-top: 1px solid var(--border-subtle);
  background: var(--surface-sunken);
}

.section-toggle {
  width: 100%;
  padding: 0.75rem 1.5rem;
  background: none;
  border: none;
  display: flex;
  align-items: center;
  gap: 0.5rem;
  cursor: pointer;
  color: var(--text-primary);
  font-weight: 600;
  font-size: 0.875rem;
  transition: background 0.2s ease;
}

.section-toggle:hover {
  background: var(--surface-hover);
}

.toggle-icon {
  display: inline-block;
  transition: transform 0.2s ease;
  font-size: 0.75rem;
}

.section-toggle.expanded .toggle-icon {
  transform: rotate(90deg);
}

.section-content {
  max-height: 0;
  overflow: hidden;
  transition: max-height 0.3s ease;
}

.section-content:not(.collapsed) {
  max-height: 1000px;
  padding: 1rem 1.5rem;
}

.config-subsection {
  margin-bottom: 1.5rem;
}

.config-subsection:last-child {
  margin-bottom: 0;
}

.config-subsection h5 {
  font-size: 0.8rem;
  font-weight: 700;
  color: var(--text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.5px;
  margin: 0 0 0.75rem 0;
}

.config-row {
  display: flex;
  justify-content: space-between;
  padding: 0.5rem 0;
  border-bottom: 1px solid var(--border-subtle);
}

.config-row:last-child {
  border-bottom: none;
}

.config-key {
  font-size: 0.875rem;
  color: var(--text-secondary);
  font-weight: 500;
}

.config-val {
  font-size: 0.875rem;
  color: var(--text-primary);
  font-weight: 600;
  text-align: right;
}

.config-val.mono {
  font-family: var(--font-mono);
  font-size: 0.8rem;
}
```

**Data Structure Changes:**

Extend `KoanAdminServiceSurface`:
```csharp
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
    IReadOnlyList<KoanAdminServiceInstanceSurface> Instances,

    // NEW: Add service configuration
    ServiceConfigurationInfo Configuration
);

public sealed record ServiceConfigurationInfo(
    int Port,
    string HealthEndpoint,
    string ManifestEndpoint,
    TimeSpan HeartbeatInterval,
    TimeSpan StaleThreshold,
    string? ServiceChannel,  // "239.255.42.10:42010" or null if disabled
    string ServiceTypeName,  // "Koan.Services.Translation.TranslationService"
    string? ContainerImage,
    string? DefaultTag
);
```

**Factory Changes:**
```csharp
// In foreach (var serviceId in serviceIds) loop:

var descriptor = TryGetServiceDescriptor(serviceProvider, serviceId);

// Build configuration info
var configuration = new ServiceConfigurationInfo(
    Port: descriptor?.Port ?? 8080,
    HealthEndpoint: descriptor?.HealthEndpoint ?? "/health",
    ManifestEndpoint: descriptor?.ManifestEndpoint ?? "/.well-known/koan-service",
    HeartbeatInterval: descriptor?.HeartbeatInterval ?? TimeSpan.FromSeconds(30),
    StaleThreshold: descriptor?.StaleThreshold ?? TimeSpan.FromSeconds(90),
    ServiceChannel: descriptor?.EnableServiceChannel == true
        ? $"{descriptor.ServiceMulticastGroup}:{descriptor.ServiceMulticastPort}"
        : null,
    ServiceTypeName: descriptor?.ServiceType?.FullName ?? "Unknown",
    ContainerImage: descriptor?.ContainerImage,
    DefaultTag: descriptor?.DefaultTag
);

services.Add(new KoanAdminServiceSurface(
    // ... existing fields ...
    Configuration: configuration
));
```

**JavaScript Rendering:**
```javascript
function renderServiceConfiguration(service) {
  const config = service.configuration;
  const serviceChannel = config.serviceChannel
    ? config.serviceChannel
    : '<span class="disabled">Disabled</span>';

  const containerInfo = config.containerImage
    ? `<div class="config-row">
         <span class="config-key">Container Image:</span>
         <span class="config-val mono">${escapeHtml(config.containerImage)}:${escapeHtml(config.defaultTag || 'latest')}</span>
       </div>`
    : '';

  return `
    <div class="service-config-section">
      <button class="section-toggle" onclick="toggleSection(this, 'service-config-${service.serviceId}')">
        <span class="toggle-icon">▶</span>
        <span class="toggle-label">Configuration</span>
      </button>
      <div class="section-content collapsed" id="service-config-${service.serviceId}">
        <div class="config-subsection">
          <h5>HTTP Configuration</h5>
          <div class="config-row">
            <span class="config-key">Port:</span>
            <span class="config-val">${config.port}</span>
          </div>
          <div class="config-row">
            <span class="config-key">Health Endpoint:</span>
            <span class="config-val">${escapeHtml(config.healthEndpoint)}</span>
          </div>
          <div class="config-row">
            <span class="config-key">Manifest Endpoint:</span>
            <span class="config-val">${escapeHtml(config.manifestEndpoint)}</span>
          </div>
        </div>

        <div class="config-subsection">
          <h5>Mesh Configuration</h5>
          <div class="config-row">
            <span class="config-key">Heartbeat Interval:</span>
            <span class="config-val">${formatDuration(config.heartbeatInterval)}</span>
          </div>
          <div class="config-row">
            <span class="config-key">Stale Threshold:</span>
            <span class="config-val">${formatDuration(config.staleThreshold)}</span>
          </div>
          <div class="config-row">
            <span class="config-key">Service Channel:</span>
            <span class="config-val">${serviceChannel}</span>
          </div>
        </div>

        <div class="config-subsection">
          <h5>Deployment</h5>
          <div class="config-row">
            <span class="config-key">Service Type:</span>
            <span class="config-val mono" title="${escapeHtml(config.serviceTypeName)}">${truncateType(config.serviceTypeName)}</span>
          </div>
          ${containerInfo}
        </div>
      </div>
    </div>
  `;
}

function toggleSection(button, targetId) {
  const content = document.getElementById(targetId);
  const isCollapsed = content.classList.contains('collapsed');

  content.classList.toggle('collapsed');
  button.classList.toggle('expanded');
}

function truncateType(fullName, maxLength = 50) {
  if (fullName.length <= maxLength) return fullName;

  // Try to keep namespace prefix and class name
  const parts = fullName.split('.');
  if (parts.length > 2) {
    return parts[0] + '...' + parts[parts.length - 1];
  }

  return fullName.substring(0, maxLength - 3) + '...';
}
```

#### 1.3 Enhanced Instance Details

**Location:** Within instance rows

**UI Changes:**

Add ServiceChannelEndpoint to instance display:
```html
<div class="instance-row status-{status}">
  <div class="instance-main">
    <div class="instance-status-icon">{statusIcon}</div>
    <div class="instance-info">
      <div class="instance-id">{instanceId}</div>
      <div class="instance-endpoints">
        <div class="endpoint-row">
          <span class="endpoint-label">HTTP:</span>
          <a href="{httpEndpoint}" target="_blank">{httpEndpoint}</a>
        </div>
        <!-- NEW: Service Channel -->
        {if serviceChannelEndpoint}
        <div class="endpoint-row">
          <span class="endpoint-label">Service Channel:</span>
          <span class="endpoint-value">{serviceChannelEndpoint}</span>
        </div>
        {/if}
      </div>
    </div>
  </div>

  <div class="instance-stats">
    <!-- existing stats -->

    <!-- NEW: Absolute timestamp -->
    <div class="instance-stat">
      <span class="stat-label">Last Seen:</span>
      <span class="stat-value" title="{absoluteTime}">{timeSinceLastSeen}</span>
    </div>
  </div>
</div>
```

**CSS Additions:**
```css
.instance-endpoints {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  margin-top: 0.25rem;
}

.endpoint-row {
  display: flex;
  gap: 0.5rem;
  font-size: 0.75rem;
}

.endpoint-label {
  color: var(--text-secondary);
  font-weight: 600;
  min-width: 80px;
}

.endpoint-value {
  color: var(--text-primary);
  font-family: var(--font-mono);
  font-size: 0.7rem;
}
```

**Data Structure Changes:**

Extend `KoanAdminServiceInstanceSurface`:
```csharp
public sealed record KoanAdminServiceInstanceSurface(
    string InstanceId,
    string HttpEndpoint,
    string Status,
    DateTime LastSeen,
    string TimeSinceLastSeen,
    int ActiveConnections,
    string AverageResponseTime,
    string DeploymentMode,
    string? ContainerId,
    string[] Capabilities,

    // NEW: Add service channel and absolute time
    string? ServiceChannelEndpoint,
    DateTimeOffset LastSeenAbsolute
);
```

**Factory Changes:**
```csharp
return new KoanAdminServiceInstanceSurface(
    inst.InstanceId,
    inst.HttpEndpoint,
    inst.Status.ToString(),
    inst.LastSeen,
    FormatTimeSince(timeSince),
    inst.ActiveConnections,
    FormatDuration(inst.AverageResponseTime),
    inst.DeploymentMode.ToString(),
    inst.ContainerId,
    inst.Capabilities,

    // NEW: Include service channel and absolute timestamp
    ServiceChannelEndpoint: inst.ServiceChannelEndpoint,
    LastSeenAbsolute: inst.LastSeen
);
```

**JavaScript Rendering:**
```javascript
function renderServiceInstance(instance) {
  const statusClass = instance.status.toLowerCase();
  const statusIcon = instance.status === 'Healthy' ? '✓' :
                     instance.status === 'Degraded' ? '⚠' : '✗';

  const serviceChannel = instance.serviceChannelEndpoint
    ? `<div class="endpoint-row">
         <span class="endpoint-label">Service Channel:</span>
         <span class="endpoint-value">${escapeHtml(instance.serviceChannelEndpoint)}</span>
       </div>`
    : '';

  const absoluteTime = new Date(instance.lastSeenAbsolute).toLocaleString('en-US', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hour12: false
  });

  return `
    <div class="instance-row status-${statusClass}">
      <div class="instance-main">
        <div class="instance-status-icon">${statusIcon}</div>
        <div class="instance-info">
          <div class="instance-id">${escapeHtml(instance.instanceId)}</div>
          <div class="instance-endpoints">
            <div class="endpoint-row">
              <span class="endpoint-label">HTTP:</span>
              <a href="${escapeHtml(instance.httpEndpoint)}" target="_blank" rel="noopener">
                ${escapeHtml(instance.httpEndpoint)}
              </a>
            </div>
            ${serviceChannel}
          </div>
        </div>
      </div>
      <div class="instance-stats">
        <div class="instance-stat">
          <span class="stat-label">Last Seen:</span>
          <span class="stat-value" title="${absoluteTime}">${escapeHtml(instance.timeSinceLastSeen)}</span>
        </div>
        <div class="instance-stat">
          <span class="stat-label">Connections:</span>
          <span class="stat-value">${instance.activeConnections}</span>
        </div>
        <div class="instance-stat">
          <span class="stat-label">Response:</span>
          <span class="stat-value">${escapeHtml(instance.averageResponseTime)}</span>
        </div>
        <div class="instance-stat">
          <span class="stat-label">Mode:</span>
          <span class="stat-value">${escapeHtml(instance.deploymentMode)}</span>
        </div>
        ${instance.containerId ? `
        <div class="instance-stat">
          <span class="stat-label">Container:</span>
          <span class="stat-value" title="${escapeHtml(instance.containerId)}">${escapeHtml(instance.containerId.substring(0, 12))}</span>
        </div>
        ` : ''}
      </div>
    </div>
  `;
}
```

#### 1.4 Fix Hardcoded Load Balancing Policy

**Current Issue:** Factory has `// TODO: Get actual policy from service descriptor or configuration`

**Solution:**

Add to `KoanServiceDescriptor`:
```csharp
public class KoanServiceDescriptor
{
    // ... existing fields ...

    public LoadBalancingPolicy Policy { get; set; } = LoadBalancingPolicy.RoundRobin;
}

public enum LoadBalancingPolicy
{
    RoundRobin,
    LeastConnections,
    Random,
    HealthAware
}
```

Or if policy is determined by mesh logic, expose it via a method:
```csharp
// In IKoanServiceMesh
LoadBalancingPolicy GetLoadBalancingPolicy(string serviceId);
```

Update factory:
```csharp
var loadBalancingPolicy = descriptor?.Policy ?? LoadBalancingPolicy.RoundRobin;
var loadBalancing = new LoadBalancingInfo(
    loadBalancingPolicy.ToString(),
    GetPolicyDescription(loadBalancingPolicy)
);

static string GetPolicyDescription(LoadBalancingPolicy policy) => policy switch
{
    LoadBalancingPolicy.RoundRobin => "Distributes requests evenly across healthy instances",
    LoadBalancingPolicy.LeastConnections => "Routes to instance with fewest active connections",
    LoadBalancingPolicy.Random => "Randomly selects from healthy instances",
    LoadBalancingPolicy.HealthAware => "Prefers healthy instances, falls back to degraded",
    _ => "Unknown policy"
};
```

---

### Phase 2: Operational Metrics

#### 2.1 Health Distribution Visualizations

**Location:** Within service card, enhance health badges

**UI Structure:**
```html
<div class="service-health-section">
  <div class="health-distribution">
    <div class="health-bar-container">
      <div class="health-bar">
        <div class="health-segment healthy" style="width: {healthyPercent}%"
             title="{healthy} healthy ({healthyPercent}%)">
        </div>
        <div class="health-segment degraded" style="width: {degradedPercent}%"
             title="{degraded} degraded ({degradedPercent}%)">
        </div>
        <div class="health-segment unhealthy" style="width: {unhealthyPercent}%"
             title="{unhealthy} unhealthy ({unhealthyPercent}%)">
        </div>
      </div>
    </div>
    <div class="health-legend">
      <span class="legend-item healthy">
        <span class="legend-dot"></span>
        {healthy} Healthy ({healthyPercent}%)
      </span>
      <span class="legend-item degraded">
        <span class="legend-dot"></span>
        {degraded} Degraded ({degradedPercent}%)
      </span>
      <span class="legend-item unhealthy">
        <span class="legend-dot"></span>
        {unhealthy} Unhealthy ({unhealthyPercent}%)
      </span>
    </div>
  </div>
</div>
```

**CSS:**
```css
.health-distribution {
  padding: 1rem 1.5rem;
  background: var(--surface-sunken);
  border-bottom: 1px solid var(--border-subtle);
}

.health-bar-container {
  margin-bottom: 0.75rem;
}

.health-bar {
  display: flex;
  height: 24px;
  border-radius: 12px;
  overflow: hidden;
  background: var(--surface-base);
}

.health-segment {
  transition: width 0.3s ease;
  cursor: help;
}

.health-segment.healthy {
  background: linear-gradient(135deg, #22c55e 0%, #16a34a 100%);
}

.health-segment.degraded {
  background: linear-gradient(135deg, #facc15 0%, #eab308 100%);
}

.health-segment.unhealthy {
  background: linear-gradient(135deg, #ef4444 0%, #dc2626 100%);
}

.health-legend {
  display: flex;
  gap: 1.5rem;
  flex-wrap: wrap;
  font-size: 0.8rem;
}

.legend-item {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  color: var(--text-secondary);
}

.legend-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
}

.legend-item.healthy .legend-dot {
  background: #22c55e;
}

.legend-item.degraded .legend-dot {
  background: #facc15;
}

.legend-item.unhealthy .legend-dot {
  background: #ef4444;
}
```

**JavaScript:**
```javascript
function renderHealthDistribution(health, instanceCount) {
  const total = instanceCount || 1;
  const healthyPercent = Math.round((health.healthy / total) * 100);
  const degradedPercent = Math.round((health.degraded / total) * 100);
  const unhealthyPercent = Math.round((health.unhealthy / total) * 100);

  return `
    <div class="health-distribution">
      <div class="health-bar-container">
        <div class="health-bar">
          ${health.healthy > 0 ? `
            <div class="health-segment healthy"
                 style="width: ${healthyPercent}%"
                 title="${health.healthy} healthy (${healthyPercent}%)">
            </div>
          ` : ''}
          ${health.degraded > 0 ? `
            <div class="health-segment degraded"
                 style="width: ${degradedPercent}%"
                 title="${health.degraded} degraded (${degradedPercent}%)">
            </div>
          ` : ''}
          ${health.unhealthy > 0 ? `
            <div class="health-segment unhealthy"
                 style="width: ${unhealthyPercent}%"
                 title="${health.unhealthy} unhealthy (${unhealthyPercent}%)">
            </div>
          ` : ''}
        </div>
      </div>
      <div class="health-legend">
        <span class="legend-item healthy">
          <span class="legend-dot"></span>
          ${health.healthy} Healthy (${healthyPercent}%)
        </span>
        <span class="legend-item degraded">
          <span class="legend-dot"></span>
          ${health.degraded} Degraded (${degradedPercent}%)
        </span>
        <span class="legend-item unhealthy">
          <span class="legend-dot"></span>
          ${health.unhealthy} Unhealthy (${unhealthyPercent}%)
        </span>
      </div>
    </div>
  `;
}
```

#### 2.2 Capacity & Load Metrics

**Location:** New panel below mesh configuration

**UI Structure:**
```html
<section class="panel capacity-panel">
  <header class="panel-header">
    <div>
      <h3>Capacity Overview</h3>
      <p class="panel-subtitle">Mesh-wide resource utilization</p>
    </div>
  </header>
  <div class="panel-body">
    <div class="capacity-metrics">
      <div class="capacity-metric">
        <div class="metric-header">
          <span class="metric-label">Total Connections</span>
          <span class="metric-value">{totalConnections} / {maxConnections}</span>
        </div>
        <div class="metric-bar">
          <div class="metric-fill" style="width: {connectionPercent}%"></div>
        </div>
        <div class="metric-footer">{connectionPercent}% utilized</div>
      </div>

      <div class="capacity-metric">
        <div class="metric-header">
          <span class="metric-label">Average Response Time</span>
          <span class="metric-value">{avgResponseTime}</span>
        </div>
        <div class="metric-indicator {responseClass}">
          {responseIndicator}
        </div>
      </div>

      <div class="capacity-metric">
        <div class="metric-header">
          <span class="metric-label">Instance Distribution</span>
          <span class="metric-value">{distributionQuality}</span>
        </div>
        <div class="metric-footer">{distributionDescription}</div>
      </div>
    </div>
  </div>
</section>
```

**Data Computation:**

Add to factory:
```csharp
// Compute mesh-wide metrics
var totalConnections = services.Sum(s => s.Instances.Sum(i => i.ActiveConnections));
var maxConnections = services.Sum(s => s.InstanceCount * 20); // Assume 20 per instance

var allResponseTimes = services
    .SelectMany(s => s.Instances)
    .Select(i => ParseDuration(i.AverageResponseTime))
    .Where(t => t.HasValue)
    .Select(t => t!.Value)
    .ToList();

var meshAvgResponseTime = allResponseTimes.Any()
    ? TimeSpan.FromTicks((long)allResponseTimes.Average(t => t.Ticks))
    : TimeSpan.Zero;

// Add to surface
var capacityInfo = new MeshCapacityInfo(
    TotalConnections: totalConnections,
    MaxConnections: maxConnections,
    AverageResponseTime: meshAvgResponseTime
);
```

#### 2.3 Instance Comparison Table

**Location:** Alternative view mode for instances (tab/toggle)

**UI Structure:**
```html
<div class="instances-view-selector">
  <button class="view-btn active" data-view="cards">Cards</button>
  <button class="view-btn" data-view="table">Table</button>
</div>

<div class="instances-view cards active" id="instances-cards">
  <!-- Existing card-based instance rows -->
</div>

<div class="instances-view table" id="instances-table">
  <table class="instances-table">
    <thead>
      <tr>
        <th class="sortable" data-sort="status">Status</th>
        <th class="sortable" data-sort="instanceId">Instance</th>
        <th class="sortable" data-sort="endpoint">Endpoint</th>
        <th class="sortable" data-sort="connections">Connections</th>
        <th class="sortable" data-sort="responseTime">Response</th>
        <th class="sortable" data-sort="lastSeen">Last Seen</th>
        <th>Mode</th>
      </tr>
    </thead>
    <tbody>
      <!-- Sortable rows -->
    </tbody>
  </table>
</div>
```

**JavaScript:**
```javascript
function renderInstancesTable(instances) {
  return instances.map(inst => `
    <tr class="instance-table-row status-${inst.status.toLowerCase()}">
      <td class="status-cell">
        <span class="status-indicator ${inst.status.toLowerCase()}">${getStatusIcon(inst.status)}</span>
      </td>
      <td class="instance-cell">
        <span class="mono" title="${escapeHtml(inst.instanceId)}">${truncateId(inst.instanceId)}</span>
      </td>
      <td class="endpoint-cell">
        <a href="${escapeHtml(inst.httpEndpoint)}" target="_blank">${escapeHtml(inst.httpEndpoint)}</a>
      </td>
      <td class="connections-cell" data-value="${inst.activeConnections}">
        ${inst.activeConnections}
      </td>
      <td class="response-cell" data-value="${parseResponseTime(inst.averageResponseTime)}">
        ${escapeHtml(inst.averageResponseTime)}
      </td>
      <td class="lastseen-cell" data-value="${new Date(inst.lastSeenAbsolute).getTime()}">
        ${escapeHtml(inst.timeSinceLastSeen)}
      </td>
      <td class="mode-cell">
        ${escapeHtml(inst.deploymentMode)}
      </td>
    </tr>
  `).join('');
}

function setupTableSorting() {
  document.querySelectorAll('.instances-table th.sortable').forEach(th => {
    th.addEventListener('click', () => {
      const sortKey = th.dataset.sort;
      sortInstancesTable(sortKey);
    });
  });
}

function sortInstancesTable(key) {
  // Implementation of table sorting logic
  const tbody = document.querySelector('.instances-table tbody');
  const rows = Array.from(tbody.querySelectorAll('tr'));

  rows.sort((a, b) => {
    const aVal = a.querySelector(`[data-value]`)?.dataset.value;
    const bVal = b.querySelector(`[data-value]`)?.dataset.value;
    // Sort logic based on key
  });

  rows.forEach(row => tbody.appendChild(row));
}
```

---

### Phase 3: Temporal Tracking

**Note:** This phase requires backend enhancements to store historical data.

#### 3.1 Instance Lifecycle Tracking

**Backend Addition:**

Create a new service for tracking events:
```csharp
public interface IMeshEventTracker
{
    void RecordInstanceJoined(string serviceId, string instanceId, DateTime timestamp);
    void RecordInstanceLeft(string serviceId, string instanceId, DateTime timestamp);
    void RecordHealthChange(string serviceId, string instanceId, ServiceInstanceStatus oldStatus, ServiceInstanceStatus newStatus, DateTime timestamp);

    IEnumerable<MeshEvent> GetInstanceHistory(string instanceId, TimeSpan window);
    TimeSpan GetInstanceUptime(string instanceId);
}

public record MeshEvent(
    string ServiceId,
    string InstanceId,
    MeshEventType Type,
    DateTime Timestamp,
    Dictionary<string, string> Metadata
);

public enum MeshEventType
{
    InstanceJoined,
    InstanceLeft,
    HealthChanged,
    HeartbeatMissed
}
```

Integrate with `KoanServiceMesh`:
```csharp
public class KoanServiceMesh : IKoanServiceMesh
{
    private readonly IMeshEventTracker _eventTracker;

    private void ProcessDiscoveredInstance(ServiceInstance instance)
    {
        // Existing logic...

        // NEW: Track if this is a new instance
        if (!_knownInstances.ContainsKey(instance.InstanceId))
        {
            _eventTracker.RecordInstanceJoined(instance.ServiceId, instance.InstanceId, DateTime.UtcNow);
        }

        // Track health changes
        if (_knownInstances.TryGetValue(instance.InstanceId, out var existing))
        {
            if (existing.Status != instance.Status)
            {
                _eventTracker.RecordHealthChange(
                    instance.ServiceId,
                    instance.InstanceId,
                    existing.Status,
                    instance.Status,
                    DateTime.UtcNow
                );
            }
        }
    }
}
```

#### 3.2 Heartbeat Statistics

**Data Structure:**
```csharp
public record InstanceHeartbeatStats(
    int TotalHeartbeats,
    int MissedHeartbeats,
    DateTime LastHeartbeat,
    TimeSpan AverageInterval
);
```

Track in mesh coordinator:
```csharp
private readonly Dictionary<string, InstanceHeartbeatStats> _heartbeatStats = new();

private void TrackHeartbeat(string instanceId)
{
    if (!_heartbeatStats.TryGetValue(instanceId, out var stats))
    {
        stats = new InstanceHeartbeatStats(0, 0, DateTime.UtcNow, TimeSpan.Zero);
    }

    var interval = DateTime.UtcNow - stats.LastHeartbeat;
    var newAvg = TimeSpan.FromTicks(
        ((stats.AverageInterval.Ticks * stats.TotalHeartbeats) + interval.Ticks) / (stats.TotalHeartbeats + 1)
    );

    _heartbeatStats[instanceId] = stats with
    {
        TotalHeartbeats = stats.TotalHeartbeats + 1,
        LastHeartbeat = DateTime.UtcNow,
        AverageInterval = newAvg
    };
}
```

---

## Testing Strategy

### Unit Tests

**Test Cases for Phase 1:**
```csharp
[Fact]
public async Task CaptureAsync_IncludesMeshConfiguration()
{
    // Arrange
    var mesh = CreateMockMesh();
    var serviceProvider = CreateMockServiceProvider();

    // Act
    var surface = await KoanAdminServiceMeshSurfaceFactory.CaptureAsync(mesh, serviceProvider, CancellationToken.None);

    // Assert
    Assert.NotNull(surface.SelfInstanceId);
    Assert.NotEqual(TimeSpan.Zero, surface.GlobalHeartbeatInterval);
    Assert.NotEqual("239.255.42.1:42001", surface.OrchestratorChannel); // Not hardcoded
}

[Fact]
public void ServiceConfigurationInfo_IncludesAllFields()
{
    // Verify ServiceConfigurationInfo has all required fields
}
```

### Integration Tests

**Test Scenarios:**
```csharp
[Fact]
public async Task ServiceMesh_Endpoint_ReturnsEnrichedData()
{
    // Start S8.PolyglotShop
    // Call /api/service-mesh
    // Verify response includes configuration
    var response = await client.GetAsync("/.koan/admin/api/service-mesh");
    var mesh = await response.Content.ReadFromJsonAsync<KoanAdminServiceMeshSurface>();

    Assert.NotNull(mesh.SelfInstanceId);
    Assert.All(mesh.Services, s => Assert.NotNull(s.Configuration));
}
```

### UI Testing

**Manual Test Checklist:**
- [ ] Mesh configuration panel displays correctly
- [ ] Service configuration sections are collapsible
- [ ] Service channel endpoints shown when enabled
- [ ] Absolute timestamps in tooltips
- [ ] Health distribution bars render accurately
- [ ] Capacity metrics calculate correctly
- [ ] Table view sorts properly
- [ ] All tooltips display full values

---

## Success Metrics

### Phase 1 (Configuration Visibility)
- **Before:** 3 data points per service (name, instances, health)
- **After:** 15+ data points per service (config, channels, deployment)
- **Operator Feedback:** "Can now debug mesh issues without diving into code"

### Phase 2 (Operational Metrics)
- **Before:** No capacity visibility
- **After:** Mesh-wide utilization, health trends, sortable views
- **Operator Feedback:** "Can identify overloaded instances at a glance"

### Phase 3 (Temporal Context)
- **Before:** No historical data
- **After:** Uptime, lifecycle events, trends
- **Operator Feedback:** "Can see when problems started and correlate with deployments"

---

## Implementation Checklist

### Phase 1: Configuration Visibility

**Backend (C#):**
- [ ] Add `SelfInstanceId`, `GlobalHeartbeatInterval`, `GlobalStaleThreshold` to `KoanAdminServiceMeshSurface`
- [ ] Create `ServiceConfigurationInfo` record
- [ ] Add `Configuration` property to `KoanAdminServiceSurface`
- [ ] Add `ServiceChannelEndpoint` and `LastSeenAbsolute` to `KoanAdminServiceInstanceSurface`
- [ ] Update `KoanAdminServiceMeshSurfaceFactory.CaptureAsync()`:
  - [ ] Read orchestrator channel from descriptor (remove hardcode)
  - [ ] Populate global heartbeat and stale threshold
  - [ ] Try to get self instance ID from mesh
  - [ ] Build `ServiceConfigurationInfo` for each service
  - [ ] Include service channel endpoint in instances
  - [ ] Add absolute timestamp to instances
- [ ] Fix load balancing policy (remove TODO, get from descriptor or mesh)

**Frontend (HTML/CSS/JS):**
- [ ] Add mesh configuration panel HTML structure to `index.html`
- [ ] Add service configuration section HTML template
- [ ] Add CSS for config panels, collapsible sections
- [ ] Update `app.js`:
  - [ ] Add `renderMeshConfiguration()` function
  - [ ] Add `renderServiceConfiguration()` function
  - [ ] Add `toggleSection()` for collapsible sections
  - [ ] Update `renderServiceInstance()` to show service channel
  - [ ] Add tooltips with absolute timestamps
  - [ ] Add `truncateType()` helper function
- [ ] Test collapsible behavior
- [ ] Test responsive layout

**Testing:**
- [ ] Rebuild S8.PolyglotShop containers
- [ ] Verify mesh config panel shows correct data
- [ ] Verify service config sections collapse/expand
- [ ] Verify service channel shows for translation service
- [ ] Verify absolute timestamps in tooltips
- [ ] Verify load balancing policy is not hardcoded

### Phase 2: Operational Metrics

**Backend:**
- [ ] Add `MeshCapacityInfo` record
- [ ] Update factory to compute capacity metrics
- [ ] Add capacity info to mesh surface

**Frontend:**
- [ ] Add health distribution HTML/CSS
- [ ] Add capacity panel HTML/CSS
- [ ] Add table view HTML/CSS
- [ ] Add `renderHealthDistribution()` function
- [ ] Add `renderCapacityMetrics()` function
- [ ] Add `renderInstancesTable()` function
- [ ] Add table sorting logic
- [ ] Add view toggle (cards/table)

**Testing:**
- [ ] Verify health bars render correctly
- [ ] Verify capacity calculations
- [ ] Verify table sorting
- [ ] Test with multiple services

### Phase 3: Temporal Tracking

**Backend:**
- [ ] Create `IMeshEventTracker` interface
- [ ] Implement `MeshEventTracker` service
- [ ] Create `MeshEvent` record
- [ ] Integrate event tracking into `KoanServiceMesh`
- [ ] Add heartbeat statistics tracking
- [ ] Add event history endpoints

**Frontend:**
- [ ] Add lifecycle events display
- [ ] Add uptime display
- [ ] Add heartbeat stats
- [ ] Add response time trends (charts)

**Testing:**
- [ ] Verify events are tracked
- [ ] Verify uptime calculations
- [ ] Verify historical data persists
- [ ] Test event correlation

---

## Files to Modify

### Backend Files

**Contracts:**
- `src/Koan.Web.Admin/Contracts/KoanAdminServiceMeshSurface.cs`
  - Add mesh config fields
  - Add `ServiceConfigurationInfo` record
  - Update `KoanAdminServiceSurface` with Configuration
  - Update `KoanAdminServiceInstanceSurface` with channel and timestamp

**Factory:**
- `src/Koan.Web.Admin/Infrastructure/KoanAdminServiceMeshSurfaceFactory.cs`
  - Read orchestrator channel from descriptor
  - Populate mesh configuration
  - Build service configuration info
  - Include service channels
  - Fix load balancing policy

**Services (Phase 3):**
- `src/Koan.Services/Tracking/IMeshEventTracker.cs` (new)
- `src/Koan.Services/Tracking/MeshEventTracker.cs` (new)
- `src/Koan.Services/ServiceMesh/KoanServiceMesh.cs` (modify for event tracking)

### Frontend Files

**HTML:**
- `src/Koan.Web.Admin/wwwroot/index.html`
  - Add mesh config panel
  - Add service config sections to template

**JavaScript:**
- `src/Koan.Web.Admin/wwwroot/app.js`
  - Add rendering functions
  - Add toggle/collapse logic
  - Add table sorting
  - Add view switching

**CSS:**
- `src/Koan.Web.Admin/wwwroot/styles.css`
  - Add config panel styles
  - Add collapsible section styles
  - Add health distribution styles
  - Add capacity metrics styles
  - Add table view styles

---

## Usage Instructions for Future Implementation

**To implement this proposal:**

```
Continue implementing the proposal in doc MESH-ADMIN-ENRICHMENT.md

Start with Phase 1: Configuration Visibility
```

The AI assistant should:
1. Read this entire document
2. Start with Phase 1 backend changes
3. Follow the implementation checklist
4. Test each section before moving to the next
5. Commit work in logical chunks (backend contracts, factory changes, UI updates)
6. Verify in running S8.PolyglotShop containers
7. Move to Phase 2 only after Phase 1 is complete and tested

---

## References

- **Original Integration:** `docs/proposals/MESH-ADMIN-INTEGRATION.md`
- **Service Mesh Architecture:** ADR-0042
- **Admin UI Patterns:** Existing Admin dashboard implementation
- **Koan Framework Patterns:** CLAUDE.md framework guidelines

---

## Appendix: Code Snippets Reference

### Utility Functions

```javascript
// Format TimeSpan from C# JSON serialization
function formatDuration(timeSpan) {
  if (!timeSpan) return 'N/A';

  const totalMs = timeSpan.totalMilliseconds || 0;
  if (totalMs < 1) return `${Math.round(totalMs * 1000)}μs`;
  if (totalMs < 1000) return `${Math.round(totalMs)}ms`;
  if (totalMs < 60000) return `${(totalMs / 1000).toFixed(2)}s`;

  const minutes = Math.floor(totalMs / 60000);
  const seconds = Math.round((totalMs % 60000) / 1000);
  return `${minutes}m ${seconds}s`;
}

// Escape HTML to prevent XSS
function escapeHtml(str) {
  if (!str) return '';
  const div = document.createElement('div');
  div.textContent = str;
  return div.innerHTML;
}

// Truncate long IDs
function truncateId(id, length = 12) {
  return id.length > length ? id.substring(0, length) + '...' : id;
}

// Get status icon
function getStatusIcon(status) {
  switch (status) {
    case 'Healthy': return '✓';
    case 'Degraded': return '⚠';
    case 'Unhealthy': return '✗';
    default: return '?';
  }
}
```

### CSS Variables Reference

```css
:root {
  --surface-base: #ffffff;
  --surface-raised: #f9fafb;
  --surface-sunken: #f3f4f6;
  --surface-hover: #e5e7eb;

  --border-subtle: #e5e7eb;
  --border-default: #d1d5db;

  --text-primary: #111827;
  --text-secondary: #6b7280;
  --text-faint: #9ca3af;

  --accent-success: #22c55e;
  --accent-warning: #facc15;
  --accent-danger: #ef4444;
  --accent-info: #3b82f6;

  --font-mono: 'SF Mono', 'Monaco', 'Consolas', monospace;
}
```

---

**End of Proposal**
