// ========================================
// Koan Admin - Framework Inspector
// Main Application Logic
// ========================================

// Global State
const AppState = {
  currentView: 'dashboard',
  configViewMode: 'canonical', // 'canonical' | 'appsettings' | 'env'
  selectedPillar: null,
  selectedModule: null,
  expandedPillars: [],
  apiData: null,
  refreshInterval: 30000,
  autoRefresh: true,
  lastUpdate: null,
  configSearchTerm: '',
  configSortBy: 'key', // 'key' | 'source' | 'value'
  expandedConfigItems: [], // Array of expanded canonical keys
  configDisplayMode: 'canonical' // 'label' | 'canonical'
};

// Constants
const PILLAR_ICONS = {
  'data': '🗄️',
  'web': '🌐',
  'ai': '🧠',
  'security': '🔐',
  'core': '⚙️',
  'admin': '🧩',
  'scheduling': '⏰',
  'messaging': '📨',
  'storage': '💾'
};

const PILLAR_COLORS = {
  'data': '6, 182, 212',
  'web': '139, 92, 246',
  'ai': '236, 72, 153',
  'security': '250, 204, 21',
  'core': '100, 116, 139',
  'admin': '6, 182, 212',
  'scheduling': '56, 189, 248',
  'messaging': '139, 92, 246',
  'storage': '100, 116, 139'
};

// ========================================
// State Management
// ========================================

function loadState() {
  const saved = localStorage.getItem('koan-admin-state');
  if (saved) {
    try {
      const parsed = JSON.parse(saved);
      AppState.expandedPillars = parsed.expandedPillars || [];
      AppState.autoRefresh = parsed.autoRefresh !== undefined ? parsed.autoRefresh : true;
      AppState.configViewMode = parsed.configViewMode || 'canonical';
      AppState.configSortBy = parsed.configSortBy || 'key';
      AppState.expandedConfigItems = parsed.expandedConfigItems || [];
      AppState.configDisplayMode = parsed.configDisplayMode || 'canonical';
    } catch (e) {
      console.error('Failed to load state:', e);
    }
  }
}

function saveState() {
  localStorage.setItem('koan-admin-state', JSON.stringify({
    expandedPillars: AppState.expandedPillars,
    autoRefresh: AppState.autoRefresh,
    configViewMode: AppState.configViewMode,
    configSortBy: AppState.configSortBy,
    expandedConfigItems: AppState.expandedConfigItems,
    configDisplayMode: AppState.configDisplayMode
  }));
}

// ========================================
// API Fetching
// ========================================

async function fetchAPIData() {
  try {
    const response = await fetch('api/status');
    if (!response.ok) throw new Error(`HTTP ${response.status}`);

    const data = await response.json();
    AppState.apiData = data;
    AppState.lastUpdate = new Date();

    updateStatusIndicator('healthy', 'Healthy');
    return data;
  } catch (error) {
    console.error('Failed to fetch API data:', error);
    updateStatusIndicator('error', 'Error');
    return null;
  }
}

function updateStatusIndicator(status, label) {
  const indicator = document.getElementById('status-indicator');
  if (!indicator) return;

  const dot = indicator.querySelector('.status-dot');
  const text = indicator.querySelector('.status-label');

  if (dot) {
    dot.className = `status-dot status-${status}`;
  }
  if (text) {
    text.textContent = label;
  }
}

// ========================================
// Hash Routing
// ========================================

function navigate(hash) {
  window.location.hash = hash;
}

function parseHash() {
  const hash = window.location.hash.slice(1); // Remove #
  const parts = hash.split('/').filter(Boolean);

  if (parts.length === 0) {
    return { view: 'dashboard', params: {} };
  }

  const [view, ...rest] = parts;

  switch (view) {
    case 'ops':
      return { view: 'ops', params: {} };
    case 'framework':
      return { view: 'framework', params: {} };
    case 'configuration':
      return { view: 'configuration', params: {} };
    case 'mesh':
      return { view: 'mesh', params: {} };
    case 'pillar':
      return { view: 'pillar', params: { pillar: rest[0] } };
    case 'module':
      return { view: 'module', params: { module: decodeURIComponent(rest[0]) } };
    default:
      return { view: 'dashboard', params: {} };
  }
}

function handleRouteChange() {
  const route = parseHash();
  switchView(route.view, route.params);
  updateNavHighlight(route.view);
}

function updateNavHighlight(view) {
  // Map view to nav item
  const navMapping = {
    'dashboard': 'dashboard',
    'ops': 'ops',
    'framework': 'framework',
    'configuration': 'configuration',
    'mesh': 'mesh',
    'pillar': 'dashboard',  // Pillar views don't highlight nav
    'module': 'dashboard'   // Module views don't highlight nav
  };

  const navItem = navMapping[view] || 'dashboard';

  document.querySelectorAll('.nav-item').forEach(item => {
    item.classList.toggle('active', item.dataset.nav === navItem);
  });
}

function switchView(view, params = {}) {
  AppState.currentView = view;

  // Hide all views
  document.querySelectorAll('.view').forEach(v => v.classList.remove('active'));

  // Show active view
  const activeView = document.getElementById(`${view}-view`);
  if (activeView) {
    activeView.classList.add('active');
  }

  // Handle view-specific logic
  switch (view) {
    case 'ops':
      renderOpsMode();
      break;
    case 'framework':
      renderFrameworkMode();
      break;
    case 'configuration':
      renderConfigurationView();
      break;
    case 'mesh':
      renderMeshView();
      break;
    case 'pillar':
      if (params.pillar) {
        renderPillarView(params.pillar);
      }
      break;
    case 'module':
      if (params.module) {
        renderModuleView(params.module);
      }
      break;
  }
}

// ========================================
// Framework Pulse (Hero Metrics)
// ========================================

function renderFrameworkPulse() {
  const data = AppState.apiData;
  if (!data) return;

  const modulesCount = data.modules?.length || 0;
  const providersCount = countProviders(data);
  const healthyCount = data.health?.components?.filter(h => h.status === 'Healthy').length || 0;
  const totalHealth = data.health?.components?.length || 0;
  const settingsCount = data.modules?.reduce((sum, m) => sum + (m.settings?.length || 0), 0) || 0;

  const pulseModules = document.getElementById('pulse-modules');
  const pulseProviders = document.getElementById('pulse-providers');
  const pulseHealth = document.getElementById('pulse-health');
  const pulseSettings = document.getElementById('pulse-settings');

  if (pulseModules) pulseModules.textContent = modulesCount;
  if (pulseProviders) pulseProviders.textContent = providersCount;
  if (pulseHealth) pulseHealth.textContent = `${healthyCount}/${totalHealth}`;
  if (pulseSettings) pulseSettings.textContent = settingsCount;
}

