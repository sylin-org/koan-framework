// API Client for S7 TechDocs
class API {
  constructor() {
    this.baseUrl = '';
  }

  async get(endpoint) {
    try {
      const response = await fetch(`${this.baseUrl}${endpoint}`);
      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }
      return await response.json();
    } catch (error) {
      console.error('API GET error:', error);
      throw error;
    }
  }

  async post(endpoint, data) {
    try {
      const response = await fetch(`${this.baseUrl}${endpoint}`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json'
        },
        body: JSON.stringify(data)
      });
      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }
      return await response.json();
    } catch (error) {
      console.error('API POST error:', error);
      throw error;
    }
  }

  async put(endpoint, data) {
    try {
      const response = await fetch(`${this.baseUrl}${endpoint}`, {
        method: 'PUT',
        headers: {
          'Content-Type': 'application/json'
        },
        body: JSON.stringify(data)
      });
      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }
      return await response.json();
    } catch (error) {
      console.error('API PUT error:', error);
      throw error;
    }
  }

  async patch(endpoint, data) {
    try {
      const response = await fetch(`${this.baseUrl}${endpoint}`, {
        method: 'PATCH',
        headers: {
          'Content-Type': 'application/json'
        },
        body: JSON.stringify(data)
      });
      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }
      return await response.json();
    } catch (error) {
      console.error('API PATCH error:', error);
      throw error;
    }
  }

  async delete(endpoint) {
    try {
      const response = await fetch(`${this.baseUrl}${endpoint}`, {
        method: 'DELETE'
      });
      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }
      // DELETE might not return content
      if (response.status === 204) {
        return null;
      }
      return await response.json();
    } catch (error) {
      console.error('API DELETE error:', error);
      throw error;
    }
  }

  // Documents API
  async getDocuments(collection = null, status = null) {
    let endpoint = '/api/documents';
    const params = new URLSearchParams();
    if (collection) params.append('collection', collection);
    if (status) params.append('status', status);
    if (params.toString()) endpoint += `?${params.toString()}`;
    return this.get(endpoint);
  }

  async getDocument(id) {
  return this.get(`/api/documents/${id}`);
  }

  async createDocument(document) {
    return this.post('/api/documents', document);
  }

  async updateDocument(id, document) {
    return this.put(`/api/documents/${id}`, document);
  }

  async deleteDocument(id) {
    return this.delete(`/api/documents/${id}`);
  }


  async trackDocumentView(id) {
    return this.post(`/api/documents/${id}/view`, {});
  }

  // Moderation API (EntityModerationController)
  async createDraft(id, snapshot) {
    return this.post(`/api/documents/${id}/moderation/draft`, { snapshot });
  }

  async updateDraft(id, snapshot) {
    return this.patch(`/api/documents/${id}/moderation/draft`, { snapshot });
  }

  async getDraft(id) {
    return this.get(`/api/documents/${id}/moderation/draft`);
  }

  async submitDraft(id) {
    // Body optional; send empty object
    return this.post(`/api/documents/${id}/moderation/submit`, {});
  }

  async withdrawDraft(id) {
    return this.post(`/api/documents/${id}/moderation/withdraw`, {});
  }

  async getModerationQueue(page = 1, size = 50) {
    const params = new URLSearchParams({ page: String(page), size: String(size) });
    return this.get(`/api/documents/moderation/queue?${params.toString()}`);
  }

  async getModerationStats() {
    return this.get(`/api/documents/moderation/stats`);
  }

  async approveSubmission(id, transform = null) {
    return this.post(`/api/documents/${id}/moderation/approve`, transform ? { transform } : {});
  }

  async rejectSubmission(id, reason) {
    return this.post(`/api/documents/${id}/moderation/reject`, { reason });
  }

  async returnSubmission(id, reason) {
    return this.post(`/api/documents/${id}/moderation/return`, { reason });
  }

  // Collections API
  async getCollections() {
    return this.get('/api/collections');
  }

  async getCollection(id) {
    return this.get(`/api/collections/${id}`);
  }

  // Users API
  async getUsers() {
    return this.get('/api/users');
  }

  async getUser(id) {
    return this.get(`/api/users/${id}`);
  }

  async updateUserRoles(id, roles) {
    return this.patch(`/api/users/${id}/roles`, { roles });
  }

  // Search API
  async search(query, collection = null) {
    let endpoint = '/api/search';
    const params = new URLSearchParams();
    if (query) params.append('q', query);
    if (collection) params.append('collection', collection);
    if (params.toString()) endpoint += `?${params.toString()}`;
    return this.get(endpoint);
  }

  // AI API
  async getAIAssistance(content, title = '') {
    return this.post('/api/ai/assist', { content, title });
  }

  // Engagement API
  async isBookmarked(documentId) {
    return this.get(`/api/engagement/bookmarks/${documentId}`);
  }

  async addBookmark(documentId) {
    return this.post(`/api/engagement/bookmarks/${documentId}`, {});
  }

  async removeBookmark(documentId) {
    return this.delete(`/api/engagement/bookmarks/${documentId}`);
  }

  async rateDocument(documentId, rating) {
    return this.post(`/api/engagement/ratings/${documentId}`, { rating });
  }

  async reportIssue(documentId, type, description) {
    return this.post(`/api/engagement/issues/${documentId}`, { type, description });
  }
}

// Create global API instance
window.api = new API();
