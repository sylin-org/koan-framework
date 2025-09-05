/**
 * S8 Flow Operations Dashboard - API Client
 * Handles all API communication with S8 Flow endpoints
 */

class FlowApiClient {
  constructor() {
    this.baseUrl = '';
    this.defaultHeaders = {
      'Accept': 'application/json',
      'Content-Type': 'application/json'
    };
  }

  async fetchJson(url, options = {}) {
    const response = await fetch(url, {
      headers: { ...this.defaultHeaders, ...options.headers },
      ...options
    });
    
    if (!response.ok) {
      throw new Error(`${response.status} ${response.statusText}`);
    }
    
    return response.json();
  }

  // Health and Status APIs
  async getAdapterHealth() {
    return this.fetchJson('/adapters/health');
  }

  async getSystemHealth() {
    return this.fetchJson('/health');
  }

  async getObservability() {
    return this.fetchJson('/.well-known/sora/observability');
  }

  // Entity APIs - Devices
  async getDevices(params = {}) {
    const query = new URLSearchParams(params).toString();
    return this.fetchJson(`/api/devices${query ? '?' + query : ''}`);
  }

  async getDevice(id) {
    return this.fetchJson(`/api/devices/${id}`);
  }

  async getDeviceByCanonicalId(canonicalId) {
    return this.fetchJson(`/api/devices/by-cid/${canonicalId}`);
  }

  async queryDevices(query) {
    return this.fetchJson('/api/devices/query', {
      method: 'POST',
      body: JSON.stringify(query)
    });
  }

  // Entity APIs - Sensors  
  async getSensors(params = {}) {
    const query = new URLSearchParams(params).toString();
    return this.fetchJson(`/api/sensors${query ? '?' + query : ''}`);
  }

  async getSensor(id) {
    return this.fetchJson(`/api/sensors/${id}`);
  }

  async getSensorByCanonicalId(canonicalId) {
    return this.fetchJson(`/api/sensors/by-cid/${canonicalId}`);
  }

  async querySensors(query) {
    return this.fetchJson('/api/sensors/query', {
      method: 'POST',
      body: JSON.stringify(query)
    });
  }

  // Entity APIs - Manufacturers
  async getManufacturers(params = {}) {
    const query = new URLSearchParams(params).toString();
    return this.fetchJson(`/api/manufacturers${query ? '?' + query : ''}`);
  }

  async getManufacturer(id) {
    return this.fetchJson(`/api/manufacturers/${id}`);
  }

  async getManufacturerByCanonicalId(canonicalId) {
    return this.fetchJson(`/api/manufacturers/by-cid/${canonicalId}`);
  }

  async queryManufacturers(query) {
    return this.fetchJson('/api/manufacturers/query', {
      method: 'POST',
      body: JSON.stringify(query)
    });
  }

  // Value Object APIs - Readings
  async getReadings(params = {}) {
    const query = new URLSearchParams(params).toString();
  return this.fetchJson(`/api/readings${query ? '?' + query : ''}`);
  }

  async getReadingsByReference(referenceId, params = {}) {
    const query = new URLSearchParams(params).toString();
    return this.fetchJson(`/api/readings/${referenceId}${query ? '?' + query : ''}`);
  }

  async getReadingsByCanonicalId(canonicalId, params = {}) {
    const query = new URLSearchParams(params).toString();
    return this.fetchJson(`/api/readings/by-cid/${canonicalId}${query ? '?' + query : ''}`);
  }

  async postReading(reading) {
    return this.fetchJson('/api/readings', {
      method: 'POST',
      body: JSON.stringify(reading)
    });
  }

  // Flow Entity APIs - Advanced Views
  async getFlowDevices(params = {}) {
    const query = new URLSearchParams(params).toString();
  // Fallback to base device listing for now
  return this.fetchJson(`/api/devices${query ? '?' + query : ''}`);
  }

  async getFlowSensors(params = {}) {
    const query = new URLSearchParams(params).toString();
  // Fallback to base sensor listing for now
  return this.fetchJson(`/api/sensors${query ? '?' + query : ''}`);
  }

  async getCanonicalView(model, referenceId) {
    return this.fetchJson(`/api/flow/${model}/views/canonical/${referenceId}`);
  }

  async getLineageView(model, referenceId) {
    return this.fetchJson(`/api/flow/${model}/views/lineage/${referenceId}`);
  }

  // Generic View APIs
  async getModelView(model, view, params = {}) {
    const query = new URLSearchParams(params).toString();
    return this.fetchJson(`/models/${model}/views/${view}${query ? '?' + query : ''}`);
  }

