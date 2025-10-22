// Settings Page - SnapVault Pro
// Progressive disclosure, safety-first interactions

class SettingsManager {
    constructor() {
        this.apiBase = '/api';
        this.stats = null;
        this.init();
    }

    init() {
        this.setupTabs();
        this.setupDangerZone();
        this.setupMaintenanceActions();
        this.setupExportActions();
        this.loadStorageStats();
    }

    // ═══════════════════════════════════════════════════════════════
    // TAB NAVIGATION
    // ═══════════════════════════════════════════════════════════════

    setupTabs() {
        const tabs = document.querySelectorAll('.tab-btn');
        tabs.forEach(tab => {
            tab.addEventListener('click', () => this.switchTab(tab));
        });

        // Keyboard navigation
        document.addEventListener('keydown', (e) => {
            if (e.key === 'ArrowLeft' || e.key === 'ArrowRight') {
                const currentTab = document.querySelector('.tab-btn.active');
                const allTabs = Array.from(tabs);
                const currentIndex = allTabs.indexOf(currentTab);
                const nextIndex = e.key === 'ArrowLeft'
                    ? Math.max(0, currentIndex - 1)
                    : Math.min(allTabs.length - 1, currentIndex + 1);
                this.switchTab(allTabs[nextIndex]);
            }
        });
    }

    switchTab(tab) {
        // Update tab buttons
        document.querySelectorAll('.tab-btn').forEach(t => {
            t.classList.remove('active');
            t.setAttribute('aria-selected', 'false');
        });
        tab.classList.add('active');
        tab.setAttribute('aria-selected', 'true');

        // Update panels
        const targetPanel = `tab-${tab.dataset.tab}`;
        document.querySelectorAll('.tab-panel').forEach(panel => {
            panel.classList.remove('active');
        });
        document.getElementById(targetPanel).classList.add('active');
    }

    // ═══════════════════════════════════════════════════════════════
    // STORAGE STATS
    // ═══════════════════════════════════════════════════════════════

    async loadStorageStats() {
        try {
            const response = await fetch(`${this.apiBase}/maintenance/stats`);
            if (!response.ok) {
                // Fallback to mock data for demo
                this.stats = {
                    hotTierGB: 2.3,
                    warmTierGB: 8.7,
                    coldTierGB: 45.2,
                    totalGB: 56.2,
                    photoCount: 4382,
                    cacheEntries: 2847,
                    cacheSizeMB: 127
                };
                this.updateStorageUI();
                return;
            }
            this.stats = await response.json();
            this.updateStorageUI();
        } catch (error) {
            console.error('Failed to load storage stats:', error);
            this.showToast('Unable to load storage statistics', 'error');
        }
    }

    updateStorageUI() {
        if (!this.stats) return;

        // Update storage chart
        const total = this.stats.hotTierGB + this.stats.warmTierGB + this.stats.coldTierGB;
        const hotPct = (this.stats.hotTierGB / total * 100).toFixed(1);
        const warmPct = (this.stats.warmTierGB / total * 100).toFixed(1);
        const coldPct = (this.stats.coldTierGB / total * 100).toFixed(1);

        document.querySelector('.chart-segment.hot').style.width = `${hotPct}%`;
        document.querySelector('.chart-segment.warm').style.width = `${warmPct}%`;
        document.querySelector('.chart-segment.cold').style.width = `${coldPct}%`;

        // Update stat values
        document.getElementById('hot-size').textContent = `${this.stats.hotTierGB.toFixed(1)} GB`;
        document.getElementById('warm-size').textContent = `${this.stats.warmTierGB.toFixed(1)} GB`;
        document.getElementById('cold-size').textContent = `${this.stats.coldTierGB.toFixed(1)} GB`;
        document.getElementById('total-size').textContent = `${this.stats.totalGB.toFixed(1)} GB`;

        // Update action metadata
        document.getElementById('photo-count').textContent = `${this.stats.photoCount.toLocaleString()} photos`;
        document.getElementById('cache-status').textContent =
            `${this.stats.cacheEntries.toLocaleString()} cached embeddings (${this.stats.cacheSizeMB} MB)`;

        // Update wipe confirmation text
        document.getElementById('wipe-photo-count').textContent = this.stats.photoCount.toLocaleString();
        document.getElementById('wipe-photo-size').textContent = this.stats.totalGB.toFixed(1);
        document.getElementById('modal-photo-count').textContent = this.stats.photoCount.toLocaleString();
        document.getElementById('modal-photo-size').textContent = this.stats.totalGB.toFixed(1);
    }

    // ═══════════════════════════════════════════════════════════════
    // MAINTENANCE ACTIONS
    // ═══════════════════════════════════════════════════════════════

