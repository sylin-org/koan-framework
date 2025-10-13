const STATUS_ENDPOINT = 'api/status';
const HEALTH_ENDPOINT = 'api/health';
const LAUNCHKIT_METADATA_ENDPOINT = 'api/launchkit/metadata';
const LAUNCHKIT_BUNDLE_ENDPOINT = 'api/launchkit/bundle';
const REFRESH_INTERVAL_MS = 30000;

const environmentContainer = document.querySelector('.environment-content');
const capabilitiesContainer = document.querySelector('.capabilities-content');
const modulesContainer = document.querySelector('.modules-content');
const healthContainer = document.querySelector('.health-content');
const notesContainer = document.querySelector('.notes-content');
const launchKitContainer = document.querySelector('.launchkit-content');
const generatedAtElement = document.getElementById('generated-at');
const refreshButton = document.getElementById('refresh-btn');
const autoRefreshToggle = document.getElementById('auto-refresh');

let refreshTimer;

async function fetchJson(url) {
  const response = await fetch(url, {
    headers: { 'Accept': 'application/json' }
  });
  if (!response.ok) {
    const detail = await safeReadText(response);
    throw new Error(`Request to ${url} failed: ${response.status} ${response.statusText}${detail ? ` - ${detail}` : ''}`);
  }
  return response.json();
}

async function safeReadText(response) {
  try {
    return await response.text();
  } catch {
    return '';
  }
}

function renderEnvironment(environment) {
  const processStartRaw = environment?.processStart ?? environment?.ProcessStart;
  const processStart = processStartRaw ? new Date(processStartRaw) : new Date();
  const uptimeMs = Date.now() - processStart.getTime();

  environmentContainer.innerHTML = '';

  const primary = document.createElement('div');
  primary.className = 'chip-row';
  primary.innerHTML = `
    <span class="chip">${environment?.environmentName ?? 'Unknown environment'}</span>
    <span class="chip">Uptime: ${formatUptime(Math.max(0, uptimeMs))}</span>
  `;
  environmentContainer.appendChild(primary);

  const meta = document.createElement('div');
  meta.className = 'chip-row';
  meta.innerHTML = `
    <span class="chip">Dev: ${environment?.isDevelopment ? 'Yes' : 'No'}</span>
    <span class="chip">Prod: ${environment?.isProduction ? 'Yes' : 'No'}</span>
    <span class="chip">Staging: ${environment?.isStaging ? 'Yes' : 'No'}</span>
    <span class="chip">CI: ${environment?.isCi ? 'Yes' : 'No'}</span>
    <span class="chip">Container: ${environment?.inContainer ? 'Yes' : 'No'}</span>
  `;
  environmentContainer.appendChild(meta);
}

function renderCapabilities(features) {
  capabilitiesContainer.innerHTML = '';
  if (!features) {
    capabilitiesContainer.innerHTML = '<p class="loading">Feature snapshot unavailable.</p>';
    return;
  }

  const flagGrid = document.createElement('div');
  flagGrid.className = 'capability-grid';

  const flags = [
    { label: 'Web UI', value: features.webEnabled },
    { label: 'Console', value: features.consoleEnabled },
    { label: 'Manifest API', value: features.manifestExposed },
    { label: 'Destructive operations', value: features.allowDestructiveOperations },
    { label: 'Log transcript download', value: features.allowLogTranscriptDownload },
    { label: 'LaunchKit', value: features.launchKitEnabled },
    { label: 'Dot prefix allowed', value: features.dotPrefixAllowedInCurrentEnvironment }
  ];

  flags.forEach(flag => {
    const item = document.createElement('div');
    item.className = 'capability-item';
    item.innerHTML = `
      <span>${flag.label}</span>
      <span class="badge ${flag.value ? 'on' : 'off'}">${flag.value ? 'Enabled' : 'Disabled'}</span>
    `;
    flagGrid.appendChild(item);
  });

  capabilitiesContainer.appendChild(flagGrid);

  if (features.routes) {
    const routes = document.createElement('div');
    routes.className = 'routes';
    routes.innerHTML = `
      <div><strong>UI root</strong><span>${features.routes.rootPath ?? '/'}</span></div>
      <div><strong>API root</strong><span>${features.routes.apiPath ?? '/api'}</span></div>
      <div><strong>LaunchKit</strong><span>${features.routes.launchKitPath ?? '/api/launchkit'}</span></div>
      <div><strong>Health</strong><span>${features.routes.healthPath ?? '/api/health'}</span></div>
    `;
    capabilitiesContainer.appendChild(routes);
  }
}