function countProviders(data) {
  const providers = new Set();
  data.modules?.forEach(module => {
    if (module.name?.toLowerCase().includes('connector')) {
      const parts = module.name.split('.');
      const providerName = parts[parts.length - 1];
      providers.add(providerName);
    }
  });
  return providers.size;
}

// ========================================
// Pillar Accordion Rendering
// ========================================

function renderPillars() {
  const data = AppState.apiData;
  if (!data || !data.modules) return;

  const pillars = groupByPillar(data.modules);
  const container = document.getElementById('pillars-container');
  if (!container) return;

  container.innerHTML = '';

  Object.entries(pillars).forEach(([pillarName, modules]) => {
    const pillarEl = createPillarElement(pillarName, modules);
    container.appendChild(pillarEl);
  });
}

function groupByPillar(modules) {
  const pillars = {};

  modules.forEach(module => {
    const pillarName = extractPillarName(module.name || module.pillar);
    if (!pillars[pillarName]) {
      pillars[pillarName] = [];
    }
    pillars[pillarName].push(module);
  });

  return pillars;
}

function extractPillarName(moduleName) {
  const parts = moduleName.split('.');
  if (parts.length < 2) return 'core';

  const pillar = parts[1].toLowerCase();

  // Map to known pillars
  if (pillar === 'data' || pillar === 'web' || pillar === 'ai' ||
      pillar === 'security' || pillar === 'core' || pillar === 'admin' ||
      pillar === 'scheduling' || pillar === 'messaging' || pillar === 'storage') {
    return pillar;
  }

  return 'core';
}

function createPillarElement(pillarName, modules) {
  const details = document.createElement('details');
  details.className = 'pillar';
  details.dataset.pillar = pillarName;

  const colorRGB = PILLAR_COLORS[pillarName] || '100, 116, 139';
  details.style.setProperty('--pillar-color-rgb', colorRGB);

  // Check if this pillar should be open
  if (AppState.expandedPillars.includes(pillarName)) {
    details.open = true;
  }

  details.addEventListener('toggle', () => {
    if (details.open) {
      if (!AppState.expandedPillars.includes(pillarName)) {
        AppState.expandedPillars.push(pillarName);
      }
    } else {
      AppState.expandedPillars = AppState.expandedPillars.filter(p => p !== pillarName);
    }
    saveState();
  });

  const summary = document.createElement('summary');
  summary.innerHTML = `
    <div class="pillar-summary-left">
      <span class="pillar-icon">${PILLAR_ICONS[pillarName] || '📦'}</span>
      <span class="pillar-name">${pillarName.toUpperCase()}</span>
      <span class="pillar-count">(${modules.length})</span>
    </div>
    <span class="pillar-health"></span>
  `;

  const modulesContainer = document.createElement('div');
  modulesContainer.className = 'pillar-modules';

  modules.forEach(module => {
    const moduleItem = createModuleItem(module);
    modulesContainer.appendChild(moduleItem);
  });

  details.appendChild(summary);
  details.appendChild(modulesContainer);

  return details;
}

function createModuleItem(module) {
  const div = document.createElement('div');
  div.className = 'module-item';
  div.dataset.module = module.name;

  const shortName = extractModuleShortName(module.name);
  const settingsCount = module.settings?.length || 0;
  const notesCount = module.notes?.length || 0;

  div.innerHTML = `
    <span class="module-name">${shortName}</span>
    <span class="module-indicators">
      ${settingsCount > 0 ? `<span class="has-settings" title="${settingsCount} settings">⚙</span>` : ''}
      ${notesCount > 0 ? `<span class="has-notes" title="${notesCount} notes">📝</span>` : ''}
    </span>
  `;

  div.addEventListener('click', (e) => {
    e.stopPropagation();
    selectModule(module);
  });

  return div;
}

function extractModuleShortName(fullName) {
  const parts = fullName.split('.');
  return parts.slice(2).join('.') || parts[parts.length - 1];
}

function selectModule(module) {
  AppState.selectedModule = module;

  // Update selection in pillar sidebar
  document.querySelectorAll('.module-item').forEach(item => {
    item.classList.toggle('selected', item.dataset.module === module.name);
  });

  // Navigate to module view
  navigate(`#/module/${encodeURIComponent(module.name)}`);
}

// ========================================
// Ops Mode Content
// ========================================

function renderOpsMode() {
  const data = AppState.apiData;
  if (!data) return;

  renderTelemetry(data.runtime);
  renderHealth(data.health?.components);
  renderEnvironment(data.environment);
  renderStartupNotes(data.modules);
}

function renderTelemetry(runtime) {
  const grid = document.getElementById('telemetry-grid');
  if (!grid || !runtime) return;

  const process = runtime.process || {};
  const memory = runtime.memory || {};
  const gc = runtime.garbageCollector || {};
  const threadPool = runtime.threadPool || {};

  grid.innerHTML = `
    <div class="telemetry-card">
      <div class="telemetry-label">CPU Usage</div>
      <div class="telemetry-value">${formatPercent(process.cpuUtilizationPercent) || '—'}</div>
      <div class="telemetry-detail">Current</div>
    </div>
    <div class="telemetry-card">
      <div class="telemetry-label">Memory</div>
      <div class="telemetry-value">${formatBytes(memory.workingSetBytes) || '—'}</div>
      <div class="telemetry-detail">Working set</div>
    </div>
    <div class="telemetry-card">
      <div class="telemetry-label">Threads</div>
      <div class="telemetry-value">${threadPool.threadCount || '—'}</div>
      <div class="telemetry-detail">Thread pool</div>
    </div>
    <div class="telemetry-card">
      <div class="telemetry-label">GC Collections</div>
      <div class="telemetry-value">${formatGCCounts(gc.collectionCounts)}</div>
      <div class="telemetry-detail">Gen0/Gen1/Gen2</div>
    </div>
    <div class="telemetry-card">
      <div class="telemetry-label">Handles</div>
      <div class="telemetry-value">${process.handleCount || '—'}</div>
      <div class="telemetry-detail">System handles</div>
    </div>
    <div class="telemetry-card">
      <div class="telemetry-label">Work Items</div>
      <div class="telemetry-value">${formatNumber(threadPool.pendingWorkItemCount) || '—'}</div>
      <div class="telemetry-detail">ThreadPool queue</div>
    </div>
  `;
}

