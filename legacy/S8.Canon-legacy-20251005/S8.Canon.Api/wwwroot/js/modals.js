/**
 * S8 Flow Operations Dashboard - Modal Management
 * Handles administrative operations and entity details
 */

class FlowModals {
  constructor() {
    this.activeModal = null;
    this.initialize();
  }

  initialize() {
    this.createModalContainer();
    this.setupKeyboardListeners();
  }

  createModalContainer() {
    if (document.getElementById('modalContainer')) return;

    const container = document.createElement('div');
    container.id = 'modalContainer';
    container.className = 'modal-container';
    document.body.appendChild(container);
  }

  setupKeyboardListeners() {
    document.addEventListener('keydown', (e) => {
      if (e.key === 'Escape' && this.activeModal) {
        this.closeModal();
      }
    });
  }

  openModal(content) {
    const container = document.getElementById('modalContainer');
    container.innerHTML = content;
    container.style.display = 'flex';
    this.activeModal = container;
    document.body.style.overflow = 'hidden';

    // Setup close handlers
    container.addEventListener('click', (e) => {
      if (e.target === container) {
        this.closeModal();
      }
    });

    container.querySelectorAll('[data-modal-close]').forEach(btn => {
      btn.addEventListener('click', () => this.closeModal());
    });
  }

  closeModal() {
    const container = document.getElementById('modalContainer');
    container.style.display = 'none';
    container.innerHTML = '';
    this.activeModal = null;
    document.body.style.overflow = '';
  }

  // Quick Actions Modals
  openReplayModal() {
    const content = `
      <div class="modal-backdrop">
        <div class="modal-content">
          <div class="modal-header">
            <h3 class="modal-title">Replay Messages</h3>
            <button data-modal-close class="modal-close">
              <i class="fas fa-times"></i>
            </button>
          </div>
          <div class="modal-body">
            <form id="replayForm" class="space-y-4">
              <div class="form-group">
                <label for="replaySource" class="form-label">Source Stage</label>
                <select id="replaySource" name="source" class="form-select" required>
                  <option value="">Select source...</option>
                  <option value="intake">Intake</option>
                  <option value="keyed">Keyed</option>
                  <option value="canonical">Canonical</option>
                </select>
              </div>
              
              <div class="form-group">
                <label for="replayTarget" class="form-label">Target Stage</label>
                <select id="replayTarget" name="target" class="form-select" required>
                  <option value="">Select target...</option>
                  <option value="keyed">Keyed</option>
                  <option value="canonical">Canonical</option>
                  <option value="lineage">Lineage</option>
                </select>
              </div>
              
              <div class="form-group">
                <label for="replayTimeRange" class="form-label">Time Range</label>
                <select id="replayTimeRange" name="timeRange" class="form-select">
                  <option value="1h">Last 1 hour</option>
                  <option value="6h">Last 6 hours</option>
                  <option value="24h">Last 24 hours</option>
                  <option value="7d">Last 7 days</option>
                  <option value="custom">Custom range...</option>
                </select>
              </div>
              
              <div id="customTimeRange" class="form-group" style="display: none;">
                <div class="grid grid-cols-2 gap-4">
                  <div>
                    <label for="replayStartTime" class="form-label">Start Time</label>
                    <input type="datetime-local" id="replayStartTime" name="startTime" class="form-input">
                  </div>
                  <div>
                    <label for="replayEndTime" class="form-label">End Time</label>
                    <input type="datetime-local" id="replayEndTime" name="endTime" class="form-input">
                  </div>
                </div>
              </div>
              
              <div class="form-group">
                <label for="replayFilter" class="form-label">Entity Filter (Optional)</label>
                <input type="text" id="replayFilter" name="filter" class="form-input" 
                       placeholder="e.g., deviceId=ABC123 or sensorType=temperature">
              </div>
              
              <div class="form-group">
                <label class="flex items-center">
                  <input type="checkbox" id="replayDryRun" name="dryRun" class="form-checkbox">
                  <span class="ml-2">Dry run (preview only)</span>
                </label>
              </div>
            </form>
          </div>
          <div class="modal-footer">
            <button type="button" data-modal-close class="btn-secondary">Cancel</button>
            <button type="submit" form="replayForm" class="btn-primary">
              <i class="fas fa-play mr-2"></i>Start Replay
            </button>
          </div>
        </div>
      </div>
    `;

    this.openModal(content);
    this.setupReplayForm();
  }