function renderModules(modules, manifestSummary) {
  modulesContainer.innerHTML = '';
  const source = Array.isArray(modules) && modules.length ? modules : manifestSummary?.modules;

  if (!source || !source.length) {
    modulesContainer.innerHTML = '<p class="loading">No modules reported yet.</p>';
    return;
  }

  source.forEach(module => {
    const details = document.createElement('details');
    details.className = 'module-item';

    const summary = document.createElement('summary');
    const version = module.version ? `v${module.version}` : 'unversioned';
    const noteCount = Array.isArray(module.notes) ? module.notes.length : module.noteCount ?? 0;
    const settingCount = Array.isArray(module.settings) ? module.settings.length : module.settingCount ?? 0;
    summary.innerHTML = `
      <span class="module-name">${module.name}</span>
      <span class="module-meta">${version} · ${settingCount} settings · ${noteCount} notes</span>
    `;
    details.appendChild(summary);

    const inner = document.createElement('div');
    inner.className = 'module-body';

    const settingsList = Array.isArray(module.settings) && module.settings.length
      ? module.settings.map(setting => `
          <div class="module-setting">
            <span class="key">${setting.key}</span>
            <span class="value ${setting.secret ? 'secret' : ''}">${setting.value ?? ''}</span>
            ${setting.secret ? '<span class="tag">secret</span>' : ''}
          </div>
        `).join('')
      : '<p class="muted">No settings captured.</p>';

    const notesList = Array.isArray(module.notes) && module.notes.length
      ? `<ul class="module-notes">${module.notes.map(note => `<li>${note}</li>`).join('')}</ul>`
      : '<p class="muted">No notes recorded.</p>';

    inner.innerHTML = `
      <div class="module-section">
        <h3>Settings</h3>
        ${settingsList}
      </div>
      <div class="module-section">
        <h3>Notes</h3>
        ${notesList}
      </div>
    `;

    details.appendChild(inner);
    modulesContainer.appendChild(details);
  });
}

function renderHealth(health) {
  healthContainer.innerHTML = '';
  if (!health) {
    healthContainer.innerHTML = '<p class="loading">Health snapshot unavailable.</p>';
    return;
  }

  const overall = document.createElement('div');
  overall.className = `overall ${statusClass((health.overall ?? '').toString())}`;
  const computed = health.computedAtUtc ? new Date(health.computedAtUtc) : null;
  overall.innerHTML = `
    <span class="badge ${statusClass((health.overall ?? '').toString())}">${health.overall ?? 'Unknown'}</span>
    <span>${computed ? `Computed ${computed.toLocaleString()}` : 'No timestamp available'}</span>
  `;
  healthContainer.appendChild(overall);

  if (!Array.isArray(health.components) || !health.components.length) {
    const empty = document.createElement('p');
    empty.className = 'loading';
    empty.textContent = 'No health components registered.';
    healthContainer.appendChild(empty);
    return;
  }

  health.components.forEach(component => {
    const detail = document.createElement('details');
    detail.className = 'health-item';
    detail.open = component.status?.toString().toLowerCase().includes('unhealthy');

    const summary = document.createElement('summary');
    const status = (component.status ?? '').toString();
    summary.innerHTML = `
      <span class="health-name">${component.component}</span>
      <span class="badge ${statusClass(status.toLowerCase())}">${status}</span>
    `;
    detail.appendChild(summary);

    const body = document.createElement('div');
    body.className = 'health-body';

    if (component.message) {
      const message = document.createElement('p');
      message.className = 'health-message';
      message.textContent = component.message;
      body.appendChild(message);
    }

    const timestamp = component.timestampUtc ? new Date(component.timestampUtc) : null;
    const stamp = document.createElement('p');
    stamp.className = 'muted';
    stamp.textContent = timestamp ? `Observed ${timestamp.toLocaleString()}` : 'Timestamp unavailable';
    body.appendChild(stamp);

    const facts = component.facts && Object.keys(component.facts).length
      ? Object.entries(component.facts).map(([key, value]) => `<div><span class="fact-key">${key}</span><span>${value}</span></div>`).join('')
      : '<p class="muted">No diagnostic facts supplied.</p>';

    const factBlock = document.createElement('div');
    factBlock.className = 'health-facts';
    factBlock.innerHTML = facts;
    body.appendChild(factBlock);

    detail.appendChild(body);
    healthContainer.appendChild(detail);
  });
}

function renderNotes(notes, modules) {
  notesContainer.innerHTML = '';
  const entries = Array.isArray(notes) && notes.length
    ? notes
    : (Array.isArray(modules) ? modules.flatMap(module => (module.notes ?? []).map(note => ({ module: module.name, note }))) : []);

  if (!entries.length) {
    notesContainer.innerHTML = '<p class="loading">No startup notes recorded.</p>';
    return;
  }

  const list = document.createElement('ul');
  list.className = 'notes-list';
  entries.forEach(entry => {
    const item = document.createElement('li');
    item.innerHTML = `<strong>${entry.module}</strong><span>${entry.note}</span>`;
    list.appendChild(item);
  });
  notesContainer.appendChild(list);
}

