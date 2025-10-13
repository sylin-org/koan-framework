const STATUS_ENDPOINT = 'api/status';
const LAUNCHKIT_METADATA_ENDPOINT = 'api/launchkit/metadata';
const LAUNCHKIT_BUNDLE_ENDPOINT = 'api/launchkit/bundle';

const environmentContainer = document.querySelector('.environment-content');
const modulesContainer = document.querySelector('.modules-content');
const healthContainer = document.querySelector('.health-content');
const launchKitContainer = document.querySelector('.launchkit-content');
const generatedAtElement = document.getElementById('generated-at');

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
  const processStart = new Date(environment.processStart ?? environment.ProcessStart ?? Date.now());
  const uptimeMs = Date.now() - processStart.getTime();
  const uptime = formatUptime(Math.max(0, uptimeMs));

  environmentContainer.innerHTML = '';

  const envLine = document.createElement('div');
  envLine.innerHTML = `
    <span class="chip">${environment.environmentName}</span>
    <span class="chip">Uptime: ${uptime}</span>
  `;
  environmentContainer.appendChild(envLine);

  const flags = document.createElement('div');
  flags.innerHTML = `
    <span class="chip">Dev: ${environment.isDevelopment ? 'Yes' : 'No'}</span>
    <span class="chip">Prod: ${environment.isProduction ? 'Yes' : 'No'}</span>
    <span class="chip">Staging: ${environment.isStaging ? 'Yes' : 'No'}</span>
    <span class="chip">CI: ${environment.isCi ? 'Yes' : 'No'}</span>
    <span class="chip">Container: ${environment.inContainer ? 'Yes' : 'No'}</span>
  `;
  environmentContainer.appendChild(flags);
}

function renderModules(manifestSummary) {
  modulesContainer.innerHTML = '';
  if (!manifestSummary || !Array.isArray(manifestSummary.modules) || manifestSummary.modules.length === 0) {
    modulesContainer.innerHTML = '<p class="loading">No modules reported yet.</p>';
    return;
  }

  manifestSummary.modules.forEach(module => {
    const row = document.createElement('div');
    const version = module.version ? `v${module.version}` : 'unversioned';
    row.innerHTML = `
      <div><strong>${module.name}</strong> <span class="chip">${version}</span></div>
      <div class="muted">Settings: ${module.settingCount} • Notes: ${module.noteCount}</div>
    `;
    modulesContainer.appendChild(row);
  });
}

function renderHealth(health) {
  healthContainer.innerHTML = '';
  if (!health || !Array.isArray(health.components) || health.components.length === 0) {
    healthContainer.innerHTML = '<p class="loading">No health components registered.</p>';
    return;
  }

  const grid = document.createElement('div');
  grid.className = 'health-grid';

  health.components.forEach(component => {
    const item = document.createElement('div');
    const status = (component.status ?? '').toString().toLowerCase();
    item.className = `health-item ${statusClass(status)}`;
    item.innerHTML = `
      <span>${component.component}</span>
      <span>${component.status}</span>
    `;
    grid.appendChild(item);
  });

  healthContainer.appendChild(grid);
}

function statusClass(status) {
  if (status.includes('healthy') || status.includes('up')) {
    return 'success';
  }
  if (status.includes('degraded') || status.includes('warn')) {
    return 'warn';
  }
  return 'error';
}

async function renderLaunchKit(status) {
  launchKitContainer.innerHTML = '';

  if (!status.features?.launchKitEnabled) {
    launchKitContainer.innerHTML = '<p class="launchkit-disabled">LaunchKit is disabled for this host.</p>';
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

  const profileLabel = document.createElement('label');
  profileLabel.textContent = 'Profile';
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
  profileLabel.appendChild(profileSelect);
  form.appendChild(profileLabel);

  const toggles = [
    { id: 'include-appsettings', prop: 'includeAppSettings', label: 'Appsettings bundle', enabled: metadata.supportsAppSettings, checked: metadata.supportsAppSettings },
    { id: 'include-compose', prop: 'includeCompose', label: 'Docker Compose bundle', enabled: metadata.supportsCompose, checked: metadata.supportsCompose },
    { id: 'include-aspire', prop: 'includeAspire', label: 'Aspire manifest', enabled: metadata.supportsAspire, checked: metadata.supportsAspire },
    { id: 'include-manifest', prop: 'includeManifest', label: 'Diagnostic manifest', enabled: metadata.supportsManifest, checked: metadata.supportsManifest },
    { id: 'include-readme', prop: 'includeReadme', label: 'README guidance', enabled: metadata.supportsReadme, checked: metadata.supportsReadme }
  ];

  const checkboxGroup = document.createElement('div');
  toggles.forEach(toggle => {
    const wrapper = document.createElement('label');
    wrapper.innerHTML = `<input type="checkbox" id="${toggle.id}" ${toggle.enabled && toggle.checked ? 'checked' : ''} ${toggle.enabled ? '' : 'disabled'} /> ${toggle.label}`;
    checkboxGroup.appendChild(wrapper);
  });
  form.appendChild(checkboxGroup);

  const openApiSection = document.createElement('div');
  if (metadata.openApiClientTemplates?.length) {
    const heading = document.createElement('label');
    heading.textContent = 'OpenAPI Clients';
    openApiSection.appendChild(heading);

    metadata.openApiClientTemplates.forEach(client => {
      const wrapper = document.createElement('label');
      wrapper.innerHTML = `<input type="checkbox" value="${client}" checked /> ${client}`;
      openApiSection.appendChild(wrapper);
    });
  } else {
    openApiSection.innerHTML = '<p class="loading">No OpenAPI client templates configured.</p>';
  }
  form.appendChild(openApiSection);

  const submit = document.createElement('button');
  submit.type = 'submit';
  submit.textContent = 'Download bundle';
  form.appendChild(submit);

  const message = document.createElement('p');
  message.className = 'launchkit-message loading';
  message.textContent = '';
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
    if (input) {
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
  const generatedAt = new Date(manifestSummary.generatedAtUtc ?? manifestSummary.GeneratedAtUtc ?? Date.now());
  generatedAtElement.textContent = `Summary generated ${generatedAt.toLocaleString()}`;
}

async function init() {
  try {
    const status = await fetchJson(STATUS_ENDPOINT);
    renderEnvironment(status.environment);
    renderModules(status.manifest);
    renderHealth(status.health);
    renderGeneratedAt(status.manifest);
    await renderLaunchKit(status);
  } catch (err) {
    console.error(err);
    const message = `<p class="error">Unable to load admin status. ${err.message}</p>`;
    environmentContainer.innerHTML = message;
    modulesContainer.innerHTML = '';
    healthContainer.innerHTML = '';
    launchKitContainer.innerHTML = '';
  }
}

init();
