/**
 * PolyglotShop - API Client
 * Wrapper for fetch API with error handling
 */
(function() {
    'use strict';

    const { endpoints } = window.S8Const;

    /**
     * Generic fetch wrapper with error handling
     */
    async function fetchJson(url, options = {}) {
        try {
            const response = await fetch(url, {
                ...options,
                headers: {
                    'Content-Type': 'application/json',
                    ...(options.headers || {})
                }
            });

            if (!response.ok) {
                const error = await response.json().catch(() => ({
                    error: `HTTP ${response.status}: ${response.statusText}`
                }));
                throw new Error(error.error || error.message || 'Request failed');
            }

            return await response.json();
        } catch (error) {
            console.error('API Error:', error);
            throw error;
        }
    }

    /**
     * GET request
     */
    async function get(url) {
        return fetchJson(url, { method: 'GET' });
    }

    /**
     * POST request
     */
    async function post(url, body) {
        return fetchJson(url, {
            method: 'POST',
            body: JSON.stringify(body)
        });
    }

    /**
     * API Methods
     */
    const api = {
        /**
         * Get supported languages
         */
        async getLanguages() {
            return await get(endpoints.languages);
        },

        /**
         * Translate text
         */
        async translate(text, targetLanguage, sourceLanguage = 'auto') {
            return await post(endpoints.translate, {
                text,
                targetLanguage,
                sourceLanguage
            });
        },

        /**
         * Detect language of text
         */
        async detectLanguage(text) {
            return await post(endpoints.detect, {
                text
            });
        }
    };

    window.S8Api = api;
})();