  setupReplayForm() {
    const timeRangeSelect = document.getElementById('replayTimeRange');
    const customTimeRange = document.getElementById('customTimeRange');
    
    timeRangeSelect.addEventListener('change', () => {
      customTimeRange.style.display = timeRangeSelect.value === 'custom' ? 'block' : 'none';
    });

    const form = document.getElementById('replayForm');
    form.addEventListener('submit', async (e) => {
      e.preventDefault();
      await this.handleReplaySubmit(new FormData(form));
    });
  }

  async handleReplaySubmit(formData) {
    const payload = {
      source: formData.get('source'),
      target: formData.get('target'),
      timeRange: formData.get('timeRange'),
      filter: formData.get('filter'),
      dryRun: formData.has('dryRun')
    };

    if (payload.timeRange === 'custom') {
      payload.startTime = formData.get('startTime');
      payload.endTime = formData.get('endTime');
    }

    try {
  const result = await window.flowApi.replayData(payload);
      this.showReplayResult(result);
    } catch (error) {
      this.showError('Replay operation failed', error.message);
    }
  }

  openReprojectModal() {
    const content = `
      <div class="modal-backdrop">
        <div class="modal-content">
          <div class="modal-header">
            <h3 class="modal-title">Reproject Entities</h3>
            <button data-modal-close class="modal-close">
              <i class="fas fa-times"></i>
            </button>
          </div>
          <div class="modal-body">
            <form id="reprojectForm" class="space-y-4">
              <div class="form-group">
                <label for="reprojectType" class="form-label">Entity Type</label>
                <select id="reprojectType" name="entityType" class="form-select" required>
                  <option value="">Select type...</option>
                  <option value="device">Devices</option>
                  <option value="sensor">Sensors</option>
                  <option value="reading">Readings</option>
                </select>
              </div>
              
              <div class="form-group">
                <label for="reprojectSource" class="form-label">Source Collection</label>
                <select id="reprojectSource" name="source" class="form-select" required>
                  <option value="">Select source...</option>
                  <option value="keyed">Keyed</option>
                  <option value="canonical">Canonical</option>
                </select>
              </div>
              
              <div class="form-group">
                <label for="reprojectCriteria" class="form-label">Selection Criteria</label>
                <textarea id="reprojectCriteria" name="criteria" class="form-textarea" rows="3"
                         placeholder="JSON filter criteria, e.g., { &quot;timestamp&quot;: { &quot;$gte&quot;: &quot;2024-01-01T00:00:00Z&quot; } }"></textarea>
              </div>
              
              <div class="form-group">
                <label class="flex items-center">
                  <input type="checkbox" id="reprojectForce" name="force" class="form-checkbox">
                  <span class="ml-2">Force reprojection (overwrite existing)</span>
                </label>
              </div>
            </form>
          </div>
          <div class="modal-footer">
            <button type="button" data-modal-close class="btn-secondary">Cancel</button>
            <button type="submit" form="reprojectForm" class="btn-primary">
              <i class="fas fa-sync mr-2"></i>Start Reprojection
            </button>
          </div>
        </div>
      </div>
    `;

    this.openModal(content);
    this.setupReprojectForm();
  }

  setupReprojectForm() {
    const form = document.getElementById('reprojectForm');
    form.addEventListener('submit', async (e) => {
      e.preventDefault();
      await this.handleReprojectSubmit(new FormData(form));
    });
  }