function renderHealth(components) {
  const body = document.getElementById('health-body');
  if (!body || !components) return;

  if (components.length === 0) {
    body.innerHTML = '<p class="text-secondary">No health checks configured</p>';
    return;
  }

  // Sort by criticality (critical=true first), then by status, then by component name
  const sortedComponents = [...components].sort((a, b) => {
    const aCritical = a.facts?.critical === 'true';
    const bCritical = b.facts?.critical === 'true';

    if (aCritical !== bCritical) {
      return bCritical ? 1 : -1; // Critical items first
    }

    // Then by status (error, degraded, healthy, unknown)
    const statusOrder = { 'error': 0, 'degraded': 1, 'unknown': 2, 'healthy': 3 };
    const aStatus = (a.status?.toLowerCase() || 'unknown');
    const bStatus = (b.status?.toLowerCase() || 'unknown');
    const aOrder = statusOrder[aStatus] ?? 4;
    const bOrder = statusOrder[bStatus] ?? 4;

    if (aOrder !== bOrder) {
      return aOrder - bOrder;
    }

    // Finally by component name
    return (a.component || '').localeCompare(b.component || '');
  });

  const list = document.createElement('div');
  list.className = 'health-list';

  sortedComponents.forEach(item => {
    const div = document.createElement('div');
    const isCritical = item.facts?.critical === 'true';
    div.className = isCritical ? 'health-item critical' : 'health-item';

    const statusClass = item.status?.toLowerCase() || 'unknown';

    div.innerHTML = `
      <span class="health-component">
        ${isCritical ? '<span class="critical-badge">CRITICAL</span>' : ''}
        ${item.component || 'Unknown'}
      </span>
      <span class="health-status ${statusClass}">
        ${statusClass === 'healthy' ? '●' : statusClass === 'degraded' ? '⚠' : statusClass === 'error' ? '✕' : '○'}
        ${item.status || 'Unknown'}
      </span>
    `;

    list.appendChild(div);
  });

  body.innerHTML = '';
  body.appendChild(list);
}

function renderEnvironment(environment) {
  const body = document.getElementById('environment-body');
  if (!body || !environment) return;

  const list = document.createElement('div');
  list.className = 'env-list';

  Object.entries(environment).forEach(([key, value]) => {
    const div = document.createElement('div');
    div.className = 'env-item';
    div.innerHTML = `
      <span class="env-label">${formatEnvKey(key)}</span>
      <span class="env-value">${escapeHtml(String(value))}</span>
    `;
    list.appendChild(div);
  });

  body.innerHTML = '';
  body.appendChild(list);
}

function renderStartupNotes(modules) {
  const body = document.getElementById('startup-notes-body');
  if (!body || !modules) return;

  const notes = [];
  modules.forEach(module => {
    if (module.notes && module.notes.length > 0) {
      module.notes.forEach(note => {
        notes.push({ module: module.name, note });
      });
    }
  });

  if (notes.length === 0) {
    body.innerHTML = '<p class="text-secondary">No startup notes</p>';
    return;
  }

  const list = document.createElement('ul');
  list.className = 'notes-list';

  notes.slice(0, 10).forEach(item => {
    const li = document.createElement('li');
    li.className = 'note-item';
    li.innerHTML = `
      <span class="note-module">${extractModuleShortName(item.module)}</span>
      <span class="note-text">${escapeHtml(item.note)}</span>
    `;
    list.appendChild(li);
  });

  body.innerHTML = '';
  body.appendChild(list);
}

// ========================================
// Framework Mode Content
// ========================================

function renderFrameworkMode() {
  const data = AppState.apiData;
  if (!data) return;

  renderAutoRegistrationReport(data.modules);
  renderProviderElection(data.modules);
  renderCapabilityMatrix(data.modules);
  renderProvenanceAnalysis(data.modules);
}

function renderAutoRegistrationReport(modules) {
  const summary = document.getElementById('registration-summary');
  const tableBody = document.querySelector('#registration-table tbody');

  if (!summary || !tableBody || !modules) return;

  summary.textContent = `✓ Scan completed • ${modules.length} modules registered via IKoanAutoRegistrar`;

  const pillars = groupByPillar(modules);
  tableBody.innerHTML = '';

  Object.entries(pillars).forEach(([pillarName, pillarModules]) => {
    const settingsCount = pillarModules.reduce((sum, m) => sum + (m.settings?.length || 0), 0);
    const notesCount = pillarModules.reduce((sum, m) => sum + (m.notes?.length || 0), 0);

    const row = document.createElement('tr');
    row.innerHTML = `
      <td><strong>${pillarName.toUpperCase()}</strong></td>
      <td>${pillarModules.length}</td>
      <td>${settingsCount}</td>
      <td>${notesCount}</td>
      <td><span class="text-success">✓ OK</span></td>
    `;
    tableBody.appendChild(row);
  });
}

function renderProviderElection(modules) {
  const body = document.getElementById('provider-election-body');
  if (!body || !modules) return;

  const providers = modules.filter(m => m.name.toLowerCase().includes('connector'));

  if (providers.length === 0) {
    body.innerHTML = '<p class="text-secondary">No providers detected</p>';
    return;
  }

  body.innerHTML = '';

  providers.forEach(provider => {
    const card = document.createElement('div');
    card.className = 'provider-election-card';

    const providerType = extractProviderType(provider.name);
    const connectionString = findConnectionString(provider.settings);

    card.innerHTML = `
      <div class="provider-election-header">
        <span class="provider-name">${providerType}</span>
      </div>
      <div class="provider-details">
        <div class="provider-detail-row">
          <span class="provider-detail-label">Module</span>
          <span class="provider-detail-value">${provider.name}</span>
        </div>
        ${connectionString ? `
          <div class="provider-detail-row">
            <span class="provider-detail-label">Connection</span>
            <span class="provider-detail-value">${escapeHtml(truncate(connectionString, 80))}</span>
          </div>
        ` : ''}
        <div class="provider-detail-row">
          <span class="provider-detail-label">Settings</span>
          <span class="provider-detail-value">${provider.settings?.length || 0} configured</span>
        </div>
        <div class="provider-detail-row">
          <span class="provider-detail-label">Reason</span>
          <span class="provider-detail-value">Auto-discovered via ${providerType}DiscoveryAdapter</span>
        </div>
      </div>
    `;

    body.appendChild(card);
  });
}

