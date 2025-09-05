/**
 * S8 Flow Operations Dashboard - Main Dashboard Logic
 * Handles real-time monitoring and UI updates
 */

class FlowDashboard {
  constructor() {
    this.refreshInterval = 5000; // 5 seconds
    this.activityPaused = false;
    this.selectedEntities = new Set();
    this.refreshTimers = new Map();
    this.lastUpdateTime = new Date();
    this.stageData = {}; // Store per-state counts for flow visualization
    
    this.initialize();
  }

  initialize() {
    this.setupEventListeners();
    this.startRefreshCycle();
    this.loadInitialData();
  }

  setupEventListeners() {
    // Navigation
    document.querySelectorAll('[onclick*="setActiveTab"]').forEach(link => {
      link.addEventListener('click', (e) => {
        e.preventDefault();
        const tab = e.currentTarget.getAttribute('onclick').match(/'([^']+)'/)[1];
        this.setActiveTab(tab);
      });
    });

    // Search functionality
    const entitySearch = document.getElementById('entitySearch');
    if (entitySearch) {
      entitySearch.addEventListener('input', this.debounce(() => {
        this.searchEntities(entitySearch.value);
      }, 300));
    }

    // Filter changes
    const entityTypeFilter = document.getElementById('entityTypeFilter');
    if (entityTypeFilter) {
      entityTypeFilter.addEventListener('change', () => {
        this.filterEntities(entityTypeFilter.value);
      });
    }

    const activityFilter = document.getElementById('activityFilter');
    if (activityFilter) {
      activityFilter.addEventListener('change', () => {
        this.filterActivity(activityFilter.value);
      });
    }