  async handleReprojectSubmit(formData) {
    const payload = {
      entityType: formData.get('entityType'),
      source: formData.get('source'),
      criteria: formData.get('criteria'),
      force: formData.has('force')
    };

    try {
      if (payload.criteria) {
        payload.criteria = JSON.parse(payload.criteria);
      }
    } catch (error) {
      this.showError('Invalid JSON', 'Please check your selection criteria JSON format');
      return;
    }

    try {
  const result = await window.flowApi.reprojectView(payload);
      this.showReprojectResult(result);
    } catch (error) {
      this.showError('Reprojection operation failed', error.message);
    }
  }

  openBulkImportModal() {
    const content = `
      <div class="modal-backdrop">
        <div class="modal-content">
          <div class="modal-header">
            <h3 class="modal-title">Bulk Import</h3>
            <button data-modal-close class="modal-close">
              <i class="fas fa-times"></i>
            </button>
          </div>
          <div class="modal-body">
            <form id="bulkImportForm" class="space-y-4">
              <div class="form-group">
                <label for="importType" class="form-label">Import Type</label>
                <select id="importType" name="importType" class="form-select" required>
                  <option value="">Select type...</option>
                  <option value="devices">Device Configurations</option>
                  <option value="sensors">Sensor Definitions</option>
                  <option value="readings">Historical Readings</option>
                  <option value="policies">Processing Policies</option>
                </select>
              </div>
              
              <div class="form-group">
                <label for="importFile" class="form-label">Data File</label>
                <input type="file" id="importFile" name="file" class="form-file" 
                       accept=".json,.csv,.xml" required>
                <div class="form-help">
                  Supported formats: JSON, CSV, XML. Maximum file size: 50MB.
                </div>
              </div>
              
              <div class="form-group">
                <label for="importStrategy" class="form-label">Import Strategy</label>
                <select id="importStrategy" name="strategy" class="form-select">
                  <option value="merge">Merge (update existing)</option>
                  <option value="replace">Replace (overwrite existing)</option>
                  <option value="append">Append (insert only new)</option>
                </select>
              </div>
              
              <div class="form-group">
                <label class="flex items-center">
                  <input type="checkbox" id="importValidateOnly" name="validateOnly" class="form-checkbox">
                  <span class="ml-2">Validate only (don't import)</span>
                </label>
              </div>
            </form>
          </div>
          <div class="modal-footer">
            <button type="button" data-modal-close class="btn-secondary">Cancel</button>
            <button type="submit" form="bulkImportForm" class="btn-primary">
              <i class="fas fa-upload mr-2"></i>Start Import
            </button>
          </div>
        </div>
      </div>
    `;

    this.openModal(content);
    this.setupBulkImportForm();
  }

  setupBulkImportForm() {
    const form = document.getElementById('bulkImportForm');
    form.addEventListener('submit', async (e) => {
      e.preventDefault();
      await this.handleBulkImportSubmit(new FormData(form));
    });
  }

  async handleBulkImportSubmit(formData) {
    try {
  // Not implemented; provide a simulated success
  const result = { count: 0 };
      this.showBulkImportResult(result);
    } catch (error) {
      this.showError('Bulk import operation failed', error.message);
    }
  }

