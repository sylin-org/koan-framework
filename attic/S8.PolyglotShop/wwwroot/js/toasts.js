/**
 * PolyglotShop - Toast Notifications
 * Simple toast notification system
 */
(function() {
    'use strict';

    const { toastDuration } = window.S8Const.ui;

    /**
     * Show a toast notification
     */
    function showToast(message, type = 'info') {
        const container = document.getElementById('toastContainer');
        if (!container) return;

        const toast = document.createElement('div');
        toast.className = `toast ${type}`;

        const icon = getIcon(type);
        toast.innerHTML = `
            <i class="fas ${icon}"></i>
            <span>${message}</span>
        `;

        container.appendChild(toast);

        // Auto-remove after duration
        setTimeout(() => {
            toast.style.opacity = '0';
            toast.style.transform = 'translateX(100%)';
            setTimeout(() => {
                toast.remove();
            }, 300);
        }, toastDuration);
    }

    /**
     * Get icon class for toast type
     */
    function getIcon(type) {
        const icons = {
            success: 'fa-check-circle',
            error: 'fa-exclamation-circle',
            info: 'fa-info-circle',
            warning: 'fa-exclamation-triangle'
        };
        return icons[type] || icons.info;
    }

    /**
     * Convenience methods
     */
    const toasts = {
        success: (message) => showToast(message, 'success'),
        error: (message) => showToast(message, 'error'),
        info: (message) => showToast(message, 'info'),
        warning: (message) => showToast(message, 'warning')
    };

    window.S8Toasts = toasts;
})();