function renderCapabilityMatrix(modules) {
  const matrix = document.getElementById('capability-matrix');
  if (!matrix || !modules) return;

  const providers = modules.filter(m => m.name.toLowerCase().includes('connector'));

  if (providers.length === 0) {
    matrix.innerHTML = '<p class="text-secondary">No providers to analyze</p>';
    return;
  }

  const table = document.createElement('table');
  table.className = 'capability-table';
  table.innerHTML = `
    <thead>
      <tr>
        <th>Provider</th>
        <th>Module</th>
        <th>Settings</th>
        <th>Status</th>
      </tr>
    </thead>
    <tbody></tbody>
  `;

  const tbody = table.querySelector('tbody');

  providers.forEach(provider => {
    const row = document.createElement('tr');
    const providerType = extractProviderType(provider.name);
    const settingsCount = provider.settings?.length || 0;

    row.innerHTML = `
      <td><strong>${providerType}</strong></td>
      <td>${provider.name}</td>
      <td>${settingsCount}</td>
      <td><span class="capability-indicator supported">✓ Active</span></td>
    `;
    tbody.appendChild(row);
  });

  matrix.innerHTML = '';
  matrix.appendChild(table);
}

function renderProvenanceAnalysis(modules) {
  const summary = document.getElementById('provenance-summary');
  const details = document.getElementById('provenance-details');

  if (!summary || !details || !modules) return;

  let autoCount = 0;
  let appSettingsCount = 0;
  let environmentCount = 0;

  modules.forEach(module => {
    if (module.settings) {
      module.settings.forEach(setting => {
        const source = setting.source || 'Auto';
        if (source.toLowerCase().includes('auto')) autoCount++;
        else if (source.toLowerCase().includes('appsettings')) appSettingsCount++;
        else if (source.toLowerCase().includes('environment')) environmentCount++;
      });
    }
  });

  summary.innerHTML = `
    <div class="provenance-stat">
      <div class="provenance-stat-label">Auto Defaults</div>
      <div class="provenance-stat-value">${autoCount}</div>
    </div>
    <div class="provenance-stat">
      <div class="provenance-stat-label">AppSettings</div>
      <div class="provenance-stat-value">${appSettingsCount}</div>
    </div>
    <div class="provenance-stat">
      <div class="provenance-stat-label">Environment</div>
      <div class="provenance-stat-value">${environmentCount}</div>
    </div>
    <div class="provenance-stat">
      <div class="provenance-stat-label">Total Settings</div>
      <div class="provenance-stat-value">${autoCount + appSettingsCount + environmentCount}</div>
    </div>
  `;

  details.innerHTML = '<p class="text-secondary">Navigate to Configuration view for detailed setting provenance</p>';
}

// ========================================
// Configuration View
// ========================================

function renderConfigurationView() {
  const data = AppState.apiData;
  if (!data || !data.modules) return;

  // Collect all settings
  const allSettings = [];
  data.modules.forEach(module => {
    if (module.settings) {
      module.settings.forEach(setting => {
        allSettings.push({
          ...setting,
          module: module.name,
          canonicalKey: buildCanonicalKey(module.name, setting.key)
        });
      });
    }
  });

  // Update display button states
  const displayButtons = document.querySelectorAll('.config-display-btn');
  displayButtons.forEach(btn => {
    btn.classList.toggle('active', btn.dataset.display === AppState.configDisplayMode);
  });

  // Update sort button states
  const sortButtons = document.querySelectorAll('.config-sort-btn');
  sortButtons.forEach(btn => {
    btn.classList.toggle('active', btn.dataset.sort === AppState.configSortBy);
  });

  // Render based on current mode
  switch (AppState.configViewMode) {
    case 'canonical':
      renderCanonicalView(allSettings);
      break;
    case 'appsettings':
      renderAppSettingsView(allSettings);
      break;
    case 'env':
      renderEnvView(allSettings);
      break;
  }
}

function buildCanonicalKey(moduleName, settingKey) {
  // The setting key is already the full canonical path
  return settingKey;
}