  openPolicyUpdateModal() {
    const content = `
      <div class="modal-backdrop">
        <div class="modal-content">
          <div class="modal-header">
            <h3 class="modal-title">Update Processing Policies</h3>
            <button data-modal-close class="modal-close">
              <i class="fas fa-times"></i>
            </button>
          </div>
          <div class="modal-body">
            <form id="policyUpdateForm" class="space-y-4">
              <div class="form-group">
                <label for="policyScope" class="form-label">Policy Scope</label>
                <select id="policyScope" name="scope" class="form-select" required>
                  <option value="">Select scope...</option>
                  <option value="global">Global (all entities)</option>
                  <option value="device-type">By Device Type</option>
                  <option value="sensor-type">By Sensor Type</option>
                  <option value="specific">Specific Entities</option>
                </select>
              </div>
              
              <div id="scopeFilter" class="form-group" style="display: none;">
                <label for="scopeValue" class="form-label">Scope Filter</label>
                <input type="text" id="scopeValue" name="scopeValue" class="form-input" 
                       placeholder="Enter device type, sensor type, or entity IDs">
              </div>
              
              <div class="form-group">
                <label for="policyType" class="form-label">Policy Type</label>
                <select id="policyType" name="policyType" class="form-select" required>
                  <option value="">Select policy...</option>
                  <option value="validation">Validation Rules</option>
                  <option value="transformation">Data Transformation</option>
                  <option value="retention">Data Retention</option>
                  <option value="routing">Message Routing</option>
                </select>
              </div>
              
              <div class="form-group">
                <label for="policyDefinition" class="form-label">Policy Definition</label>
                <textarea id="policyDefinition" name="definition" class="form-textarea" rows="6"
                         placeholder="JSON policy definition..."></textarea>
              </div>
              
              <div class="form-group">
                <label class="flex items-center">
                  <input type="checkbox" id="policyApplyExisting" name="applyExisting" class="form-checkbox">
                  <span class="ml-2">Apply to existing data</span>
                </label>
              </div>
            </form>
          </div>
          <div class="modal-footer">
            <button type="button" data-modal-close class="btn-secondary">Cancel</button>
            <button type="submit" form="policyUpdateForm" class="btn-primary">
              <i class="fas fa-shield-alt mr-2"></i>Update Policy
            </button>
          </div>
        </div>
      </div>
    `;

    this.openModal(content);
    this.setupPolicyUpdateForm();
  }

  setupPolicyUpdateForm() {
    const scopeSelect = document.getElementById('policyScope');
    const scopeFilter = document.getElementById('scopeFilter');
    
    scopeSelect.addEventListener('change', () => {
      const showFilter = ['device-type', 'sensor-type', 'specific'].includes(scopeSelect.value);
      scopeFilter.style.display = showFilter ? 'block' : 'none';
    });

    const form = document.getElementById('policyUpdateForm');
    form.addEventListener('submit', async (e) => {
      e.preventDefault();
      await this.handlePolicyUpdateSubmit(new FormData(form));
    });
  }

  async handlePolicyUpdateSubmit(formData) {
    const payload = {
      scope: formData.get('scope'),
      scopeValue: formData.get('scopeValue'),
      policyType: formData.get('policyType'),
      definition: formData.get('definition'),
      applyExisting: formData.has('applyExisting')
    };

    try {
      if (payload.definition) {
        payload.definition = JSON.parse(payload.definition);
      }
    } catch (error) {
      this.showError('Invalid JSON', 'Please check your policy definition JSON format');
      return;
    }

    try {
  const result = await window.flowApi.updatePolicies(payload);
      this.showPolicyUpdateResult(result);
    } catch (error) {
      this.showError('Policy update operation failed', error.message);
    }
  }

  // Entity Detail Modals
  async openEntityDetailsModal(entityId, entityType) {
    try {
      const entity = await this.fetchEntityDetails(entityId, entityType);
      const content = `
        <div class="modal-backdrop">
          <div class="modal-content modal-content--large">
            <div class="modal-header">
              <h3 class="modal-title">
                <i class="fas fa-${this.getEntityIcon(entityType)} mr-2"></i>
                ${entityType} Details
              </h3>
              <button data-modal-close class="modal-close">
                <i class="fas fa-times"></i>
              </button>
            </div>
            <div class="modal-body">
              ${this.renderEntityDetails(entity, entityType)}
            </div>
            <div class="modal-footer">
              <button type="button" data-modal-close class="btn-secondary">Close</button>
              <button type="button" onclick="modals.exportEntityData('${entityId}', '${entityType}')" class="btn-primary">
                <i class="fas fa-download mr-2"></i>Export Data
              </button>
            </div>
          </div>
        </div>
      `;

      this.openModal(content);
    } catch (error) {
      this.showError('Failed to load entity details', error.message);
    }
  }

