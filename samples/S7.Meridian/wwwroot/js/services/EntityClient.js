/**
 * EntityClient - Generic CRUD client for EntityController<T> endpoints
 *
 * Eliminates duplication by providing standard operations for all entity types.
 * EntityController<T> provides consistent REST API surface:
 * - GET    /api/{endpoint}      -> List all
 * - GET    /api/{endpoint}/{id} -> Get by ID
 * - POST   /api/{endpoint}      -> Create
 * - PUT    /api/{endpoint}/{id} -> Update
 * - DELETE /api/{endpoint}/{id} -> Delete
 */
export class EntityClient {
  /**
   * Create a new entity client
   * @param {string} endpoint - Base API endpoint (e.g., 'analysis-types')
   * @param {string} baseUrl - Base API URL (default: window.location.origin)
   */
  constructor(endpoint, baseUrl = window.location.origin) {
    this.endpoint = endpoint;
    this.baseUrl = baseUrl;
    this.baseApiPath = `/api/${endpoint}`;
  }

  /**
   * Get all entities
   * @param {Object} queryParams - Optional query parameters
   * @returns {Promise<Array>}
   */
  async getAll(queryParams = {}) {
    const url = this.buildUrl(this.baseApiPath, queryParams);
    const response = await fetch(url, {
      method: 'GET',
      headers: this.getHeaders()
    });

    if (!response.ok) {
      throw new Error(`Failed to fetch ${this.endpoint}: ${response.statusText}`);
    }

    return await response.json();
  }

  /**
   * Get entity by ID
   * @param {string} id - Entity ID
   * @returns {Promise<Object>}
   */
  async getById(id) {
    const url = this.buildUrl(`${this.baseApiPath}/${id}`);
    const response = await fetch(url, {
      method: 'GET',
      headers: this.getHeaders()
    });

    if (!response.ok) {
      if (response.status === 404) {
        throw new Error(`${this.endpoint} not found: ${id}`);
      }
      throw new Error(`Failed to fetch ${this.endpoint}: ${response.statusText}`);
    }

    return await response.json();
  }

  /**
   * Create new entity
   * @param {Object} data - Entity data
   * @returns {Promise<Object>}
   */
  async create(data) {
    const url = this.buildUrl(this.baseApiPath);
    const response = await fetch(url, {
      method: 'POST',
      headers: this.getHeaders(),
      body: JSON.stringify(data)
    });

    if (!response.ok) {
      const errorText = await response.text();
      throw new Error(`Failed to create ${this.endpoint}: ${errorText || response.statusText}`);
    }

    return await response.json();
  }

  /**
   * Update existing entity
   * @param {string} id - Entity ID
   * @param {Object} data - Updated entity data
   * @returns {Promise<Object>}
   */
  async update(id, data) {
    const url = this.buildUrl(`${this.baseApiPath}/${id}`);
    const response = await fetch(url, {
      method: 'PUT',
      headers: this.getHeaders(),
      body: JSON.stringify(data)
    });

    if (!response.ok) {
      const errorText = await response.text();
      throw new Error(`Failed to update ${this.endpoint}: ${errorText || response.statusText}`);
    }

    return await response.json();
  }

  /**
   * Delete entity
   * @param {string} id - Entity ID
   * @returns {Promise<void>}
   */
  async delete(id) {
    const url = this.buildUrl(`${this.baseApiPath}/${id}`);
    const response = await fetch(url, {
      method: 'DELETE',
      headers: this.getHeaders()
    });

    if (!response.ok) {
      if (response.status === 404) {
        throw new Error(`${this.endpoint} not found: ${id}`);
      }
      const errorText = await response.text();
      throw new Error(`Failed to delete ${this.endpoint}: ${errorText || response.statusText}`);
    }

    // DELETE may return 204 No Content or 200 with data
    if (response.status === 204) {
      return;
    }

    return await response.json();
  }

  /**
   * Build full URL with query parameters
   * @param {string} path - API path
   * @param {Object} queryParams - Query parameters
   * @returns {string}
   */
  buildUrl(path, queryParams = {}) {
    const url = new URL(path, this.baseUrl);

    Object.entries(queryParams).forEach(([key, value]) => {
      if (value !== null && value !== undefined) {
        url.searchParams.append(key, value);
      }
    });

    return url.toString();
  }

  /**
   * Get standard headers
   * @returns {Object}
   */
  getHeaders() {
    return {
      'Content-Type': 'application/json',
      'Accept': 'application/json'
    };
  }
}

/**
 * Create entity client for a specific endpoint
 * @param {string} endpoint - API endpoint name
 * @returns {EntityClient}
 */
export function createEntityClient(endpoint) {
  return new EntityClient(endpoint);
}