function renderCanonicalView(allSettings) {
  const list = document.getElementById('config-list');
  if (!list) return;

  // Filter by search term
  const filteredSettings = allSettings.filter(setting => {
    if (!AppState.configSearchTerm) return true;
    const searchLower = AppState.configSearchTerm.toLowerCase();
    return setting.canonicalKey.toLowerCase().includes(searchLower) ||
           setting.value.toLowerCase().includes(searchLower) ||
           (setting.source || '').toLowerCase().includes(searchLower);
  });

  if (filteredSettings.length === 0) {
    list.innerHTML = '<p class="text-secondary">No settings found</p>';
    return;
  }

  // Sort settings based on AppState.configSortBy
  const sortedSettings = [...filteredSettings].sort((a, b) => {
    switch (AppState.configSortBy) {
      case 'key':
        return a.canonicalKey.localeCompare(b.canonicalKey);
      case 'source':
        const sourceA = (a.source || 'Auto').toLowerCase();
        const sourceB = (b.source || 'Auto').toLowerCase();
        return sourceA.localeCompare(sourceB);
      case 'value':
        return a.value.localeCompare(b.value);
      default:
        return 0;
    }
  });

  list.innerHTML = '';

  sortedSettings.forEach(setting => {
    const item = document.createElement('div');
    item.className = 'config-item';
    item.dataset.canonicalKey = setting.canonicalKey;

    // Check if this item is expanded
    const isExpanded = AppState.expandedConfigItems.includes(setting.canonicalKey);
    if (isExpanded) {
      item.classList.add('expanded');
    }

    const source = (setting.source || 'Auto').toLowerCase().replace(/\s/g, '');
    const sourceLabel = setting.source || 'Auto';
    const consumers = setting.consumers || [];

    // Determine display text based on display mode
    const displayText = AppState.configDisplayMode === 'label'
      ? (setting.label || setting.canonicalKey)
      : setting.canonicalKey;

    // Get source key for non-Auto sources
    const sourceKey = setting.sourceKey && setting.sourceKey.trim() !== '' ? setting.sourceKey : null;

    item.innerHTML = `
      <div class="config-item-row">
        <div class="config-key">
          <span class="config-expand-icon">${isExpanded ? '▼' : '▶'}</span>
          ${displayText}
        </div>
        <span class="config-source-badge ${source}">${sourceLabel}</span>
        <div class="config-value">${escapeHtml(truncate(setting.value, 80))}</div>
      </div>
      <div class="config-item-details">
        <div class="config-detail-row">
          <span class="config-detail-label">Label</span>
          <span class="config-detail-value">${setting.label || setting.canonicalKey}</span>
        </div>
        <div class="config-detail-row">
          <span class="config-detail-label">Canonical Key</span>
          <span class="config-detail-value">${setting.canonicalKey}</span>
        </div>
        <div class="config-detail-row">
          <span class="config-detail-label">Value</span>
          <span class="config-detail-value">${escapeHtml(setting.value)}</span>
        </div>
        ${sourceKey ? `
          <div class="config-detail-row">
            <span class="config-detail-label">Source Key</span>
            <span class="config-detail-value">
              <span class="config-source-key ${source}">${sourceKey}</span>
            </span>
          </div>
        ` : ''}
        ${setting.description ? `
          <div class="config-detail-row">
            <span class="config-detail-label">Description</span>
            <span class="config-detail-value">${escapeHtml(setting.description)}</span>
          </div>
        ` : ''}
        ${consumers.length > 0 ? `
          <div class="config-detail-row">
            <span class="config-detail-label">Consumers</span>
            <div class="config-consumers">
              ${consumers.map(c => `<span class="config-consumer-chip">${c}</span>`).join('')}
            </div>
          </div>
        ` : ''}
      </div>
    `;

    // Add click handler to header row only to toggle expanded state
    const headerRow = item.querySelector('.config-item-row');
    headerRow.addEventListener('click', (e) => {
      toggleConfigItem(setting.canonicalKey);
    });

    list.appendChild(item);
  });
}

function toggleConfigItem(canonicalKey) {
  const index = AppState.expandedConfigItems.indexOf(canonicalKey);

  if (index > -1) {
    // Item is expanded, collapse it
    AppState.expandedConfigItems.splice(index, 1);
  } else {
    // Item is collapsed, expand it
    AppState.expandedConfigItems.push(canonicalKey);
  }

  saveState();

  // Re-render to update UI
  renderCanonicalView(collectAllSettings());
}

function renderAppSettingsView(allSettings) {
  const output = document.getElementById('config-json-output');
  if (!output) return;

  // Build nested JSON structure
  const json = {};

  allSettings.forEach(setting => {
    const parts = setting.canonicalKey.split(':');
    let current = json;
    let isValid = true;

    parts.forEach((part, index) => {
      if (!isValid) return; // Skip if we've hit an invalid path

      if (index === parts.length - 1) {
        // Last part - set the value (but only if current is an object)
        if (typeof current === 'object' && current !== null) {
          current[part] = setting.value;
        }
      } else {
        // Intermediate part - create object if doesn't exist
        if (!current[part]) {
          current[part] = {};
        } else if (typeof current[part] !== 'object' || current[part] === null) {
          // If current[part] is already a primitive value, we can't traverse deeper
          // This happens when settings have conflicting paths
          isValid = false;
          return;
        }
        current = current[part];
      }
    });
  });

  output.textContent = JSON.stringify(json, null, 2);
}

function renderEnvView(allSettings) {
  const output = document.getElementById('config-env-output');
  if (!output) return;

  // Convert to environment variable format (Koan:Pillar:Module → KOAN__PILLAR__MODULE)
  const lines = [];

  // Group by source
  const bySource = {
    auto: [],
    appsettings: [],
    environment: []
  };

  allSettings.forEach(setting => {
    const source = (setting.source || 'Auto').toLowerCase();
    const envKey = setting.canonicalKey.replace(/:/g, '__').toUpperCase();
    const envLine = `${envKey}=${setting.value}`;

    if (source.includes('auto')) {
      bySource.auto.push(envLine);
    } else if (source.includes('appsettings')) {
      bySource.appsettings.push(envLine);
    } else if (source.includes('environment')) {
      bySource.environment.push(envLine);
    }
  });

  // Build output with sections
  if (bySource.environment.length > 0) {
    lines.push('# Environment-sourced settings');
    lines.push(...bySource.environment);
    lines.push('');
  }

  if (bySource.appsettings.length > 0) {
    lines.push('# AppSettings-sourced settings');
    lines.push(...bySource.appsettings);
    lines.push('');
  }

  if (bySource.auto.length > 0) {
    lines.push('# Auto-discovered settings (shown for reference)');
    bySource.auto.forEach(line => {
      lines.push(`# ${line}`);
    });
  }

  output.textContent = lines.join('\n');
}

// ========================================
// Pillar View Rendering
// ========================================

function renderPillarView(pillarName) {
  const data = AppState.apiData;
  if (!data) return;

  const pillars = groupByPillar(data.modules);
  const modules = pillars[pillarName] || [];

  const titleEl = document.getElementById('pillar-view-title');
  const subtitleEl = document.getElementById('pillar-view-subtitle');

  if (titleEl) titleEl.textContent = `${pillarName.toUpperCase()} Pillar`;
  if (subtitleEl) subtitleEl.textContent = `${modules.length} modules`;

  const grid = document.getElementById('pillar-modules-grid');
  if (!grid) return;

  grid.innerHTML = '';

  modules.forEach(module => {
    const card = createModuleCard(module, pillarName);
    grid.appendChild(card);
  });

  // Setup back button
  const backBtn = document.getElementById('back-to-dashboard');
  if (backBtn) {
    backBtn.onclick = () => navigate('#/');
  }
}

function createModuleCard(module, pillarName) {
  const card = document.createElement('div');
  card.className = 'module-card';

  const colorRGB = PILLAR_COLORS[pillarName] || '100, 116, 139';
  card.style.borderLeftColor = `rgb(${colorRGB})`;

  const version = module.version || 'N/A';
  const settingsCount = module.settings?.length || 0;
  const notesCount = module.notes?.length || 0;

  card.innerHTML = `
    <div class="module-card-header">
      <div>
        <div class="module-card-title">${module.name}</div>
        <div class="module-card-version">v${version}</div>
      </div>
    </div>
    <div class="module-card-meta">
      <span>⚙ ${settingsCount} settings</span>
      <span>📝 ${notesCount} notes</span>
    </div>
  `;

  card.addEventListener('click', () => {
    navigate(`#/module/${encodeURIComponent(module.name)}`);
  });

  return card;
}