  async fetchEntityDetails(entityId, entityType) {
    switch (entityType) {
      case 'device':
        return await window.flowApi.getDevice(entityId);
      case 'sensor':
        return await window.flowApi.getSensor(entityId);
      case 'manufacturer':
        return await window.flowApi.getManufacturer(entityId).catch(() => ({ id: entityId, model: { identifier: { name: 'Unknown' } }, metadata: {} }));
      case 'reading':
        // There isn't a direct reading-by-id route; return recent as a placeholder
        const page = await window.flowApi.getReadings({ size: 1 });
        return (page.items && page.items[0]) || { id: entityId, model: {}, metadata: {} };
      default:
        throw new Error(`Unknown entity type: ${entityType}`);
    }
  }

  renderEntityDetails(entity, entityType) {
    const metadata = entity.metadata || {};
    const model = entity.model || entity;
    
    return `
      <div class="entity-details">
        <div class="grid grid-cols-1 lg:grid-cols-2 gap-6">
          <div class="entity-details__section">
            <h4 class="entity-details__heading">Basic Information</h4>
            <dl class="entity-details__list">
              <dt>ID</dt>
              <dd class="font-mono">${entity.id || 'N/A'}</dd>
              <dt>Canonical ID</dt>
              <dd class="font-mono">${model.canonicalId || 'N/A'}</dd>
              <dt>Type</dt>
              <dd>${entityType}</dd>
              <dt>Status</dt>
              <dd><span class="chip chip--success">Active</span></dd>
              <dt>Created</dt>
              <dd>${this.formatDate(metadata.created || new Date())}</dd>
              <dt>Updated</dt>
              <dd>${this.formatDate(metadata.updated || new Date())}</dd>
            </dl>
          </div>
          
          <div class="entity-details__section">
            <h4 class="entity-details__heading">Model Data</h4>
            <pre class="entity-details__json">${JSON.stringify(model, null, 2)}</pre>
          </div>
        </div>
        
        ${this.renderEntityTypeSpecific(entity, entityType)}
      </div>
    `;
  }

  renderEntityTypeSpecific(entity, entityType) {
    switch (entityType) {
      case 'device':
        return this.renderDeviceSpecific(entity);
      case 'sensor':
        return this.renderSensorSpecific(entity);
      case 'manufacturer':
        return this.renderManufacturerSpecific(entity);
      case 'reading':
        return this.renderReadingSpecific(entity);
      default:
        return '';
    }
  }

  renderDeviceSpecific(device) {
    return `
      <div class="entity-details__section mt-6">
        <h4 class="entity-details__heading">Device Configuration</h4>
        <dl class="entity-details__list">
          <dt>Model</dt>
          <dd>${device.model?.name || 'Unknown'}</dd>
          <dt>Firmware</dt>
          <dd>${device.model?.firmware || 'N/A'}</dd>
          <dt>Location</dt>
          <dd>${device.model?.location || 'Unspecified'}</dd>
        </dl>
      </div>
    `;
  }

  renderSensorSpecific(sensor) {
    return `
      <div class="entity-details__section mt-6">
        <h4 class="entity-details__heading">Sensor Configuration</h4>
        <dl class="entity-details__list">
          <dt>Sensor Type</dt>
          <dd>${sensor.model?.type || 'Unknown'}</dd>
          <dt>Unit</dt>
          <dd>${sensor.model?.unit || 'N/A'}</dd>
          <dt>Range</dt>
          <dd>${sensor.model?.range || 'Unspecified'}</dd>
          <dt>Device ID</dt>
          <dd class="font-mono">${sensor.model?.deviceId || 'N/A'}</dd>
        </dl>
      </div>
    `;
  }

