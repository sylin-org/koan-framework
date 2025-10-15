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

function formatSettingSource(source) {
  if (!source) {
    return '';
  }
  const value = source.toString();
  return value.replace(/([a-z])([A-Z])/g, '$1 $2');
}

function renderSettingConsumers(consumers) {
  if (!Array.isArray(consumers) || consumers.length === 0) {
    return '';
  }

  const chips = consumers
    .map(consumer => `<span class="consumer-chip">${consumer}</span>`)
    .join('');

  return `<div class="setting-consumers">${chips}</div>`;
}

function pluralize(label, count) {
  if (count === 1) {
    return `${count} ${label}`;
  }
  return `${count} ${label}s`;
}

function formatModuleMeta(version, settingCount, noteCount, toolCount) {
  const parts = [];
  parts.push(version ? `v${version}` : 'unversioned');
  parts.push(pluralize('setting', settingCount));
  parts.push(pluralize('note', noteCount));
  if (toolCount) {
    parts.push(pluralize('tool', toolCount));
  }
  return parts.join(' · ');
}

function applyModuleStyling(element, module, fallbackModuleClass) {
  const moduleClass = module.moduleClass ?? fallbackModuleClass ?? 'module-general';
  if (!element.classList.contains(moduleClass)) {
    element.classList.add(moduleClass);
  }
  if (module.colorHex) {
    element.style.setProperty('--module-color-hex', module.colorHex);
  }
  if (module.colorRgb) {
    element.style.setProperty('--module-color-rgb', module.colorRgb);
  }
}