// ========================================
// Module View Rendering
// ========================================

function renderModuleView(moduleName) {
  const data = AppState.apiData;
  if (!data) return;

  const module = data.modules?.find(m => m.name === moduleName);
  if (!module) {
    const titleEl = document.getElementById('module-view-title');
    const subtitleEl = document.getElementById('module-view-subtitle');
    const contentEl = document.getElementById('module-detail-content');

    if (titleEl) titleEl.textContent = 'Module Not Found';
    if (subtitleEl) subtitleEl.textContent = '';
    if (contentEl) contentEl.innerHTML = '<p class="text-secondary">Module not found</p>';
    return;
  }

  const titleEl = document.getElementById('module-view-title');
  const subtitleEl = document.getElementById('module-view-subtitle');

  if (titleEl) titleEl.textContent = module.name;
  if (subtitleEl) subtitleEl.textContent = `v${module.version || 'N/A'}`;

  const content = document.getElementById('module-detail-content');
  if (!content) return;

  content.innerHTML = `
    <div class="panel">
      <div class="panel-header">
        <div>
          <h3>Settings</h3>
          <p class="panel-subtitle">${module.settings?.length || 0} configuration values</p>
        </div>
      </div>
      <div class="panel-body">
        ${renderAllSettings(module.settings)}
      </div>
    </div>

    ${module.notes && module.notes.length > 0 ? `
      <div class="panel">
        <div class="panel-header">
          <div>
            <h3>Notes</h3>
            <p class="panel-subtitle">${module.notes.length} startup notes</p>
          </div>
        </div>
        <div class="panel-body">
          <ul class="notes-list">
            ${module.notes.map(note => `
              <li class="note-item">
                <span class="note-text">${escapeHtml(note)}</span>
              </li>
            `).join('')}
          </ul>
        </div>
      </div>
    ` : ''}

    ${module.tools && module.tools.length > 0 ? `
      <div class="panel">
        <div class="panel-header">
          <div>
            <h3>Tools</h3>
            <p class="panel-subtitle">${module.tools.length} exposed routes</p>
          </div>
        </div>
        <div class="panel-body">
          ${module.tools.map(tool => `
            <div class="setting-compact">
              <div class="setting-key">${tool.name || 'Tool'}</div>
              <div class="setting-value">${escapeHtml(tool.route || 'N/A')}</div>
            </div>
          `).join('')}
        </div>
      </div>
    ` : ''}
  `;

  // Setup back button
  const backBtn = document.getElementById('back-to-pillar');
  if (backBtn) {
    const pillarName = extractPillarName(module.name);
    backBtn.onclick = () => navigate(`#/pillar/${pillarName}`);
  }
}

function renderAllSettings(settings) {
  if (!settings || settings.length === 0) {
    return '<p class="text-secondary">No settings configured</p>';
  }

  return settings.map(setting => {
    const source = setting.source || 'Auto';
    const sourceClass = source.toLowerCase().replace(/\s/g, '');
    const consumers = setting.consumers || [];

    return `
      <div class="setting-compact">
        <div class="setting-key">${setting.key}</div>
        <div class="setting-value">${escapeHtml(setting.value)}</div>
        <div style="display: flex; justify-content: space-between; align-items: center; margin-top: 0.5rem;">
          <span class="setting-source ${sourceClass}">${source}</span>
          ${consumers.length > 0 ? `
            <div class="consumer-chips">
              ${consumers.slice(0, 3).map(c => `<span class="consumer-chip">${c}</span>`).join('')}
              ${consumers.length > 3 ? `<span class="consumer-chip">+${consumers.length - 3}</span>` : ''}
            </div>
          ` : ''}
        </div>
      </div>
    `;
  }).join('');
}

// ========================================
// Utility Functions
// ========================================

function formatBytes(bytes) {
  if (bytes === undefined || bytes === null) return null;
  if (bytes === 0) return '0 B';
  const k = 1024;
  const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return Math.round((bytes / Math.pow(k, i)) * 100) / 100 + ' ' + sizes[i];
}

function formatNumber(num) {
  if (num === undefined || num === null) return null;
  return num.toLocaleString();
}

function formatPercent(value) {
  if (value === undefined || value === null) return null;
  return `${value.toFixed(1)}%`;
}

function formatGCCounts(gcCounts) {
  if (!gcCounts || gcCounts.length !== 3) return '—/—/—';
  return gcCounts.join('/');
}

function formatEnvKey(key) {
  return key.replace(/([A-Z])/g, ' $1').trim();
}

function truncate(str, maxLen) {
  if (!str) return '';
  if (str.length <= maxLen) return str;
  return str.substring(0, maxLen) + '...';
}

function escapeHtml(text) {
  const div = document.createElement('div');
  div.textContent = text;
  return div.innerHTML;
}

function extractProviderType(moduleName) {
  const parts = moduleName.split('.');
  return parts[parts.length - 1];
}

function findConnectionString(settings) {
  if (!settings) return null;
  const connSetting = settings.find(s =>
    s.key.toLowerCase().includes('connection') ||
    s.key.toLowerCase().includes('endpoint') ||
    s.key.toLowerCase().includes('url')
  );
  return connSetting?.value || null;
}

// ========================================
// Context Bar Updates
// ========================================

function updateContextMeta() {
  const data = AppState.apiData;
  if (!data || !data.environment) return;

  const envBadge = document.getElementById('env-badge');
  const sessionId = document.getElementById('session-id');
  const uptime = document.getElementById('uptime');

  if (envBadge && data.environment.environmentName) {
    envBadge.textContent = data.environment.environmentName;
  }

  if (sessionId && data.environment.sessionId) {
    sessionId.textContent = `Session: ${data.environment.sessionId}`;
  }

  if (uptime && data.runtime?.process?.uptimeSeconds) {
    const uptimeMs = data.runtime.process.uptimeSeconds * 1000;
    uptime.textContent = `Uptime: ${formatUptime(uptimeMs)}`;
  }
}

