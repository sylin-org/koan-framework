/**
 * API Client for Prism
 * Handles all HTTP requests to the backend
 */

const DEFAULT_TIMEOUT = 30000;

export class API {
    constructor() {
        this.baseUrl = window.location.origin;
    }

    /**
     * GET request
     * @param {string} url - API path
     * @param {object} params - Query parameters
     * @param {number} timeout - Request timeout in ms (default 30s)
     * @returns {Promise<any>} Response data
     */
    async get(url, params = {}, timeout = DEFAULT_TIMEOUT) {
        const queryString = new URLSearchParams(params).toString();
        const fullUrl = `${this.baseUrl}${url}${queryString ? `?${queryString}` : ''}`;

        const controller = new AbortController();
        const timer = setTimeout(() => controller.abort(), timeout);

        try {
            const response = await fetch(fullUrl, {
                method: 'GET',
                headers: { 'Accept': 'application/json' },
                signal: controller.signal
            });

            if (!response.ok) {
                throw new Error(`HTTP ${response.status}: ${response.statusText}`);
            }

            return await response.json();
        } catch (error) {
            if (error.name === 'AbortError') {
                throw new Error('Request timed out');
            }
            throw error;
        } finally {
            clearTimeout(timer);
        }
    }

    /**
     * POST request with JSON body
     * @param {string} url - API path
     * @param {any} data - Request body
     * @param {number} timeout - Request timeout in ms (default 30s)
     * @returns {Promise<any>} Response data
     */
    async post(url, data = null, timeout = DEFAULT_TIMEOUT) {
        const controller = new AbortController();
        const timer = setTimeout(() => controller.abort(), timeout);

        try {
            const response = await fetch(`${this.baseUrl}${url}`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'Accept': 'application/json'
                },
                body: data ? JSON.stringify(data) : null,
                signal: controller.signal
            });

            if (!response.ok) {
                throw new Error(`HTTP ${response.status}: ${response.statusText}`);
            }

            const text = await response.text();
            return text ? JSON.parse(text) : null;
        } catch (error) {
            if (error.name === 'AbortError') {
                throw new Error('Request timed out');
            }
            throw error;
        } finally {
            clearTimeout(timer);
        }
    }

    /**
     * PUT request with JSON body
     * @param {string} url - API path
     * @param {any} data - Request body
     * @param {number} timeout - Request timeout in ms (default 30s)
     * @returns {Promise<any>} Response data
     */
    async put(url, data, timeout = DEFAULT_TIMEOUT) {
        const controller = new AbortController();
        const timer = setTimeout(() => controller.abort(), timeout);

        try {
            const response = await fetch(`${this.baseUrl}${url}`, {
                method: 'PUT',
                headers: {
                    'Content-Type': 'application/json',
                    'Accept': 'application/json'
                },
                body: JSON.stringify(data),
                signal: controller.signal
            });

            if (!response.ok) {
                throw new Error(`HTTP ${response.status}: ${response.statusText}`);
            }

            return await response.json();
        } catch (error) {
            if (error.name === 'AbortError') {
                throw new Error('Request timed out');
            }
            throw error;
        } finally {
            clearTimeout(timer);
        }
    }

    /**
     * DELETE request
     * @param {string} url - API path
     * @param {number} timeout - Request timeout in ms (default 30s)
     * @returns {Promise<boolean>} Success
     */
    async delete(url, timeout = DEFAULT_TIMEOUT) {
        const controller = new AbortController();
        const timer = setTimeout(() => controller.abort(), timeout);

        try {
            const response = await fetch(`${this.baseUrl}${url}`, {
                method: 'DELETE',
                signal: controller.signal
            });

            if (!response.ok) {
                throw new Error(`HTTP ${response.status}: ${response.statusText}`);
            }

            return response.ok;
        } catch (error) {
            if (error.name === 'AbortError') {
                throw new Error('Request timed out');
            }
            throw error;
        } finally {
            clearTimeout(timer);
        }
    }

    /**
     * Upload file with progress tracking
     * @param {string} url - API path
     * @param {FormData} formData - Form data with file
     * @param {Function} onProgress - Progress callback (0-100)
     * @returns {Promise<any>} Response data
     */
    async upload(url, formData, onProgress = null) {
        return new Promise((resolve, reject) => {
            const xhr = new XMLHttpRequest();

            if (onProgress) {
                xhr.upload.addEventListener('progress', (e) => {
                    if (e.lengthComputable) {
                        onProgress((e.loaded / e.total) * 100);
                    }
                });
            }

            xhr.addEventListener('load', () => {
                if (xhr.status >= 200 && xhr.status < 300) {
                    resolve(xhr.responseText ? JSON.parse(xhr.responseText) : null);
                } else {
                    reject(new Error(`HTTP ${xhr.status}: ${xhr.statusText}`));
                }
            });

            xhr.addEventListener('error', () => {
                reject(new Error('Upload failed'));
            });

            xhr.addEventListener('timeout', () => {
                reject(new Error('Upload timed out'));
            });

            xhr.timeout = 120000; // 2 minutes for file uploads

            xhr.open('POST', `${this.baseUrl}${url}`);
            xhr.send(formData);
        });
    }
}
