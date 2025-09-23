// Koan-Focused API Client - Framework capabilities demonstration
window.DevPortalApi = {
    // Base configuration
    baseUrl: '',

    // Utility function for making HTTP requests
    async request(url, options = {}) {
        const defaultOptions = {
            headers: {
                'Content-Type': 'application/json',
                ...options.headers
            }
        };

        const response = await fetch(url, { ...defaultOptions, ...options });

        if (!response.ok) {
            const error = await response.text();
            throw new Error(`HTTP ${response.status}: ${error}`);
        }

        // Handle empty responses
        const text = await response.text();
        return text ? JSON.parse(text) : {};
    },

    // Multi-provider transparency demo
    async switchProvider(provider) {
        return await this.request(`/api/demo/switch-provider/${provider}`, {
            method: 'POST'
        });
    },

    // Capability detection demo
    async getProviderCapabilities() {
        return await this.request('/api/demo/capabilities');
    },

    // Performance comparison
    async getPerformanceComparison() {
        return await this.request('/api/demo/performance-comparison');
    },

    // Bulk operations demo
    async bulkDemo(count = 100) {
        return await this.request(`/api/demo/bulk-demo?count=${count}`, {
            method: 'POST'
        });
    },

    // Demo data management
    async seedDemoData() {
        return await this.request('/api/demo/seed-demo-data', {
            method: 'POST'
        });
    },

    async clearDemoData() {
        return await this.request('/api/demo/clear-demo-data', {
            method: 'DELETE'
        });
    },

    // Relationship navigation demo
    async getRelationshipDemo() {
        return await this.request('/api/demo/relationship-demo');
    },

    // Article operations (EntityController<Article> demo)
    async getArticles(set = null) {
        const url = set ? `/api/articles?set=${set}` : '/api/articles';
        return await this.request(url);
    },

    async getArticle(id) {
        return await this.request(`/api/articles/${id}`);
    },

    async createArticle(article) {
        return await this.request('/api/articles', {
            method: 'POST',
            body: JSON.stringify(article)
        });
    },

    async updateArticle(article) {
        return await this.request('/api/articles', {
            method: 'POST',
            body: JSON.stringify(article)
        });
    },

    async deleteArticle(id) {
        return await this.request(`/api/articles/${id}`, {
            method: 'DELETE'
        });
    },

    // Bulk article operations
    async bulkImportArticles(articles) {
        return await this.request('/api/articles/bulk-import', {
            method: 'POST',
            body: JSON.stringify(articles)
        });
    },

    async bulkDeleteArticles(ids) {
        return await this.request('/api/articles/bulk-delete', {
            method: 'DELETE',
            body: JSON.stringify(ids)
        });
    },

    // Technology operations (EntityController<Technology> demo)
    async getTechnologies() {
        return await this.request('/api/technologies');
    },

    async getTechnology(id) {
        return await this.request(`/api/technologies/${id}`);
    },

    async getTechnologyChildren(id) {
        return await this.request(`/api/technologies/${id}/children`);
    },

    async getTechnologyHierarchy(id) {
        return await this.request(`/api/technologies/${id}/hierarchy`);
    },

    async getTechnologyRelated(id) {
        return await this.request(`/api/technologies/${id}/related`);
    },

    async createTechnology(technology) {
        return await this.request('/api/technologies', {
            method: 'POST',
            body: JSON.stringify(technology)
        });
    },

    // User operations (EntityController<User> demo)
    async getUsers() {
        return await this.request('/api/users');
    },

    async getUser(id) {
        return await this.request(`/api/users/${id}`);
    },

    async createUser(user) {
        return await this.request('/api/users', {
            method: 'POST',
            body: JSON.stringify(user)
        });
    },

    // Comment operations (EntityController<Comment> demo)
    async getComments() {
        return await this.request('/api/comments');
    },

    async getCommentThread(articleId) {
        return await this.request(`/api/comments/thread/${articleId}`);
    },

    async getCommentReplies(commentId) {
        return await this.request(`/api/comments/${commentId}/replies`);
    },

    async createComment(comment) {
        return await this.request('/api/comments', {
            method: 'POST',
            body: JSON.stringify(comment)
        });
    },

    // Utility functions for demo generation
    generateSampleArticles(count) {
        const articles = [];
        const sampleTitles = [
            'Introduction to Koan Framework',
            'Entity-First Development Patterns',
            'Multi-Provider Architecture',
            'Zero-Boilerplate Controllers',
            'Bulk Operations at Scale',
            'Relationship Navigation',
            'Set Routing Strategies',
            'Performance Optimization',
            'Container Development',
            'Framework Auto-Registration'
        ];

        for (let i = 0; i < count; i++) {
            articles.push({
                title: `${sampleTitles[i % sampleTitles.Length]} #${i + 1}`,
                content: `Sample content for article ${i + 1}. This demonstrates the Koan Framework's Entity<T> pattern with auto GUID v7 generation.`,
                type: Math.random() > 0.5 ? 'Article' : 'Tutorial',
                isPublished: Math.random() > 0.25, // 75% published
                createdAt: new Date(Date.now() - Math.random() * 365 * 24 * 60 * 60 * 1000).toISOString()
            });
        }

        return articles;
    }
};