function updateGeneratedAt() {
  const timestamp = document.getElementById('generated-at');
  if (timestamp && AppState.lastUpdate) {
    timestamp.textContent = `Updated ${formatRelativeTime(AppState.lastUpdate)}`;
  }
}

function formatUptime(milliseconds) {
  const totalSeconds = Math.floor(milliseconds / 1000);
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  if (hours > 0) return `${hours}h ${minutes}m`;
  if (minutes > 0) return `${minutes}m`;
  return `${totalSeconds}s`;
}

function formatRelativeTime(date) {
  const seconds = Math.floor((new Date() - date) / 1000);
  if (seconds < 60) return `${seconds}s ago`;
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.floor(minutes / 60);
  return `${hours}h ago`;
}

// ========================================
// Main Render Function
// ========================================

function renderContent() {
  if (!AppState.apiData) return;

  renderFrameworkPulse();
  renderPillars();
  updateContextMeta();
  updateGeneratedAt();
}

// ========================================
// Event Handlers
// ========================================

function setupEventHandlers() {
  // Refresh button
  const refreshBtn = document.getElementById('refresh-btn');
  if (refreshBtn) {
    refreshBtn.addEventListener('click', async () => {
      refreshBtn.disabled = true;
      await fetchAPIData();
      renderContent();
      // Re-render current view
      handleRouteChange();
      setTimeout(() => {
        refreshBtn.disabled = false;
      }, 1000);
    });
  }

  // Auto-refresh toggle
  const autoRefreshCheckbox = document.getElementById('auto-refresh');
  if (autoRefreshCheckbox) {
    autoRefreshCheckbox.checked = AppState.autoRefresh;
    autoRefreshCheckbox.addEventListener('change', (e) => {
      AppState.autoRefresh = e.target.checked;
      saveState();
      if (AppState.autoRefresh) {
        startAutoRefresh();
      } else {
        stopAutoRefresh();
      }
    });
  }

  // Collapse all button
  const collapseAllBtn = document.getElementById('collapse-all-btn');
  if (collapseAllBtn) {
    collapseAllBtn.addEventListener('click', () => {
      document.querySelectorAll('.pillar[open]').forEach(pillar => {
        pillar.open = false;
      });
      AppState.expandedPillars = [];
      saveState();
    });
  }

  // Configuration view mode switcher
  const configModeButtons = document.querySelectorAll('#config-view-mode .view-mode-btn');
  configModeButtons.forEach(btn => {
    btn.addEventListener('click', () => {
      const mode = btn.dataset.mode;
      AppState.configViewMode = mode;
      saveState();

      // Update button states
      configModeButtons.forEach(b => b.classList.toggle('active', b.dataset.mode === mode));

      // Update content visibility
      document.querySelectorAll('.config-mode-content').forEach(content => {
        const contentMode = content.id.replace('config-', '');
        content.classList.toggle('active', contentMode === mode);
      });

      // Re-render configuration view
      renderConfigurationView();
    });
  });

  // Configuration search
  const searchInput = document.getElementById('config-search-input');
  if (searchInput) {
    searchInput.addEventListener('input', (e) => {
      AppState.configSearchTerm = e.target.value;
      renderCanonicalView(collectAllSettings());
    });
  }

  // Configuration display mode buttons
  const displayButtons = document.querySelectorAll('.config-display-btn');
  displayButtons.forEach(btn => {
    btn.addEventListener('click', () => {
      AppState.configDisplayMode = btn.dataset.display;
      saveState();

      // Update button states
      displayButtons.forEach(b => b.classList.toggle('active', b.dataset.display === AppState.configDisplayMode));

      // Re-render configuration view
      renderCanonicalView(collectAllSettings());
    });
  });

  // Configuration sort buttons
  const sortButtons = document.querySelectorAll('.config-sort-btn');
  sortButtons.forEach(btn => {
    btn.addEventListener('click', () => {
      AppState.configSortBy = btn.dataset.sort;
      saveState();

      // Update button states
      sortButtons.forEach(b => b.classList.toggle('active', b.dataset.sort === AppState.configSortBy));

      // Re-render configuration view
      renderCanonicalView(collectAllSettings());
    });
  });

  // Hash change
  window.addEventListener('hashchange', handleRouteChange);
}

function collectAllSettings() {
  const data = AppState.apiData;
  if (!data || !data.modules) return [];

  const allSettings = [];
  data.modules.forEach(module => {
    if (module.settings) {
      module.settings.forEach(setting => {
        allSettings.push({
          ...setting,
          module: module.name,
          canonicalKey: buildCanonicalKey(module.name, setting.key)
        });
      });
    }
  });

  return allSettings;
}

// ========================================
// Auto-refresh
// ========================================

let refreshTimer = null;

function startAutoRefresh() {
  stopAutoRefresh();
  if (AppState.autoRefresh) {
    refreshTimer = setInterval(async () => {
      await fetchAPIData();
      renderContent();
      // Re-render current view
      handleRouteChange();
      updateGeneratedAt();
    }, AppState.refreshInterval);
  }
}

function stopAutoRefresh() {
  if (refreshTimer) {
    clearInterval(refreshTimer);
    refreshTimer = null;
  }
}

// ========================================
// Service Mesh View
// ========================================

async function fetchMeshData() {
  try {
    const response = await fetch('api/status/service-mesh');
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    return await response.json();
  } catch (error) {
    console.error('Failed to fetch mesh data:', error);
    return null;
  }
}

async function renderMeshView() {
  const meshData = await fetchMeshData();

  if (!meshData || !meshData.enabled) {
    renderMeshDisabled();
    return;
  }

  renderMeshOverview(meshData);
  renderMeshServices(meshData);
}

function renderMeshDisabled() {
  const statsGrid = document.getElementById('mesh-stats-grid');
  const servicesGrid = document.getElementById('services-grid');
  const subtitle = document.getElementById('mesh-subtitle');

  if (subtitle) {
    subtitle.textContent = 'Service mesh not enabled';
  }

  if (statsGrid) {
    statsGrid.innerHTML = '<div class="empty-state">Service Mesh is not enabled or no services are registered.</div>';
  }

  if (servicesGrid) {
    servicesGrid.innerHTML = '';
  }
}

