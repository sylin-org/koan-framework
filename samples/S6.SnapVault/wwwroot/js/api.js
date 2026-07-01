/**
 * API Client for SnapVault Pro
 * Handles all HTTP requests to the backend
 */

export class API {
  constructor() {
    this.baseUrl = window.location.origin;
  }

  /**
   * Build an Error from a non-2xx response, surfacing the server's JSON error body when present.
   * The backend returns { error: "..." } (e.g. "Collection limit reached (2048 photos maximum)."); callers
   * grep error.message (the collection-cap toast checks for "limit"), so the body must reach them — the bare
   * status line would hide it. Falls back to "HTTP <status>: <statusText>" when the body is absent/not JSON.
   * @param {Response} response
   * @returns {Promise<Error>}
   */
  async _errorFrom(response) {
    let message = `HTTP ${response.status}: ${response.statusText}`;
    try {
      const text = await response.text();
      if (text) {
        const body = JSON.parse(text);
        if (body && (body.error || body.message)) {
          message = body.error || body.message;
        }
      }
    } catch {
      // Non-JSON body — keep the status line.
    }
    return new Error(message);
  }

  async get(url, params = {}, options = {}) {
    const queryString = new URLSearchParams(params).toString();
    const fullUrl = `${this.baseUrl}${url}${queryString ? `?${queryString}` : ''}`;

    const response = await fetch(fullUrl, {
      method: 'GET',
      headers: {
        'Accept': 'application/json'
      }
    });

    if (!response.ok) {
      throw await this._errorFrom(response);
    }

    const data = await response.json();

    // If caller wants headers, return both data and headers
    if (options.includeHeaders) {
      return {
        data,
        headers: {
          totalCount: parseInt(response.headers.get('X-Total-Count') || '0'),
          page: parseInt(response.headers.get('X-Page') || '1'),
          pageSize: parseInt(response.headers.get('X-Page-Size') || '30'),
          totalPages: parseInt(response.headers.get('X-Total-Pages') || '0')
        }
      };
    }

    return data;
  }

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
      throw await this._errorFrom(response);
    }

    // Handle empty responses
    const text = await response.text();
    return text ? JSON.parse(text) : null;
  }

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
      throw await this._errorFrom(response);
    }

    return await response.json();
  }

  async delete(url) {
    const response = await fetch(`${this.baseUrl}${url}`, {
      method: 'DELETE'
    });

    if (!response.ok) {
      throw await this._errorFrom(response);
    }

    return response.ok;
  }

  async upload(url, formData, onProgress = null) {
    return new Promise((resolve, reject) => {
      const xhr = new XMLHttpRequest();

      if (onProgress) {
        xhr.upload.addEventListener('progress', (e) => {
          if (e.lengthComputable) {
            const percentComplete = (e.loaded / e.total) * 100;
            onProgress(percentComplete);
          }
        });
      }

      xhr.addEventListener('load', () => {
        if (xhr.status >= 200 && xhr.status < 300) {
          resolve(JSON.parse(xhr.responseText));
        } else {
          let message = `HTTP ${xhr.status}: ${xhr.statusText}`;
          try {
            const body = JSON.parse(xhr.responseText);
            if (body && (body.error || body.message)) message = body.error || body.message;
          } catch {
            // Non-JSON body — keep the status line.
          }
          reject(new Error(message));
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