function createModuleStatic(module, fallbackModuleClass) {
  const container = document.createElement('div');
  container.className = 'module-item module-static';
  applyModuleStyling(container, module, fallbackModuleClass);

  const header = document.createElement('div');
  header.className = 'module-static-header';

  const nameSpan = document.createElement('span');
  nameSpan.className = 'module-name';
  nameSpan.innerHTML = `<span class="module-icon">${module.icon ?? '🧩'}</span>${module.name ?? 'Unnamed module'}`;

  const badge = document.createElement('span');
  badge.className = 'module-badge';
  badge.textContent = 'Stub module';

  const settingCount = Array.isArray(module.settings) ? module.settings.length : module.settingCount ?? 0;
  const noteCount = Array.isArray(module.notes) ? module.notes.length : module.noteCount ?? 0;
  const toolCount = Array.isArray(module.tools) ? module.tools.length : module.toolCount ?? 0;
  const metaSpan = document.createElement('span');
  metaSpan.className = 'module-meta';
  metaSpan.textContent = formatModuleMeta(module.version, settingCount, noteCount, toolCount);

  header.appendChild(nameSpan);
  header.appendChild(badge);
  header.appendChild(metaSpan);
  container.appendChild(header);

  if (module.description) {
    const desc = document.createElement('p');
    desc.className = 'module-description';
    desc.textContent = module.description;
    container.appendChild(desc);
  }

  const empty = document.createElement('div');
  empty.className = 'module-static-empty';
  empty.innerHTML = `
    <p class="muted">No settings, notes, or tools reported.</p>
    <p class="muted secondary">Feature surface is available for future expansion.</p>
  `;
  container.appendChild(empty);

  return container;
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

function buildPillarGroups(modules, configurationSummary) {
  const modulesByPillar = new Map();

  modules.forEach(module => {
    const key = module.pillar ?? 'General';
    if (!modulesByPillar.has(key)) {
      modulesByPillar.set(key, []);
    }
    modulesByPillar.get(key).push(module);
  });

  const groups = [];
  const summaryList = Array.isArray(configurationSummary?.pillars)
    ? configurationSummary.pillars.slice()
    : [];

  const normalizeSort = (a, b) => a.name.localeCompare(b.name, undefined, { sensitivity: 'base' });

  summaryList.forEach(summary => {
    const pillarKey = summary.pillar ?? 'General';
    const moduleList = modulesByPillar.get(pillarKey) ?? [];
    moduleList.sort(normalizeSort);

    const moduleClassFallback = moduleList[0]?.moduleClass ?? 'module-general';
    const moduleToolCount = moduleList.reduce((total, item) => total + (Array.isArray(item.tools) ? item.tools.length : 0), 0);

    groups.push({
      pillar: pillarKey,
      pillarClass: summary.pillarClass ?? moduleList[0]?.pillarClass ?? 'pillar-general',
      moduleClass: moduleClassFallback,
      icon: summary.icon ?? moduleList[0]?.icon ?? '🧩',
      colorHex: summary.colorHex ?? moduleList[0]?.colorHex ?? '#38bdf8',
      colorRgb: summary.colorRgb ?? moduleList[0]?.colorRgb ?? '56, 189, 248',
      modules: moduleList,
      moduleCount: moduleList.length || (summary.moduleCount ?? 0),
      settingCount: moduleList.length
        ? moduleList.reduce((total, item) => total + (item.settings?.length ?? 0), 0)
        : summary.settingCount ?? 0,
      noteCount: moduleList.length
        ? moduleList.reduce((total, item) => total + (item.notes?.length ?? 0), 0)
        : summary.noteCount ?? 0,
      toolCount: moduleList.length ? moduleToolCount : 0
    });

    modulesByPillar.delete(pillarKey);
  });

  modulesByPillar.forEach((moduleList, pillarKey) => {
    moduleList.sort(normalizeSort);
    const moduleToolCount = moduleList.reduce((total, item) => total + (Array.isArray(item.tools) ? item.tools.length : 0), 0);
    groups.push({
      pillar: pillarKey,
      pillarClass: moduleList[0]?.pillarClass ?? 'pillar-general',
      moduleClass: moduleList[0]?.moduleClass ?? 'module-general',
      icon: moduleList[0]?.icon ?? '🧩',
      colorHex: moduleList[0]?.colorHex ?? '#38bdf8',
      colorRgb: moduleList[0]?.colorRgb ?? '56, 189, 248',
      modules: moduleList,
      moduleCount: moduleList.length,
      settingCount: moduleList.reduce((total, item) => total + (item.settings?.length ?? 0), 0),
      noteCount: moduleList.reduce((total, item) => total + (item.notes?.length ?? 0), 0),
      toolCount: moduleToolCount
    });
  });

  groups.sort((left, right) => {
    if (right.moduleCount !== left.moduleCount) {
      return right.moduleCount - left.moduleCount;
    }
    return left.pillar.localeCompare(right.pillar, undefined, { sensitivity: 'base' });
  });

  return groups;
}

function createModuleDetails(module, fallbackModuleClass) {
  if (module.isStub) {
    return createModuleStatic(module, fallbackModuleClass);
  }

  const details = document.createElement('details');
  details.className = 'module-item';
  applyModuleStyling(details, module, fallbackModuleClass);

  const icon = module.icon ?? '🧩';
  const summary = document.createElement('summary');
  const settingCount = Array.isArray(module.settings) ? module.settings.length : module.settingCount ?? 0;
  const noteCount = Array.isArray(module.notes) ? module.notes.length : module.noteCount ?? 0;
  const toolCount = Array.isArray(module.tools) ? module.tools.length : module.toolCount ?? 0;

  const nameSpan = document.createElement('span');
  nameSpan.className = 'module-name';
  nameSpan.innerHTML = `<span class="module-icon">${icon}</span>${module.name ?? 'Unnamed module'}`;

  const metaSpan = document.createElement('span');
  metaSpan.className = 'module-meta';
  metaSpan.textContent = formatModuleMeta(module.version, settingCount, noteCount, toolCount);

  summary.appendChild(nameSpan);
  summary.appendChild(metaSpan);
  details.appendChild(summary);

  const inner = document.createElement('div');
  inner.className = 'module-body';

  const settingsList = Array.isArray(module.settings) && module.settings.length
    ? module.settings.map(setting => {
        const label = typeof setting.label === 'string' && setting.label.trim().length
          ? setting.label.trim()
          : setting.key;
        const description = typeof setting.description === 'string' ? setting.description.trim() : '';
        const sourceValue = formatSettingSource(setting.source);
        const sourceLabel = sourceValue && sourceValue.toLowerCase() !== 'unknown' ? sourceValue : '';
        const sourceKey = typeof setting.sourceKey === 'string' ? setting.sourceKey : '';
        const tags = [];
        if (sourceLabel) {
          tags.push(`<span class="tag tag-source">${sourceLabel}</span>`);
        }
        if (setting.secret) {
          tags.push('<span class="tag tag-secret">secret</span>');
        }
        const keyBadge = `<code class="setting-key">${setting.key}</code>`;
        const sourceKeyMarkup = sourceKey ? `<code class="setting-source-key">${sourceKey}</code>` : '';
        const consumers = renderSettingConsumers(setting.consumers);
        const metaParts = [keyBadge];
        if (sourceKeyMarkup) {
          metaParts.push(sourceKeyMarkup);
        }
        if (tags.length) {
          metaParts.push(...tags);
        }
        if (consumers) {
          metaParts.push(consumers);
        }
        const hasMeta = metaParts.some(part => part && part.length);
        const descriptionTitle = description.replace(/"/g, '&quot;');
        const descriptionMarkup = description
          ? `<span class="setting-description" title="${descriptionTitle}">${description}</span>`
          : '';
        const meta = hasMeta
          ? `<div class="setting-meta">${metaParts.join('')}</div>`
          : '';

        return `
        <div class="module-setting">
          <div class="setting-header">
            <div class="setting-text">
              <span class="setting-label">${label}</span>
              ${descriptionMarkup}
            </div>
            <span class="value ${setting.secret ? 'secret' : ''}">${setting.value ?? ''}</span>
          </div>
          ${meta}
        </div>
        `;
      }).join('')
    : '<p class="muted">No settings captured.</p>';

  const notesList = Array.isArray(module.notes) && module.notes.length
    ? `<ul class="module-notes">${module.notes.map(note => `<li>${note}</li>`).join('')}</ul>`
    : '<p class="muted">No notes recorded.</p>';

  const tools = Array.isArray(module.tools) ? module.tools : [];
  const toolsList = tools.length
    ? `<ul class="module-tools">${tools.map(tool => {
        const capabilityTag = tool.capability ? `<span class="tag tag-capability">${tool.capability}</span>` : '';
        const headerTags = capabilityTag ? `<span class="tool-tags">${capabilityTag}</span>` : '';
        const normalizedRoute = typeof tool.route === 'string' && tool.route.length
          ? (tool.route.startsWith('/') || /^https?:/i.test(tool.route) ? tool.route : `/${tool.route}`)
          : '';
        const routeMarkup = normalizedRoute
          ? `<a class="tool-route" href="${normalizedRoute}" target="_blank" rel="noopener noreferrer">${normalizedRoute}</a>`
          : '<span class="tool-route muted">Route unavailable</span>';
        const descriptionMarkup = tool.description
          ? `<p class="tool-description">${tool.description}</p>`
          : '';
        return `
          <li class="module-tool">
            <div class="module-tool-header">
              <span class="tool-name">${tool.name ?? 'Unnamed tool'}</span>
              ${headerTags}
            </div>
            <div class="module-tool-route">${routeMarkup}</div>
            ${descriptionMarkup}
          </li>
        `;
      }).join('')}</ul>`
    : '<p class="muted">No admin tools registered.</p>';

  inner.innerHTML = `
    <div class="module-section">
      <h3>Settings</h3>
      ${settingsList}
    </div>
    <div class="module-section">
      <h3>Notes</h3>
      ${notesList}
    </div>
    <div class="module-section">
      <h3>Tools</h3>
      ${toolsList}
    </div>
  `;

  if (module.description) {
    const descriptionNode = document.createElement('p');
    descriptionNode.className = 'module-description';
    descriptionNode.textContent = module.description;
    inner.prepend(descriptionNode);
  }

  details.appendChild(inner);
  return details;
}

function renderModules(modules, configurationSummary) {
  modulesContainer.innerHTML = '';

  if (!Array.isArray(modules) || !modules.length) {
    modulesContainer.innerHTML = '<p class="loading">No modules reported yet.</p>';
    return;
  }

  const groups = buildPillarGroups(modules, configurationSummary);

  groups.forEach((group, index) => {
    const wrapper = document.createElement('details');
    wrapper.className = 'pillar-group';
    if (group.pillarClass) {
      wrapper.classList.add(group.pillarClass);
    }
    if (group.colorHex) {
      wrapper.style.setProperty('--pillar-color-hex', group.colorHex);
    }
    if (group.colorRgb) {
      wrapper.style.setProperty('--pillar-color-rgb', group.colorRgb);
    }
    if (index === 0) {
      wrapper.open = true;
    }

    const summary = document.createElement('summary');

    const name = document.createElement('span');
    name.className = 'pillar-name';
    name.innerHTML = `<span class="module-icon">${group.icon ?? '🧩'}</span>${group.pillar}`;

    const counts = document.createElement('span');
    counts.className = 'pillar-counts';
    const countParts = [
      pluralize('module', group.moduleCount),
      pluralize('setting', group.settingCount)
    ];
    if (group.toolCount) {
      countParts.push(pluralize('tool', group.toolCount));
    }
    if (group.noteCount) {
      countParts.push(pluralize('note', group.noteCount));
    }
    counts.textContent = countParts.join(' · ');

    summary.appendChild(name);
    summary.appendChild(counts);
    wrapper.appendChild(summary);

    const body = document.createElement('div');
    body.className = 'pillar-body';

    if (!group.modules.length) {
      const empty = document.createElement('p');
      empty.className = 'pillar-empty';
      empty.textContent = 'No modules reported for this pillar yet.';
      body.appendChild(empty);
    } else {
      const moduleList = document.createElement('div');
      moduleList.className = 'pillar-modules';
      group.modules.forEach(module => {
        moduleList.appendChild(createModuleDetails(module, group.moduleClass));
      });
      body.appendChild(moduleList);
    }

    wrapper.appendChild(body);
    modulesContainer.appendChild(wrapper);
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
    renderModules(status.modules, status.configuration);
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