function renderMeshOverview(meshData) {
  const statsGrid = document.getElementById('mesh-stats-grid');
  const subtitle = document.getElementById('mesh-subtitle');

  if (!statsGrid) return;

  if (subtitle) {
    subtitle.textContent = `Orchestrator: ${meshData.orchestratorChannel || 'N/A'}`;
  }

  const healthyPercent = meshData.totalInstancesCount > 0
    ? Math.round((meshData.healthyInstancesCount / meshData.totalInstancesCount) * 100)
    : 0;

  statsGrid.innerHTML = `
    <div class="mesh-stat-card">
      <div class="mesh-stat-icon">🕸️</div>
      <div class="mesh-stat-content">
        <div class="mesh-stat-value">${meshData.totalServicesCount}</div>
        <div class="mesh-stat-label">Services</div>
      </div>
    </div>
    <div class="mesh-stat-card">
      <div class="mesh-stat-icon">📦</div>
      <div class="mesh-stat-content">
        <div class="mesh-stat-value">${meshData.totalInstancesCount}</div>
        <div class="mesh-stat-label">Total Instances</div>
      </div>
    </div>
    <div class="mesh-stat-card status-healthy">
      <div class="mesh-stat-icon">✓</div>
      <div class="mesh-stat-content">
        <div class="mesh-stat-value">${meshData.healthyInstancesCount}</div>
        <div class="mesh-stat-label">Healthy (${healthyPercent}%)</div>
      </div>
    </div>
    <div class="mesh-stat-card status-degraded">
      <div class="mesh-stat-icon">⚠</div>
      <div class="mesh-stat-content">
        <div class="mesh-stat-value">${meshData.degradedInstancesCount}</div>
        <div class="mesh-stat-label">Degraded</div>
      </div>
    </div>
    <div class="mesh-stat-card status-unhealthy">
      <div class="mesh-stat-icon">✗</div>
      <div class="mesh-stat-content">
        <div class="mesh-stat-value">${meshData.unhealthyInstancesCount}</div>
        <div class="mesh-stat-label">Unhealthy</div>
      </div>
    </div>
  `;
}

function renderMeshServices(meshData) {
  const servicesGrid = document.getElementById('services-grid');
  if (!servicesGrid) return;

  if (!meshData.services || meshData.services.length === 0) {
    servicesGrid.innerHTML = '<div class="empty-state">No services discovered</div>';
    return;
  }

  servicesGrid.innerHTML = meshData.services.map(service => `
    <div class="service-card">
      <div class="service-card-header">
        <div class="service-title-group">
          <h4 class="service-name">${escapeHtml(service.displayName)}</h4>
          <span class="service-id">${escapeHtml(service.serviceId)}</span>
        </div>
        <div class="service-health-badges">
          ${service.health.healthy > 0 ? `<span class="health-badge healthy">${service.health.healthy} ✓</span>` : ''}
          ${service.health.degraded > 0 ? `<span class="health-badge degraded">${service.health.degraded} ⚠</span>` : ''}
          ${service.health.unhealthy > 0 ? `<span class="health-badge unhealthy">${service.health.unhealthy} ✗</span>` : ''}
        </div>
      </div>

      ${service.description ? `<div class="service-description">${escapeHtml(service.description)}</div>` : ''}

      <div class="service-meta">
        <div class="service-meta-item">
          <span class="meta-label">Instances:</span>
          <span class="meta-value">${service.instanceCount}</span>
        </div>
        <div class="service-meta-item">
          <span class="meta-label">Load Balancing:</span>
          <span class="meta-value">${escapeHtml(service.loadBalancing.policy)}</span>
        </div>
        ${service.avgResponseTime ? `
        <div class="service-meta-item">
          <span class="meta-label">Avg Response:</span>
          <span class="meta-value">${formatDuration(service.avgResponseTime)}</span>
        </div>
        ` : ''}
      </div>

      ${service.capabilities && service.capabilities.length > 0 ? `
      <div class="service-capabilities">
        <span class="capabilities-label">Capabilities:</span>
        ${service.capabilities.map(cap => `<span class="capability-badge">${escapeHtml(cap)}</span>`).join('')}
      </div>
      ` : ''}

      <div class="service-instances">
        ${service.instances.map(instance => renderServiceInstance(instance)).join('')}
      </div>
    </div>
  `).join('');
}

function renderServiceInstance(instance) {
  const statusClass = instance.status.toLowerCase();
  const statusIcon = instance.status === 'Healthy' ? '✓' :
                     instance.status === 'Degraded' ? '⚠' : '✗';

  return `
    <div class="instance-row status-${statusClass}">
      <div class="instance-main">
        <div class="instance-status-icon">${statusIcon}</div>
        <div class="instance-info">
          <div class="instance-id">${escapeHtml(instance.instanceId)}</div>
          <div class="instance-endpoint">
            <a href="${escapeHtml(instance.httpEndpoint)}" target="_blank" rel="noopener">
              ${escapeHtml(instance.httpEndpoint)}
            </a>
          </div>
        </div>
      </div>
      <div class="instance-stats">
        <div class="instance-stat">
          <span class="stat-label">Last Seen:</span>
          <span class="stat-value">${escapeHtml(instance.timeSinceLastSeen)}</span>
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

function formatDuration(timeSpan) {
  // timeSpan is like { ticks: 123456, days: 0, hours: 0, ... }
  if (!timeSpan) return 'N/A';

  const totalMs = timeSpan.totalMilliseconds || 0;
  if (totalMs < 1) return `${Math.round(totalMs * 1000)}μs`;
  if (totalMs < 1000) return `${Math.round(totalMs)}ms`;
  if (totalMs < 60000) return `${(totalMs / 1000).toFixed(2)}s`;

  const minutes = Math.floor(totalMs / 60000);
  const seconds = Math.round((totalMs % 60000) / 1000);
  return `${minutes}m ${seconds}s`;
}

// ========================================
// Initialization
// ========================================

async function initialize() {
  console.log('Koan Admin initializing...');

  loadState();
  setupEventHandlers();

  // Initial data fetch
  await fetchAPIData();

  // Handle initial route
  handleRouteChange();

  // Render content
  renderContent();

  // Start auto-refresh if enabled
  if (AppState.autoRefresh) {
    startAutoRefresh();
  }

  // Update timestamps periodically
  setInterval(updateGeneratedAt, 5000);

  console.log('Koan Admin initialized');
}

// Start the app when DOM is ready
if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', initialize);
} else {
  initialize();
}
