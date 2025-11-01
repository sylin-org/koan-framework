/**
 * PolyglotShop - Main Utilities
 * Common DOM helpers and utilities
 */
(function() {
    'use strict';

    /**
     * Show/hide loading overlay
     */
    function setLoading(isLoading) {
        const overlay = document.getElementById('loadingOverlay');
        if (overlay) {
            overlay.style.display = isLoading ? 'flex' : 'none';
        }
    }

    /**
     * Debounce function
     */
    function debounce(func, wait) {
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

    /**
     * Copy text to clipboard
     */
    async function copyToClipboard(text) {
        try {
            await navigator.clipboard.writeText(text);
            return true;
        } catch (error) {
            console.error('Failed to copy:', error);
            return false;
        }
    }

    /**
     * Get language name from code
     */
    function getLanguageName(code, languages) {
        const lang = languages.find(l => l.code === code);
        return lang ? lang.name : code.toUpperCase();
    }

    window.S8Utils = {
        setLoading,
        debounce,
        copyToClipboard,
        getLanguageName
    };
})();