async function renderLaunchKit(status) {
  launchKitContainer.innerHTML = '';

  if (!status?.features?.launchKitEnabled) {
    launchKitContainer.innerHTML = '<p class="launchkit-disabled">LaunchKit is disabled for this host. Set Koan:Admin:EnableLaunchKit=true to enable bundle generation.</p>';
    return;
  }

  try {
    const metadata = await fetchJson(LAUNCHKIT_METADATA_ENDPOINT);
    launchKitContainer.appendChild(buildLaunchKitForm(metadata));
  } catch (err) {
    console.error(err);
    launchKitContainer.innerHTML = `<p class="error">Unable to load LaunchKit metadata. ${err.message}</p>`;
  }
}

function buildLaunchKitForm(metadata) {
  const form = document.createElement('form');
  form.className = 'launchkit-form';

  const profileField = document.createElement('label');
  profileField.className = 'field';
  profileField.innerHTML = '<span>Profile</span>';
  const profileSelect = document.createElement('select');
  const profiles = metadata.availableProfiles && metadata.availableProfiles.length
    ? metadata.availableProfiles
    : [metadata.defaultProfile ?? 'Default'];
  profiles.forEach(profile => {
    const option = document.createElement('option');
    option.value = profile;
    option.textContent = profile;
    if (profile === metadata.defaultProfile) {
      option.selected = true;
    }
    profileSelect.appendChild(option);
  });
  profileField.appendChild(profileSelect);
  form.appendChild(profileField);

  const toggles = [
    { id: 'include-appsettings', prop: 'includeAppSettings', label: 'Appsettings bundle', description: 'Exports environment scoped configuration files.', supported: metadata.supportsAppSettings },
    { id: 'include-compose', prop: 'includeCompose', label: 'Docker Compose bundle', description: 'Generates docker-compose assets for local orchestration.', supported: metadata.supportsCompose },
    { id: 'include-aspire', prop: 'includeAspire', label: 'Aspire manifest', description: 'Adds Aspire manifest files for AppHost projects.', supported: metadata.supportsAspire },
    { id: 'include-manifest', prop: 'includeManifest', label: 'Diagnostic manifest', description: 'Includes the sanitized Koan Admin manifest summary.', supported: metadata.supportsManifest },
    { id: 'include-readme', prop: 'includeReadme', label: 'README guidance', description: 'Adds walkthrough README tailored to the selected profile.', supported: metadata.supportsReadme }
  ];

  const toggleGroup = document.createElement('div');
  toggleGroup.className = 'toggle-group';
  toggles.forEach(toggle => {
    const wrapper = document.createElement('label');
    wrapper.className = toggle.supported ? 'toggle-option' : 'toggle-option disabled';
    wrapper.innerHTML = `
      <input type="checkbox" id="${toggle.id}" ${toggle.supported ? 'checked' : 'disabled'} />
      <div>
        <span>${toggle.label}</span>
        <small>${toggle.description}</small>
      </div>
    `;
    toggleGroup.appendChild(wrapper);
  });
  form.appendChild(toggleGroup);

  const openApiSection = document.createElement('div');
  openApiSection.className = 'openapi';
  if (metadata.openApiClientTemplates?.length) {
    openApiSection.innerHTML = '<p class="section-title">OpenAPI clients</p>';
    metadata.openApiClientTemplates.forEach(client => {
      const wrapper = document.createElement('label');
      wrapper.className = 'toggle-option';
      wrapper.innerHTML = `<input type="checkbox" value="${client}" checked /><div><span>${client}</span></div>`;
      openApiSection.appendChild(wrapper);
    });
  } else {
    openApiSection.innerHTML = '<p class="muted">No OpenAPI client templates configured.</p>';
  }
  form.appendChild(openApiSection);

  const submit = document.createElement('button');
  submit.type = 'submit';
  submit.textContent = 'Download bundle';
  form.appendChild(submit);

  const message = document.createElement('p');
  message.className = 'launchkit-message';
  form.appendChild(message);

  form.addEventListener('submit', async event => {
    event.preventDefault();
    try {
      message.className = 'launchkit-message loading';
      message.textContent = 'Preparing bundle…';

      const payload = buildPayload(profileSelect, toggles, openApiSection);
      const response = await fetch(LAUNCHKIT_BUNDLE_ENDPOINT, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
      });

      if (!response.ok) {
        const detail = await safeReadText(response);
        throw new Error(`Bundle generation failed: ${response.status} ${response.statusText}${detail ? ` - ${detail}` : ''}`);
      }

      const blob = await response.blob();
      const filename = extractFileName(response.headers.get('Content-Disposition')) || `koan-launchkit-${payload.profile}.zip`;
      downloadBlob(blob, filename);

      message.className = 'launchkit-message';
      message.textContent = `Bundle ready: ${filename}`;
    } catch (err) {
      console.error(err);
      message.className = 'launchkit-message error';
      message.textContent = err.message;
    }
  });

  return form;
}

