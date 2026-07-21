const state = { data: null, query: '' };

const byId = id => document.getElementById(id);
const text = (tag, value, className) => {
  const node = document.createElement(tag);
  node.textContent = value ?? '';
  if (className) node.className = className;
  return node;
};

function formatBytes(value) {
  const bytes = Number(value || 0);
  if (!bytes) return '0 B';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  const index = Math.min(Math.floor(Math.log(bytes) / Math.log(1024)), units.length - 1);
  return `${(bytes / Math.pow(1024, index)).toFixed(index > 1 ? 1 : 0)} ${units[index]}`;
}

function formatDuration(seconds) {
  const total = Math.max(0, Math.floor(Number(seconds || 0)));
  const days = Math.floor(total / 86400);
  const hours = Math.floor((total % 86400) / 3600);
  const minutes = Math.floor((total % 3600) / 60);
  return days ? `${days}d ${hours}h` : hours ? `${hours}h ${minutes}m` : `${minutes}m`;
}

function safeToolHref(value) {
  try {
    const url = new URL(String(value || ''), window.location.origin);
    return url.protocol === 'http:' || url.protocol === 'https:' ? url.href : null;
  } catch {
    return null;
  }
}

function addFact(container, label, value) {
  const card = document.createElement('div');
  card.className = 'fact';
  card.append(text('span', label), text('strong', value));
  container.append(card);
}

function renderSummary(data) {
  byId('environment').textContent = data.environment?.environmentName || data.environment?.environment || 'Development';
  byId('module-count').textContent = String(data.modules?.length || 0);
  byId('health-state').textContent = data.health?.overall || 'Unknown';
  byId('uptime').textContent = formatDuration(data.runtime?.process?.uptimeSeconds);
  byId('captured-at').textContent = data.capturedAtUtc ? new Date(data.capturedAtUtc).toLocaleString() : '';
}

function renderRuntime(runtime) {
  const container = byId('runtime');
  container.replaceChildren();
  addFact(container, 'Process', `${runtime?.process?.name || 'unknown'} · PID ${runtime?.process?.processId || 0}`);
  addFact(container, 'CPU', `${Number(runtime?.process?.cpuUtilizationPercent || 0).toFixed(2)}%`);
  addFact(container, 'Working set', formatBytes(runtime?.memory?.workingSetBytes));
  addFact(container, 'Managed heap', formatBytes(runtime?.memory?.managedHeapBytes));
  addFact(container, 'GC heap', formatBytes(runtime?.memory?.gcHeapSizeBytes));
  addFact(container, 'GC mode', `${runtime?.garbageCollector?.isServerGc ? 'Server' : 'Workstation'} / ${runtime?.garbageCollector?.latencyMode || 'Unknown'}`);
  addFact(container, 'Thread pool', `${runtime?.threadPool?.threadCount || 0} threads`);
  addFact(container, 'Runtime', runtime?.machine?.frameworkDescription || 'unknown');
}

function renderHealth(health) {
  const container = byId('health');
  container.replaceChildren();
  const components = health?.components || [];
  if (!components.length) {
    container.append(text('p', 'No health contributors have reported yet.', 'empty'));
    return;
  }
  components.forEach(component => {
    const row = document.createElement('article');
    row.className = 'health-item';
    const copy = document.createElement('div');
    copy.append(text('strong', component.component));
    if (component.message) copy.append(text('p', component.message));
    const status = text('span', component.status || 'Unknown', `status ${(component.status || 'unknown').toLowerCase()}`);
    row.append(copy, status);
    container.append(row);
  });
}

function settingTable(settings) {
  const table = document.createElement('table');
  const head = document.createElement('thead');
  const header = document.createElement('tr');
  ['Setting', 'Value', 'Source'].forEach(label => header.append(text('th', label)));
  head.append(header);
  const body = document.createElement('tbody');
  settings.forEach(setting => {
    const row = document.createElement('tr');
    const name = document.createElement('td');
    name.append(text('code', setting.key), text('div', setting.description || setting.label, 'muted'));
    const value = document.createElement('td');
    value.append(text('code', setting.value));
    row.append(name, value, text('td', setting.source || 'Unknown'));
    body.append(row);
  });
  table.append(head, body);
  return table;
}

function renderModules(modules) {
  const container = byId('modules');
  container.replaceChildren();
  const query = state.query.trim().toLowerCase();
  const visible = (modules || []).filter(module => {
    if (!query) return true;
    return [module.name, module.description, module.pillar, ...(module.settings || []).flatMap(item => [item.key, item.label])]
      .some(value => String(value || '').toLowerCase().includes(query));
  });
  if (!visible.length) {
    container.append(text('p', 'No modules match this filter.', 'empty'));
    return;
  }

  visible.forEach(module => {
    const details = document.createElement('details');
    const summary = document.createElement('summary');
    const icon = text('span', module.pillarIcon || '🧩', 'pillar');
    icon.style.setProperty('--pillar', module.pillarColor || '#64748b');
    const title = document.createElement('span');
    title.className = 'module-title';
    title.append(text('strong', module.name), text('span', `${module.pillar || 'General'} · ${module.version || 'unversioned'}`));
    summary.append(icon, title, text('span', `${module.settings?.length || 0} settings`, 'muted'));

    const body = document.createElement('div');
    body.className = 'module-body';
    if (module.description) body.append(text('p', module.description, 'muted'));
    if (module.settings?.length) {
      const section = document.createElement('section');
      section.append(text('h3', 'Settings'), settingTable(module.settings));
      body.append(section);
    }
    if (module.notes?.length) {
      const section = document.createElement('section');
      section.append(text('h3', 'Notes'));
      const list = document.createElement('ul');
      module.notes.forEach(note => list.append(text('li', note)));
      section.append(list);
      body.append(section);
    }
    if (module.tools?.length) {
      const section = document.createElement('section');
      section.append(text('h3', 'Tools'));
      const list = document.createElement('ul');
      module.tools.forEach(tool => {
        const item = document.createElement('li');
        const href = safeToolHref(tool.route);
        if (href) {
          const link = document.createElement('a');
          link.href = href;
          link.textContent = tool.name;
          link.rel = 'noreferrer';
          item.append(link);
        } else {
          item.append(text('span', tool.name));
        }
        if (tool.description) item.append(document.createTextNode(` — ${tool.description}`));
        list.append(item);
      });
      section.append(list);
      body.append(section);
    }
    details.append(summary, body);
    container.append(details);
  });
}

function render(data) {
  renderSummary(data);
  renderRuntime(data.runtime);
  renderHealth(data.health);
  renderModules(data.modules);
}

async function refresh() {
  const notice = byId('notice');
  const button = byId('refresh');
  notice.hidden = true;
  button.disabled = true;
  try {
    const response = await fetch('status', { headers: { Accept: 'application/json' }, cache: 'no-store' });
    if (!response.ok) throw new Error(response.status === 401 || response.status === 403
      ? 'Your authenticated user does not satisfy the Koan Admin policy.'
      : `Status request failed (${response.status}).`);
    state.data = await response.json();
    render(state.data);
  } catch (error) {
    notice.textContent = error instanceof Error ? error.message : String(error);
    notice.hidden = false;
  } finally {
    button.disabled = false;
  }
}

byId('refresh').addEventListener('click', refresh);
byId('filter').addEventListener('input', event => {
  state.query = event.target.value;
  if (state.data) renderModules(state.data.modules);
});
refresh();