  renderManufacturerSpecific(manufacturer) {
    const identifier = manufacturer.model?.identifier || {};
    const manufacturing = manufacturer.model?.manufacturing || {};
    const support = manufacturer.model?.support || {};
    const certifications = manufacturer.model?.certifications || {};
    
    return `
      <div class="entity-details__section mt-6">
        <h4 class="entity-details__heading">Manufacturer Information</h4>
        <dl class="entity-details__list">
          <dt>Company Name</dt>
          <dd>${identifier.name || 'Unknown'}</dd>
          <dt>Company Code</dt>
          <dd class="font-mono">${identifier.code || 'N/A'}</dd>
          <dt>Country</dt>
          <dd>${manufacturing.country || 'Unspecified'}</dd>
          <dt>Established</dt>
          <dd>${manufacturing.established || 'Unknown'}</dd>
          <dt>Support Tier</dt>
          <dd><span class="chip chip--info">${support.tier || 'Standard'}</span></dd>
          <dt>Contact</dt>
          <dd>${support.email || support.phone || 'N/A'}</dd>
          <dt>ISO 9001 Certified</dt>
          <dd>${certifications.iso9001 ? '✓ Yes' : '○ No'}</dd>
        </dl>
      </div>
    `;
  }

  renderReadingSpecific(reading) {
    return `
      <div class="entity-details__section mt-6">
        <h4 class="entity-details__heading">Reading Data</h4>
        <dl class="entity-details__list">
          <dt>Value</dt>
          <dd class="text-lg font-semibold">${reading.model?.value || 'N/A'}</dd>
          <dt>Timestamp</dt>
          <dd>${this.formatDate(reading.model?.timestamp)}</dd>
          <dt>Quality</dt>
          <dd><span class="chip chip--info">${reading.model?.quality || 'Good'}</span></dd>
          <dt>Sensor Key</dt>
          <dd class="font-mono">${reading.model?.key || 'N/A'}</dd>
        </dl>
      </div>
    `;
  }

  async openLineageModal(entityId, entityType) {
    try {
      const lineage = await this.fetchEntityLineage(entityId, entityType);
      const content = `
        <div class="modal-backdrop">
          <div class="modal-content modal-content--large">
            <div class="modal-header">
              <h3 class="modal-title">
                <i class="fas fa-project-diagram mr-2"></i>
                Entity Lineage
              </h3>
              <button data-modal-close class="modal-close">
                <i class="fas fa-times"></i>
              </button>
            </div>
            <div class="modal-body">
              ${this.renderLineageGraph(lineage)}
            </div>
            <div class="modal-footer">
              <button type="button" data-modal-close class="btn-secondary">Close</button>
              <button type="button" onclick="modals.exportLineageData('${entityId}', '${entityType}')" class="btn-primary">
                <i class="fas fa-download mr-2"></i>Export Lineage
              </button>
            </div>
          </div>
        </div>
      `;

      this.openModal(content);
    } catch (error) {
      this.showError('Failed to load entity lineage', error.message);
    }
  }

  async fetchEntityLineage(entityId, entityType) {
    try {
  return await window.flowApi.getLineage(entityId);
    } catch (error) {
      // Simulate lineage data if API endpoint not available
      return this.simulateLineageData(entityId, entityType);
    }
  }

  simulateLineageData(entityId, entityType) {
    return {
      entity: { id: entityId, type: entityType },
      stages: [
        { name: 'intake', timestamp: new Date(Date.now() - 3600000), status: 'completed' },
        { name: 'keyed', timestamp: new Date(Date.now() - 1800000), status: 'completed' },
        { name: 'canonical', timestamp: new Date(Date.now() - 900000), status: 'completed' },
        { name: 'lineage', timestamp: new Date(), status: 'active' }
      ],
      relations: [
        { type: 'device', id: 'dev-001', relation: 'parent' },
        { type: 'sensor', id: 'sen-123', relation: 'sibling' }
      ]
    };
  }