    // Refresh buttons
    document.addEventListener('click', (e) => {
      if (e.target.closest('[onclick*="refresh"]')) {
        e.preventDefault();
        this.refreshPipeline();
      }
    });
  }

  setActiveTab(tab) {
    // Update navigation styling
    document.querySelectorAll('nav a').forEach(link => {
      link.classList.remove('text-white', 'border-blue-500');
      link.classList.add('text-gray-400', 'border-transparent');
    });
    
    const activeLink = document.querySelector(`[onclick*="${tab}"]`);
    if (activeLink) {
      activeLink.classList.remove('text-gray-400', 'border-transparent');
      activeLink.classList.add('text-white', 'border-blue-500');
    }

    // Update content visibility if needed
    this.refreshPipeline();
  }

  async loadInitialData() {
    try {
      await Promise.all([
        this.updatePipelineHealth(),
        this.updateEntityCounts(),
        this.loadEntityBrowser(),
        this.loadActivityFeed()
      ]);
    } catch (error) {
      console.error('Failed to load initial data:', error);
      this.showSystemError('Failed to load dashboard data');
    }
  }

  startRefreshCycle() {
    // Clear any existing timers
    this.refreshTimers.forEach(timer => clearInterval(timer));
    this.refreshTimers.clear();

    // Pipeline health - every 5 seconds
    this.refreshTimers.set('pipeline', setInterval(() => {
      this.updatePipelineHealth();
    }, this.refreshInterval));

    // Entity counts - every 10 seconds
    this.refreshTimers.set('entities', setInterval(() => {
      this.updateEntityCounts();
    }, this.refreshInterval * 2));

    // Activity feed - every 3 seconds (when not paused)
    this.refreshTimers.set('activity', setInterval(() => {
      if (!this.activityPaused) {
        this.updateActivityFeed();
      }
    }, 3000));

    // Last update time
    this.refreshTimers.set('timestamp', setInterval(() => {
      this.updateLastUpdateTime();
    }, 1000));
  }

  async updatePipelineHealth() {
    try {
      const [adapterHealth, systemHealth] = await Promise.all([
        window.flowApi.getAdapterHealth(),
        window.flowApi.getSystemHealth()
      ]);

      this.updateAdapterStatus(adapterHealth);
      this.updateSystemStatus(systemHealth);
      this.updatePipelineMetrics();
    } catch (error) {
      console.error('Failed to update pipeline health:', error);
      this.showAdapterError();
    }
  }

  updateAdapterStatus(health) {
    const bmsElement = document.getElementById('adaptersBms');
    const oemElement = document.getElementById('adaptersOem');
    
    if (health && health.adapters) {
      const bmsStatus = health.adapters.bms || { status: 'unknown' };
      const oemStatus = health.adapters.oem || { status: 'unknown' };
      
      bmsElement.textContent = bmsStatus.status === 'healthy' ? '●' : '○';
      bmsElement.className = bmsStatus.status === 'healthy' ? 'text-2xl font-bold text-green-400' : 'text-2xl font-bold text-red-400';
      
      oemElement.textContent = oemStatus.status === 'healthy' ? '●' : '○';
      oemElement.className = oemStatus.status === 'healthy' ? 'text-2xl font-bold text-green-400' : 'text-2xl font-bold text-red-400';
    } else {
      // Fallback - assume healthy if we can reach the endpoint
      bmsElement.textContent = '●';
      bmsElement.className = 'text-2xl font-bold text-green-400';
      oemElement.textContent = '●';
      oemElement.className = 'text-2xl font-bold text-green-400';
    }
  }

  updateSystemStatus(health) {
    const statusElement = document.getElementById('systemStatus');
    const statusTextElement = document.getElementById('systemStatusText');
    const systemHealthElement = document.getElementById('systemHealth');
    
    if (health && health.status === 'Healthy') {
      statusElement.className = 'w-3 h-3 bg-green-500 rounded-full animate-pulse';
      statusTextElement.textContent = 'System Healthy';
      systemHealthElement.textContent = '✓ Healthy';
      systemHealthElement.className = 'text-green-400 text-sm';
    } else {
      statusElement.className = 'w-3 h-3 bg-red-500 rounded-full animate-pulse';
      statusTextElement.textContent = 'System Issues';
      systemHealthElement.textContent = '⚠ Issues';
      systemHealthElement.className = 'text-red-400 text-sm';
    }
  }

  async updatePipelineMetrics() {
    try {
      // Simulate pipeline metrics based on entity counts
      const counts = await window.flowApi.getEntityCounts();
      
      // Update intake->keyed flow (simulate high throughput)
      const intakeKeyedRate = Math.floor(Math.random() * 100) + 50;
      const intakeKeyedPercent = Math.min(95, Math.max(70, intakeKeyedRate));
      
      document.getElementById('intakeKeyedRate').textContent = `${intakeKeyedRate}/min`;
      document.getElementById('intakeKeyedBar').style.width = `${intakeKeyedPercent}%`;
      
      // Update keyed->canonical flow (simulate lower throughput)
      const keyedCanonRate = Math.floor(intakeKeyedRate * 0.3);
      const keyedCanonPercent = Math.min(98, Math.max(85, keyedCanonRate));
      
      document.getElementById('keyedCanonRate').textContent = `${keyedCanonRate}/min`;
      document.getElementById('keyedCanonBar').style.width = `${keyedCanonPercent}%`;
      
    } catch (error) {
      console.error('Failed to update pipeline metrics:', error);
    }
  }

  async updateEntityCounts() {
    try {
      const overview = await window.flowApi.getFlowOverview();
      
      // Devices
      const deviceStages = overview.devices || {};
      const deviceCount = deviceStages.total || 0;
      document.getElementById('deviceCount').textContent = deviceCount;
      
      // Sensors
      const sensorStages = overview.sensors || {};
      const sensorCount = sensorStages.total || 0;
      document.getElementById('sensorCount').textContent = sensorCount;
      
      // Readings
      const readingStages = overview.readings || {};
      const readingCount = readingStages.total || 0;
      document.getElementById('readingCount').textContent = readingCount;
      
      // Manufacturers (new)
      const manufacturerStages = overview.manufacturers || {};
      const manufacturerCount = manufacturerStages.total || 0;
      document.getElementById('manufacturerCount').textContent = manufacturerCount;

      // Update activity indicators
      document.getElementById('deviceActivity').textContent = deviceCount > 0 ? 'active' : 'none';
      document.getElementById('sensorActivity').textContent = sensorCount > 0 ? 'active' : 'none';
      document.getElementById('readingActivity').textContent = readingCount > 0 ? 'recent' : 'none';
      document.getElementById('manufacturerActivity').textContent = manufacturerCount > 0 ? 'active' : 'none';
      
      // Store the detailed stage data for flow visualization
      this.stageData = {
        devices: deviceStages,
        sensors: sensorStages,
        readings: readingStages,
        manufacturers: manufacturerStages
      };
      
      // Update detailed flow state breakdown
      this.updateFlowStateBreakdown();
      
      // Update flow visualization
      this.updateFlowVisualization();
    } catch (error) {
      console.error('Failed to update entity counts:', error);
      this.showEntityCountError();
    }
  }

  updateFlowStateBreakdown() {
    try {
      const { devices, sensors, readings, manufacturers } = this.stageData;
      
      // Update device breakdown
      if (devices) {
        document.getElementById('deviceIntakeCount').textContent = devices.intake || 0;
        document.getElementById('deviceKeyedCount').textContent = devices.keyed || 0;
        document.getElementById('deviceProcessedCount').textContent = devices.processed || 0;
        document.getElementById('deviceTotalCount').textContent = devices.total || 0;
      }
      
      // Update sensor breakdown
      if (sensors) {
        document.getElementById('sensorIntakeCount').textContent = sensors.intake || 0;
        document.getElementById('sensorKeyedCount').textContent = sensors.keyed || 0;
        document.getElementById('sensorProcessedCount').textContent = sensors.processed || 0;
        document.getElementById('sensorTotalCount').textContent = sensors.total || 0;
      }
      
      // Update reading breakdown
      if (readings) {
        document.getElementById('readingIntakeCount').textContent = readings.intake || 0;
        document.getElementById('readingKeyedCount').textContent = readings.keyed || 0;
        document.getElementById('readingProcessedCount').textContent = readings.processed || 0;
        document.getElementById('readingTotalCount').textContent = readings.total || 0;
      }
      
      // Update manufacturer breakdown
      if (manufacturers) {
        document.getElementById('manufacturerIntakeCount').textContent = manufacturers.intake || 0;
        document.getElementById('manufacturerKeyedCount').textContent = manufacturers.keyed || 0;
        document.getElementById('manufacturerProcessedCount').textContent = manufacturers.processed || 0;
        document.getElementById('manufacturerTotalCount').textContent = manufacturers.total || 0;
      }
    } catch (error) {
      console.error('Failed to update flow state breakdown:', error);
    }
  }

  updateFlowVisualization() {
    try {
      // Update pipeline flow rates based on real stage data
      const readings = this.stageData.readings || {};
      
      // Calculate flow rates between stages
      const intakeToKeyedRate = this.calculateFlowRate(readings.intake, readings.keyed);
      const keyedToProcessedRate = this.calculateFlowRate(readings.keyed, readings.processed);
      
      // Update flow indicators
      document.getElementById('intakeKeyedRate').textContent = `${intakeToKeyedRate}/min`;
      document.getElementById('keyedCanonRate').textContent = `${keyedToProcessedRate}/min`;
      
      // Update progress bars based on throughput
      const intakeKeyedPercent = Math.min(100, Math.max(0, (intakeToKeyedRate / 100) * 100));
      const keyedCanonPercent = Math.min(100, Math.max(0, (keyedToProcessedRate / 100) * 100));
      
      document.getElementById('intakeKeyedBar').style.width = `${intakeKeyedPercent}%`;
      document.getElementById('keyedCanonBar').style.width = `${keyedCanonPercent}%`;
      
    } catch (error) {
      console.error('Failed to update flow visualization:', error);
    }
  }

  calculateFlowRate(current, next) {
    // Simple heuristic: if we have more in current stage than next, there's active flow
    // In real implementation, this would be based on timestamp deltas and actual flow metrics
    if (!current || !next) return 0;
    
    const flowRate = Math.max(0, current - next);
    return Math.min(150, flowRate); // Cap at reasonable rate for visualization
  }

  extractCount(response) {
    // Try to extract count from various response formats
    if (response?.total) return response.total;
    if (response?.count) return response.count;
    if (response?.items?.length !== undefined) return response.items.length;
    if (Array.isArray(response)) return response.length;
    return null;
  }

  async loadEntityBrowser() {
    try {
      const entities = await this.fetchEntitiesForBrowser();
      this.renderEntityTree(entities);
    } catch (error) {
      console.error('Failed to load entity browser:', error);
      this.showEntityBrowserError();
    }
  }

  async fetchEntitiesForBrowser() {
    try {
      const [devices, sensors, manufacturers] = await Promise.all([
        window.flowApi.getDevices({ size: 10 }),
        window.flowApi.getSensors({ size: 20 }),
        window.flowApi.getManufacturers({ size: 10 }).catch(() => ({ items: [] })) // Graceful fallback
      ]);

      const entities = [];
      
      // Add devices
      if (devices?.items) {
        devices.items.forEach(device => {
          entities.push({
            id: device.id,
            type: 'device',
            name: device.canonicalId || device.id,
            meta: (device.model?.name || device.metadata?.name || 'Device'),
            children: []
          });
        });
      }

      // Add sensors (group under devices where possible)
      if (sensors?.items) {
        sensors.items.forEach(sensor => {
          entities.push({
            id: sensor.id,
            type: 'sensor',
            name: sensor.canonicalId || sensor.id,
            meta: (sensor.model?.type || sensor.metadata?.type || 'Sensor'),
            parent: sensor.model?.deviceId
          });
        });
      }

      // Add manufacturers
      if (manufacturers?.items) {
        manufacturers.items.forEach(manufacturer => {
          const name = manufacturer.model?.identifier?.name || manufacturer.canonicalId || manufacturer.id;
          const code = manufacturer.model?.identifier?.code || 'MFG';
          entities.push({
            id: manufacturer.id,
            type: 'manufacturer',
            name: name,
            meta: code,
            children: []
          });
        });
      }

      return entities;
    } catch (error) {
      console.error('Failed to fetch entities for browser:', error);
      return [];
    }
  }

  renderEntityTree(entities) {
    const container = document.getElementById('entityTree');
    if (!container) return;

    if (entities.length === 0) {
      container.innerHTML = '<div class="text-gray-400 text-sm">No entities found</div>';
      return;
    }

    const html = entities.map(entity => `
      <div class="entity-tree-item entity-tree-item--${entity.type}" data-entity-id="${entity.id}" data-entity-type="${entity.type}">
        <i class="entity-tree-item__icon fas fa-${this.getEntityIcon(entity.type)}"></i>
        <span class="entity-tree-item__label">${entity.name}</span>
        <span class="entity-tree-item__meta">${entity.meta}</span>
      </div>
    `).join('');

    container.innerHTML = html;

    // Add click handlers
    container.querySelectorAll('.entity-tree-item').forEach(item => {
      item.addEventListener('click', () => {
        this.selectEntity(item);
      });
    });
  }

  getEntityIcon(type) {
    switch (type) {
      case 'device': return 'microchip';
      case 'sensor': return 'thermometer-half';
      case 'manufacturer': return 'building';
      case 'reading': return 'chart-line';
      default: return 'circle';
    }
  }

  selectEntity(element) {
    // Clear previous selections
    document.querySelectorAll('.entity-tree-item--selected').forEach(item => {
      item.classList.remove('entity-tree-item--selected');
    });

    // Select current item
    element.classList.add('entity-tree-item--selected');
    
    const entityId = element.dataset.entityId;
    const entityType = element.dataset.entityType;
    
    this.selectedEntities.clear();
    this.selectedEntities.add({ id: entityId, type: entityType });
  }

  async loadActivityFeed() {
    try {
      const activities = await window.flowApi.getRecentActivity(10);
      this.renderActivityFeed(activities);
    } catch (error) {
      console.error('Failed to load activity feed:', error);
      this.showActivityFeedError();
    }
  }

  async updateActivityFeed() {
    if (this.activityPaused) return;
    
    try {
      const activities = await window.flowApi.getRecentActivity(5);
      this.addToActivityFeed(activities);
    } catch (error) {
      console.error('Failed to update activity feed:', error);
    }
  }

  renderActivityFeed(activities) {
    const container = document.getElementById('activityFeed');
    if (!container) return;

    if (activities.length === 0) {
      container.innerHTML = '<div class="text-gray-400 text-sm">No recent activity</div>';
      return;
    }

    const html = activities.map(activity => this.createActivityItem(activity)).join('');
    container.innerHTML = html;
  }

  addToActivityFeed(newActivities) {
    const container = document.getElementById('activityFeed');
    if (!container || newActivities.length === 0) return;

    const newHtml = newActivities.map(activity => this.createActivityItem(activity)).join('');
    container.insertAdjacentHTML('afterbegin', newHtml);

    // Remove old items to keep feed manageable
    const items = container.querySelectorAll('.activity-item');
    if (items.length > 20) {
      for (let i = 20; i < items.length; i++) {
        items[i].remove();
      }
    }
  }

  createActivityItem(activity) {
    const time = this.formatTime(new Date(activity.timestamp));
    const icon = this.getActivityIcon(activity.type, activity.level);
    
    return `
      <div class="activity-item">
        <div class="activity-item__icon activity-item__icon--${activity.level}">
          <i class="fas fa-${icon}"></i>
        </div>
        <div class="activity-item__content">
          <div class="activity-item__title">${activity.title}</div>
          <div class="activity-item__subtitle">${activity.subtitle}</div>
        </div>
        <div class="activity-item__time">${time}</div>
      </div>
    `;
  }

  getActivityIcon(type, level) {
    if (level === 'error') return 'exclamation-triangle';
    if (level === 'warning') return 'exclamation-circle';
    
    switch (type) {
      case 'reading': return 'chart-line';
      case 'device': return 'microchip';
      case 'sensor': return 'thermometer-half';
      case 'adapter': return 'plug';
      case 'policy': return 'shield-alt';
      default: return 'info-circle';
    }
  }

  formatTime(date) {
    const now = new Date();
    const diff = now - date;
    
    if (diff < 60000) return 'now';
    if (diff < 3600000) return `${Math.floor(diff / 60000)}m`;
    if (diff < 86400000) return `${Math.floor(diff / 3600000)}h`;
    
    return date.toLocaleTimeString();
  }

  updateLastUpdateTime() {
    const element = document.getElementById('lastUpdateTime');
    if (element) {
      const now = new Date();
      const diff = now - this.lastUpdateTime;
      
      if (diff < 60000) {
        element.textContent = 'Just now';
      } else if (diff < 3600000) {
        element.textContent = `${Math.floor(diff / 60000)}m ago`;
      } else {
        element.textContent = `${Math.floor(diff / 3600000)}h ago`;
      }
    }
  }

  // Search and filter methods
  searchEntities(query) {
    const items = document.querySelectorAll('.entity-tree-item');
    items.forEach(item => {
      const label = item.querySelector('.entity-tree-item__label').textContent.toLowerCase();
      const meta = item.querySelector('.entity-tree-item__meta').textContent.toLowerCase();
      const matches = label.includes(query.toLowerCase()) || meta.includes(query.toLowerCase());
      
      item.style.display = matches ? 'flex' : 'none';
    });
  }

  filterEntities(type) {
    const items = document.querySelectorAll('.entity-tree-item');
    items.forEach(item => {
      const entityType = item.dataset.entityType;
      const matches = type === 'all' || entityType === type;
      
      item.style.display = matches ? 'flex' : 'none';
    });
  }

  filterActivity(level) {
    const items = document.querySelectorAll('.activity-item');
    items.forEach(item => {
      const icon = item.querySelector('.activity-item__icon');
      const matches = level === 'all' || 
                     (level === 'success' && icon.classList.contains('activity-item__icon--success')) ||
                     (level === 'warning' && icon.classList.contains('activity-item__icon--warning')) ||
                     (level === 'error' && icon.classList.contains('activity-item__icon--error'));
      
      item.style.display = matches ? 'flex' : 'none';
    });
  }

  // Utility methods
  debounce(func, wait) {
    let timeout;
    return function executedFunction(...args) {
      const later = () => {
        clearTimeout(timeout);
        func(...args);
      };
      clearTimeout(timeout);
      timeout = setTimeout(later, wait);
    };
  }

  // Error handling methods
  showSystemError(message) {
    this.addToActivityFeed([{
      type: 'system',
      timestamp: new Date().toISOString(),
      title: 'System Error',
      subtitle: message,
      level: 'error'
    }]);
  }

  showAdapterError() {
    document.getElementById('adaptersBms').textContent = '○';
    document.getElementById('adaptersBms').className = 'text-2xl font-bold text-gray-400';
    document.getElementById('adaptersOem').textContent = '○';
    document.getElementById('adaptersOem').className = 'text-2xl font-bold text-gray-400';
  }

  showEntityCountError() {
    document.getElementById('deviceCount').textContent = '–';
    document.getElementById('sensorCount').textContent = '–';
    document.getElementById('readingCount').textContent = '–';
    document.getElementById('manufacturerCount').textContent = '–';
  }

  showEntityBrowserError() {
    const container = document.getElementById('entityTree');
    if (container) {
      container.innerHTML = '<div class="text-red-400 text-sm">Failed to load entities</div>';
    }
  }

  showActivityFeedError() {
    const container = document.getElementById('activityFeed');
    if (container) {
      container.innerHTML = '<div class="text-red-400 text-sm">Failed to load activity</div>';
    }
  }

  // Public methods for UI interactions
  refreshPipeline() {
    this.lastUpdateTime = new Date();
    this.updatePipelineHealth();
    this.updateEntityCounts();
  }

  clearActivityFeed() {
    const container = document.getElementById('activityFeed');
    if (container) {
      container.innerHTML = '<div class="text-gray-400 text-sm">Activity cleared</div>';
    }
  }

  toggleActivityPause() {
    this.activityPaused = !this.activityPaused;
    const button = document.getElementById('pauseButton');
    if (button) {
      button.innerHTML = this.activityPaused 
        ? '<i class="fas fa-play mr-1"></i>Resume'
        : '<i class="fas fa-pause mr-1"></i>Pause';
    }
  }

  exportActivity() {
    const items = document.querySelectorAll('.activity-item');
    const data = Array.from(items).map(item => ({
      title: item.querySelector('.activity-item__title').textContent,
      subtitle: item.querySelector('.activity-item__subtitle').textContent,
      time: item.querySelector('.activity-item__time').textContent
    }));
    
    const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `s8-flow-activity-${new Date().toISOString().slice(0, 10)}.json`;
    a.click();
    URL.revokeObjectURL(url);
  }

  exportEntities() {
    const selectedEntity = Array.from(this.selectedEntities)[0];
    if (!selectedEntity) {
      alert('Please select an entity to export');
      return;
    }
    
    // Export selected entity data
    const data = { id: selectedEntity.id, type: selectedEntity.type, exported: new Date().toISOString() };
    const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `s8-flow-entity-${selectedEntity.id}.json`;
    a.click();
    URL.revokeObjectURL(url);
  }

  viewEntityDetails() {
    const selectedEntity = Array.from(this.selectedEntities)[0];
    if (!selectedEntity) {
      alert('Please select an entity to view');
      return;
    }
    
    // Open entity details modal (to be implemented in modals.js)
    if (window.openEntityDetailsModal) {
      window.openEntityDetailsModal(selectedEntity.id, selectedEntity.type);
    }
  }

  showEntityLineage() {
    const selectedEntity = Array.from(this.selectedEntities)[0];
    if (!selectedEntity) {
      alert('Please select an entity to view lineage');
      return;
    }
    
    // Open lineage modal (to be implemented in modals.js)
    if (window.openLineageModal) {
      window.openLineageModal(selectedEntity.id, selectedEntity.type);
    }
  }
}

// Global functions for onclick handlers
window.setActiveTab = (tab) => dashboard.setActiveTab(tab);
window.refreshPipeline = () => dashboard.refreshPipeline();
window.clearActivityFeed = () => dashboard.clearActivityFeed();
window.toggleActivityPause = () => dashboard.toggleActivityPause();
window.exportActivity = () => dashboard.exportActivity();
window.exportEntities = () => dashboard.exportEntities();
window.viewEntityDetails = () => dashboard.viewEntityDetails();
window.showEntityLineage = () => dashboard.showEntityLineage();

// Initialize dashboard when DOM is ready
document.addEventListener('DOMContentLoaded', () => {
  window.dashboard = new FlowDashboard();
});