  async getModelViewByReference(model, view, referenceId) {
    return this.fetchJson(`/models/${model}/views/${view}/${referenceId}`);
  }

  async getView(view, params = {}) {
    const query = new URLSearchParams(params).toString();
    return this.fetchJson(`/views/${view}${query ? '?' + query : ''}`);
  }

  async getViewByReference(view, referenceId) {
    return this.fetchJson(`/views/${view}/${referenceId}`);
  }

  // Administrative APIs
  async replayData(replayDto) {
    return this.fetchJson('/admin/replay', {
      method: 'POST',
      body: JSON.stringify(replayDto)
    });
  }

  async reprojectView(reprojectDto) {
    return this.fetchJson('/admin/reproject', {
      method: 'POST',
      body: JSON.stringify(reprojectDto)
    });
  }

  // Policy APIs
  async getPolicies() {
    return this.fetchJson('/policies');
  }

  async updatePolicies(policyBundle) {
    return this.fetchJson('/policies', {
      method: 'PUT',
      body: JSON.stringify(policyBundle)
    });
  }

  // Intake APIs
  async ingestRecord(intakeRecord) {
    return this.fetchJson('/intake/records', {
      method: 'POST',
      body: JSON.stringify(intakeRecord)
    });
  }

  // Lineage APIs
  async getLineage(referenceId) {
    return this.fetchJson(`/lineage/${referenceId}`);
  }

  // Bulk Operations
  async bulkCreateDevices(devices) {
    return this.fetchJson('/api/devices/bulk', {
      method: 'POST',
      body: JSON.stringify(devices)
    });
  }

  async bulkCreateSensors(sensors) {
    return this.fetchJson('/api/sensors/bulk', {
      method: 'POST',
      body: JSON.stringify(sensors)
    });
  }

  async bulkCreateReadings(readings) {
  // Endpoint not implemented server-side; keep for future wiring
  return this.fetchJson('/api/readings/bulk', {
      method: 'POST',
      body: JSON.stringify(readings)
    });
  }

  async bulkDeleteDevices(deviceIds) {
    return this.fetchJson('/api/devices/bulk', {
      method: 'DELETE',
      body: JSON.stringify(deviceIds)
    });
  }

  async bulkDeleteSensors(sensorIds) {
    return this.fetchJson('/api/sensors/bulk', {
      method: 'DELETE',
      body: JSON.stringify(sensorIds)
    });
  }

  // Utility methods for error handling and response processing
  extractTraceId(response) {
    return response.headers.get('Sora-Trace-Id');
  }

  isInMemoryPaging(response) {
    return response.headers.get('Sora-InMemory-Paging') === 'true';
  }

  // High-level convenience methods
  async getEntityCounts() {
    try {
      const [devices, sensors, readings, manufacturers] = await Promise.all([
        this.getDevices({ size: 1 }),
        this.getSensors({ size: 1 }),
        this.getReadings({ size: 1 }),
        this.getManufacturers({ size: 1 }).catch(() => ({ items: [] })) // Graceful fallback if endpoint doesn't exist
      ]);

      return {
        devices: devices?.items?.length ?? (Array.isArray(devices) ? devices.length : 0),
        sensors: sensors?.items?.length ?? (Array.isArray(sensors) ? sensors.length : 0),  
        readings: readings?.items?.length ?? (Array.isArray(readings) ? readings.length : 0),
        manufacturers: manufacturers?.items?.length ?? (Array.isArray(manufacturers) ? manufacturers.length : 0)
      };
    } catch (error) {
      console.error('Failed to get entity counts:', error);
      return { devices: 0, sensors: 0, readings: 0, manufacturers: 0 };
    }
  }

  async getRecentActivity(limit = 20) {
    try {
      // Get recent readings as a proxy for activity
      const readings = await this.getReadings({ size: limit });
      
      return (readings?.items || []).map(reading => ({
        type: 'reading',
        timestamp: reading.at || new Date().toISOString(),
        title: `Reading ingested`,
  subtitle: `Sensor: ${reading.payload?.sensorKey || reading.correlationId || 'Unknown'}`,
        level: 'success',
        data: reading
      }));
    } catch (error) {
      console.error('Failed to get recent activity:', error);
      return [];
    }
  }
}

// Export a singleton instance
window.flowApi = new FlowApiClient();

// Add process overview API method
FlowApiClient.prototype.getFlowOverview = async function() {
  return this.fetchJson('/api/flow/overview');
};

// Flow seeding
FlowApiClient.prototype.seedFlow = async function(count) {
  return this.fetchJson(`/api/flow/seed`, {
    method: 'POST',
    body: JSON.stringify({ count: count || 10 })
  });
};