function buildPayload(profileSelect, toggles, openApiSection) {
  const payload = {
    profile: profileSelect.value,
    openApiClients: []
  };

  toggles.forEach(toggle => {
    const input = document.getElementById(toggle.id);
    if (input && !input.disabled) {
      payload[toggle.prop] = input.checked;
    }
  });

  const clientInputs = openApiSection.querySelectorAll('input[type="checkbox"]');
  clientInputs.forEach(input => {
    if (input.checked) {
      payload.openApiClients.push(input.value);
    }
  });

  if (!payload.openApiClients.length) {
    delete payload.openApiClients;
  }

  return payload;
}

function downloadBlob(blob, fileName) {
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = fileName;
  document.body.appendChild(anchor);
  anchor.click();
  setTimeout(() => {
    URL.revokeObjectURL(url);
    anchor.remove();
  }, 0);
}

function extractFileName(contentDisposition) {
  if (!contentDisposition) return null;
  const match = /filename="?([^";]+)"?/i.exec(contentDisposition);
  return match ? match[1] : null;
}

function formatUptime(milliseconds) {
  const totalSeconds = Math.floor(milliseconds / 1000);
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;
  if (hours > 0) {
    return `${hours}h ${minutes}m`;
  }
  if (minutes > 0) {
    return `${minutes}m ${seconds}s`;
  }
  return `${seconds}s`;
}

function renderGeneratedAt(manifestSummary) {
  if (!manifestSummary) {
    generatedAtElement.textContent = '';
    return;
  }
  const generatedAt = manifestSummary.generatedAtUtc ?? manifestSummary.GeneratedAtUtc;
  const timestamp = generatedAt ? new Date(generatedAt) : new Date();
  generatedAtElement.textContent = `Summary generated ${timestamp.toLocaleString()}`;
}

function statusClass(status) {
  const normalized = status.toLowerCase();
  if (normalized.includes('fail') || normalized.includes('down') || normalized.includes('unhealthy')) {
    return 'error';
  }
  if (normalized.includes('healthy') || normalized.includes('up')) {
    return 'success';
  }
  if (normalized.includes('degraded') || normalized.includes('warn')) {
    return 'warn';
  }
  return 'warn';
}

function clearRefreshTimer() {
  if (refreshTimer) {
    clearInterval(refreshTimer);
    refreshTimer = undefined;
  }
}

async function refreshStatus(manual = false) {
  try {
    if (manual && refreshButton) {
      refreshButton.disabled = true;
    }

    const status = await fetchJson(STATUS_ENDPOINT);
    renderEnvironment(status.environment);
    renderCapabilities(status.features);
    renderModules(status.modules, status.manifest);
    renderHealth(status.health);
    renderNotes(status.startupNotes, status.modules);
    renderGeneratedAt(status.manifest);
    await renderLaunchKit(status);
  } catch (err) {
    console.error(err);
    const message = `<p class="error">Unable to load admin status. ${err.message}</p>`;
    environmentContainer.innerHTML = message;
    capabilitiesContainer.innerHTML = '';
    modulesContainer.innerHTML = '';
    healthContainer.innerHTML = '';
    notesContainer.innerHTML = '';
    launchKitContainer.innerHTML = '';
  } finally {
    if (manual && refreshButton) {
      refreshButton.disabled = false;
    }
  }
}

async function refreshHealthOnly() {
  try {
    const health = await fetchJson(HEALTH_ENDPOINT);
    renderHealth(health);
  } catch (err) {
    console.error(err);
  }
}

function enableAutoRefresh(enabled) {
  clearRefreshTimer();
  if (!enabled) {
    return;
  }

  refreshTimer = setInterval(() => {
    refreshStatus(false);
    refreshHealthOnly();
  }, REFRESH_INTERVAL_MS);
}

if (refreshButton) {
  refreshButton.addEventListener('click', () => refreshStatus(true));
}

if (autoRefreshToggle) {
  autoRefreshToggle.addEventListener('change', event => {
    enableAutoRefresh(event.target.checked);
    if (event.target.checked) {
      refreshStatus(false);
    }
  });
}

refreshStatus(false);
