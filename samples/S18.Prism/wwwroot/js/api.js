/**
 * API Client for Prism
 * Handles all HTTP requests to the backend
 */

export class API {
    constructor() {
        this.baseUrl = window.location.origin;
    }

    /**
     * GET request
     * @param {string} url - API path
     * @param {object} params - Query parameters
     * @returns {Promise<any>} Response data
     */
    async get(url, params = {}) {
        const queryString = new URLSearchParams(params).toString();
        const fullUrl = `${this.baseUrl}${url}${queryString ? `?${queryString}` : ''}`;

        const response = await fetch(fullUrl, {
            method: 'GET',
            headers: { 'Accept': 'application/json' }
        });

        if (!response.ok) {
            throw new Error(`HTTP ${response.status}: ${response.statusText}`);
        }

        return await response.json();
    }

    /**
     * POST request with JSON body
     * @param {string} url - API path
     * @param {any} data - Request body
     * @returns {Promise<any>} Response data
     */
    async post(url, data = null) {
        const response = await fetch(`${this.baseUrl}${url}`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'Accept': 'application/json'
            },
            body: data ? JSON.stringify(data) : null
        });

        if (!response.ok) {
            throw new Error(`HTTP ${response.status}: ${response.statusText}`);
        }

        const text = await response.text();
        return text ? JSON.parse(text) : null;
    }

    /**
     * PUT request with JSON body
     * @param {string} url - API path
     * @param {any} data - Request body
     * @returns {Promise<any>} Response data
     */
    async put(url, data) {
        const response = await fetch(`${this.baseUrl}${url}`, {
            method: 'PUT',
            headers: {
                'Content-Type': 'application/json',
                'Accept': 'application/json'
            },
            body: JSON.stringify(data)
        });

        if (!response.ok) {
            throw new Error(`HTTP ${response.status}: ${response.statusText}`);
        }

        return await response.json();
    }

    /**
     * DELETE request
     * @param {string} url - API path
     * @returns {Promise<boolean>} Success
     */
    async delete(url) {
        const response = await fetch(`${this.baseUrl}${url}`, {
            method: 'DELETE'
        });

        if (!response.ok) {
            throw new Error(`HTTP ${response.status}: ${response.statusText}`);
        }

        return response.ok;
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

            xhr.open('POST', `${this.baseUrl}${url}`);
            xhr.send(formData);
        });
    }
}
