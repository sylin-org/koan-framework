/**
 * Shared HTML escaping utilities
 * Centralizes escapeHtml / escapeAttr to avoid duplication across components.
 */

export function escapeHtml(str) {
    if (!str) return '';
    return str
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#039;');
}

export function escapeAttr(str) {
    return escapeHtml(String(str ?? ''));
}