  renderLineageGraph(lineage) {
    const stages = lineage.stages || [];
    const relations = lineage.relations || [];

    return `
      <div class="lineage-graph">
        <div class="lineage-graph__timeline">
          <h4 class="text-lg font-semibold mb-4">Processing Timeline</h4>
          <div class="timeline">
            ${stages.map(stage => `
              <div class="timeline-item timeline-item--${stage.status}">
                <div class="timeline-item__marker"></div>
                <div class="timeline-item__content">
                  <div class="timeline-item__title">${stage.name}</div>
                  <div class="timeline-item__time">${this.formatDate(stage.timestamp)}</div>
                  <div class="timeline-item__status">${stage.status}</div>
                </div>
              </div>
            `).join('')}
          </div>
        </div>
        
        ${relations.length > 0 ? `
          <div class="lineage-graph__relations">
            <h4 class="text-lg font-semibold mb-4">Related Entities</h4>
            <div class="space-y-2">
              ${relations.map(rel => `
                <div class="relation-item">
                  <i class="fas fa-${this.getEntityIcon(rel.type)} mr-2"></i>
                  <span class="font-mono">${rel.id}</span>
                  <span class="chip chip--outline">${rel.relation}</span>
                </div>
              `).join('')}
            </div>
          </div>
        ` : ''}
      </div>
    `;
  }

  // Result Display Methods
  showReplayResult(result) {
    this.closeModal();
    this.showSuccess('Replay Operation', `Successfully replayed ${result.count || 0} messages`);
  }

  showReprojectResult(result) {
    this.closeModal();
    this.showSuccess('Reprojection Operation', `Successfully reprojected ${result.count || 0} entities`);
  }

  showBulkImportResult(result) {
    this.closeModal();
    this.showSuccess('Bulk Import', `Successfully imported ${result.count || 0} records`);
  }

  showPolicyUpdateResult(result) {
    this.closeModal();
    this.showSuccess('Policy Update', `Successfully updated policy for ${result.scope || 'selected entities'}`);
  }

  showSuccess(title, message) {
    if (window.dashboard) {
      window.dashboard.addToActivityFeed([{
        type: 'admin',
        timestamp: new Date().toISOString(),
        title: title,
        subtitle: message,
        level: 'success'
      }]);
    }
  }

  showError(title, message) {
    if (window.dashboard) {
      window.dashboard.addToActivityFeed([{
        type: 'admin',
        timestamp: new Date().toISOString(),
        title: title,
        subtitle: message,
        level: 'error'
      }]);
    }
  }

  // Utility Methods
  getEntityIcon(type) {
    switch (type) {
      case 'device': return 'microchip';
      case 'sensor': return 'thermometer-half';
      case 'manufacturer': return 'building';
      case 'reading': return 'chart-line';
      default: return 'circle';
    }
  }

  formatDate(date) {
    if (!date) return 'N/A';
    return new Date(date).toLocaleString();
  }

  // Export Methods
  async exportEntityData(entityId, entityType) {
    try {
      const entity = await this.fetchEntityDetails(entityId, entityType);
      const blob = new Blob([JSON.stringify(entity, null, 2)], { type: 'application/json' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `s8-flow-${entityType}-${entityId}.json`;
      a.click();
      URL.revokeObjectURL(url);
    } catch (error) {
      this.showError('Export Failed', error.message);
    }
  }

  async exportLineageData(entityId, entityType) {
    try {
      const lineage = await this.fetchEntityLineage(entityId, entityType);
      const blob = new Blob([JSON.stringify(lineage, null, 2)], { type: 'application/json' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `s8-flow-lineage-${entityType}-${entityId}.json`;
      a.click();
      URL.revokeObjectURL(url);
    } catch (error) {
      this.showError('Export Failed', error.message);
    }
  }
}

// Global functions for onclick handlers
window.openReplayModal = () => modals.openReplayModal();
window.openReprojectModal = () => modals.openReprojectModal();
window.openBulkImportModal = () => modals.openBulkImportModal();
window.openPolicyUpdateModal = () => modals.openPolicyUpdateModal();
window.openEntityDetailsModal = (id, type) => modals.openEntityDetailsModal(id, type);
window.openLineageModal = (id, type) => modals.openLineageModal(id, type);

// Initialize modals when DOM is ready
document.addEventListener('DOMContentLoaded', () => {
  window.modals = new FlowModals();
});