    setupMaintenanceActions() {
        // Rebuild Index
        document.getElementById('rebuild-index-btn').addEventListener('click', () => {
            this.performMaintenance('rebuild-index', 'Search index rebuilt successfully');
        });

        // Clear Cache
        document.getElementById('clear-cache-btn').addEventListener('click', () => {
            this.performMaintenance('clear-cache', 'AI embedding cache cleared');
        });

        // Optimize Database
        document.getElementById('optimize-db-btn').addEventListener('click', () => {
            this.performMaintenance('optimize-db', 'Database optimized successfully');
        });

        // Load index status
        this.loadIndexStatus();
    }

    async loadIndexStatus() {
        try {
            const response = await fetch(`${this.apiBase}/maintenance/index-status`);
            if (response.ok) {
                const data = await response.json();
                const lastIndexed = new Date(data.lastIndexed);
                const timeAgo = this.getTimeAgo(lastIndexed);
                document.getElementById('index-status').textContent = `Last indexed: ${timeAgo}`;
            }
        } catch (error) {
            document.getElementById('index-status').textContent = 'Status unknown';
        }
    }

    async performMaintenance(action, successMessage) {
        const btn = document.getElementById(`${action}-btn`);
        const originalText = btn.textContent;

        btn.classList.add('loading');
        btn.disabled = true;

        try {
            const response = await fetch(`${this.apiBase}/maintenance/${action}`, {
                method: 'POST'
            });

            if (response.ok) {
                this.showToast(successMessage, 'success');
                // Reload stats after maintenance
                setTimeout(() => this.loadStorageStats(), 1000);
            } else {
                throw new Error('Maintenance action failed');
            }
        } catch (error) {
            this.showToast(`Failed to ${action.replace('-', ' ')}`, 'error');
        } finally {
            btn.classList.remove('loading');
            btn.disabled = false;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // EXPORT ACTIONS
    // ═══════════════════════════════════════════════════════════════

    setupExportActions() {
        document.getElementById('export-metadata-btn').addEventListener('click', () => {
            this.exportMetadata();
        });

        document.getElementById('backup-config-btn').addEventListener('click', () => {
            this.backupConfig();
        });
    }

    async exportMetadata() {
        const btn = document.getElementById('export-metadata-btn');
        btn.classList.add('loading');
        btn.disabled = true;

        try {
            const response = await fetch(`${this.apiBase}/maintenance/export-metadata`);
            if (response.ok) {
                const blob = await response.blob();
                const url = window.URL.createObjectURL(blob);
                const a = document.createElement('a');
                a.href = url;
                a.download = `snapvault-metadata-${Date.now()}.json`;
                document.body.appendChild(a);
                a.click();
                document.body.removeChild(a);
                window.URL.revokeObjectURL(url);
                this.showToast('Metadata exported successfully', 'success');
            } else {
                throw new Error('Export failed');
            }
        } catch (error) {
            this.showToast('Failed to export metadata', 'error');
        } finally {
            btn.classList.remove('loading');
            btn.disabled = false;
        }
    }

    async backupConfig() {
        const btn = document.getElementById('backup-config-btn');
        btn.classList.add('loading');
        btn.disabled = true;

        try {
            const response = await fetch(`${this.apiBase}/maintenance/backup-config`);
            if (response.ok) {
                const blob = await response.blob();
                const url = window.URL.createObjectURL(blob);
                const a = document.createElement('a');
                a.href = url;
                a.download = `snapvault-config-${Date.now()}.json`;
                document.body.appendChild(a);
                a.click();
                document.body.removeChild(a);
                window.URL.revokeObjectURL(url);
                this.showToast('Configuration backed up successfully', 'success');
            } else {
                throw new Error('Backup failed');
            }
        } catch (error) {
            this.showToast('Failed to backup configuration', 'error');
        } finally {
            btn.classList.remove('loading');
            btn.disabled = false;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // DANGER ZONE - WIPE REPOSITORY
    // ═══════════════════════════════════════════════════════════════

    setupDangerZone() {
        const showBtn = document.getElementById('show-wipe-btn');
        const cancelBtn = document.getElementById('wipe-cancel-btn');
        const confirmInput = document.getElementById('wipe-confirm');
        const confirmBtn = document.getElementById('wipe-confirm-btn');
        const wipeContent = document.getElementById('wipe-content');

        // Show/hide wipe options
        showBtn.addEventListener('click', () => {
            wipeContent.classList.toggle('expanded');
            showBtn.textContent = wipeContent.classList.contains('expanded')
                ? 'Hide Wipe Options ▲'
                : 'Show Wipe Options ▼';

            if (wipeContent.classList.contains('expanded')) {
                setTimeout(() => confirmInput.focus(), 300);
            }
        });

        // Cancel
        cancelBtn.addEventListener('click', () => {
            wipeContent.classList.remove('expanded');
            showBtn.textContent = 'Show Wipe Options ▼';
            confirmInput.value = '';
            confirmBtn.disabled = true;
        });

        // Confirmation input validation
        confirmInput.addEventListener('input', (e) => {
            const isValid = e.target.value === 'DELETE ALL DATA';
            confirmBtn.disabled = !isValid;
            confirmInput.classList.toggle('valid', isValid);
        });

        // Show final confirmation modal
        confirmBtn.addEventListener('click', () => {
            this.showWipeModal();
        });

        // Final wipe button
        document.getElementById('final-wipe-btn').addEventListener('click', () => {
            this.performWipe();
        });
    }

    showWipeModal() {
        const modal = document.getElementById('wipe-modal');
        modal.classList.add('active');

        // Trap focus
        const focusableElements = modal.querySelectorAll('button');
        focusableElements[0]?.focus();

        // ESC to close
        const escHandler = (e) => {
            if (e.key === 'Escape') {
                this.closeWipeModal();
                document.removeEventListener('keydown', escHandler);
            }
        };
        document.addEventListener('keydown', escHandler);
    }

    closeWipeModal() {
        const modal = document.getElementById('wipe-modal');
        modal.classList.remove('active');
    }

    async performWipe() {
        this.closeWipeModal();

        // Show progress modal
        const progressModal = document.getElementById('progress-modal');
        progressModal.classList.add('active');

        try {
            const response = await fetch(`${this.apiBase}/maintenance/wipe-repository`, {
                method: 'POST'
            });

            if (!response.ok) {
                throw new Error('Wipe failed');
            }

            // Stream progress updates
            const reader = response.body.getReader();
            const decoder = new TextDecoder();

            while (true) {
                const { done, value } = await reader.read();
                if (done) break;

                const text = decoder.decode(value);
                const lines = text.split('\n').filter(l => l.trim());

                for (const line of lines) {
                    try {
                        const progress = JSON.parse(line);
                        this.updateWipeProgress(progress);
                    } catch (e) {
                        // Ignore non-JSON lines
                    }
                }
            }

            // Success
            setTimeout(() => {
                progressModal.classList.remove('active');
                this.showToast('Repository wiped successfully', 'success');
                setTimeout(() => window.location.href = '/', 2000);
            }, 1000);

        } catch (error) {
            progressModal.classList.remove('active');
            this.showToast('Failed to wipe repository', 'error');
            console.error('Wipe error:', error);
        }
    }

    updateWipeProgress(progress) {
        const bar = document.getElementById('wipe-progress');
        const text = document.getElementById('progress-text');

        bar.style.width = `${progress.percentage}%`;
        text.textContent = progress.message;
    }

    // ═══════════════════════════════════════════════════════════════
    // UTILITIES
    // ═══════════════════════════════════════════════════════════════

    showToast(message, type = 'info') {
        const container = document.querySelector('.toast-container');
        const toast = document.createElement('div');
        toast.className = `toast toast-${type}`;

        const icon = {
            success: '✓',
            error: '✕',
            info: 'ℹ'
        }[type] || 'ℹ';

        toast.innerHTML = `
            <span class="toast-icon">${icon}</span>
            <span class="toast-message">${message}</span>
        `;

        container.appendChild(toast);

        // Slide in
        setTimeout(() => toast.classList.add('show'), 10);

        // Auto dismiss
        setTimeout(() => {
            toast.classList.remove('show');
            setTimeout(() => toast.remove(), 300);
        }, type === 'error' ? 6000 : 4000);

        // Click to dismiss
        toast.addEventListener('click', () => {
            toast.classList.remove('show');
            setTimeout(() => toast.remove(), 300);
        });
    }

    getTimeAgo(date) {
        const seconds = Math.floor((new Date() - date) / 1000);

        const intervals = {
            year: 31536000,
            month: 2592000,
            week: 604800,
            day: 86400,
            hour: 3600,
            minute: 60
        };

        for (const [unit, secondsInUnit] of Object.entries(intervals)) {
            const interval = Math.floor(seconds / secondsInUnit);
            if (interval >= 1) {
                return `${interval} ${unit}${interval === 1 ? '' : 's'} ago`;
            }
        }

        return 'just now';
    }
}

// Initialize
window.closeWipeModal = () => {
    document.getElementById('wipe-modal').classList.remove('active');
};

new SettingsManager();
