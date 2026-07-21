/**
 * PhotoSetCacheIndicator - UI components for cache status and updates
 */

export class PhotoSetCacheIndicator {
    constructor() {
        this.refreshIndicator = null;
        this.changeToast = null;
        this.init();
    }

    init() {
        // Create refresh indicator (top-right corner)
        this.refreshIndicator = document.createElement('div');
        this.refreshIndicator.className = 'photoset-refresh-indicator';
        this.refreshIndicator.style.cssText = `
            position: fixed;
            top: 70px;
            right: 20px;
            background: rgba(0, 0, 0, 0.85);
            color: white;
            padding: 8px 16px;
            border-radius: 20px;
            font-size: 13px;
            display: none;
            align-items: center;
            gap: 8px;
            z-index: 1000;
            backdrop-filter: blur(10px);
            box-shadow: 0 2px 8px rgba(0,0,0,0.2);
            transition: opacity 0.3s ease;
        `;
        this.refreshIndicator.innerHTML = `
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" class="refresh-spinner">
                <path d="M21.5 2v6h-6M2.5 22v-6h6M2 11.5a10 10 0 0 1 18.8-4.3M22 12.5a10 10 0 0 1-18.8 4.2"/>
            </svg>
            <span>Checking for updates...</span>
        `;
        document.body.appendChild(this.refreshIndicator);

        // Add spinner animation
        const style = document.createElement('style');
        style.textContent = `
            @keyframes spin {
                from { transform: rotate(0deg); }
                to { transform: rotate(360deg); }
            }
            .refresh-spinner {
                animation: spin 1s linear infinite;
            }
            .photoset-change-toast {
                position: fixed;
                bottom: 80px;
                left: 50%;
                transform: translateX(-50%);
                background: rgba(0, 0, 0, 0.9);
                color: white;
                padding: 12px 24px;
                border-radius: 24px;
                font-size: 14px;
                display: none;
                align-items: center;
                gap: 12px;
                z-index: 1001;
                backdrop-filter: blur(10px);
                box-shadow: 0 4px 16px rgba(0,0,0,0.3);
                animation: slideUp 0.3s ease;
                cursor: pointer;
            }
            @keyframes slideUp {
                from {
                    opacity: 0;
                    transform: translate(-50%, 20px);
                }
                to {
                    opacity: 1;
                    transform: translate(-50%, 0);
                }
            }
            .photoset-change-toast:hover {
                background: rgba(0, 0, 0, 1);
            }
        `;
        document.head.appendChild(style);

        // Create change toast (bottom center)
        this.changeToast = document.createElement('div');
        this.changeToast.className = 'photoset-change-toast';
        document.body.appendChild(this.changeToast);
    }

    /**
     * Show refresh indicator
     */
    showRefreshing() {
        this.refreshIndicator.style.display = 'flex';
        this.refreshIndicator.style.opacity = '1';
    }

    /**
     * Hide refresh indicator
     */
    hideRefreshing() {
        this.refreshIndicator.style.opacity = '0';
        setTimeout(() => {
            this.refreshIndicator.style.display = 'none';
        }, 300);
    }

    /**
     * Show changes detected notification
     */
    showChanges(changes, onRefresh) {
        let message = '';

        if (changes.added > 0) {
            message = `${changes.added} new photo${changes.added > 1 ? 's' : ''} available`;
        } else if (changes.removed > 0) {
            message = `${changes.removed} photo${changes.removed > 1 ? 's' : ''} removed`;
        } else if (changes.modified > 0) {
            message = `${changes.modified} photo${changes.modified > 1 ? 's' : ''} updated`;
        } else {
            message = 'Photos updated';
        }

        this.changeToast.innerHTML = `
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M21 12a9 9 0 1 1-9-9c2.52 0 4.93 1 6.74 2.74L21 8"/>
                <path d="M21 3v5h-5"/>
            </svg>
            <span>${message}</span>
            <span style="opacity: 0.7; font-size: 12px;">Tap to refresh</span>
        `;

        this.changeToast.style.display = 'flex';

        // Auto-dismiss after 10 seconds
        const timeout = setTimeout(() => {
            this.hideChanges();
        }, 10000);

        // Click to refresh
        this.changeToast.onclick = () => {
            clearTimeout(timeout);
            this.hideChanges();
            if (onRefresh) onRefresh();
        };
    }

    /**
     * Hide changes notification
     */
    hideChanges() {
        this.changeToast.style.opacity = '0';
        setTimeout(() => {
            this.changeToast.style.display = 'none';
            this.changeToast.style.opacity = '1';
            this.changeToast.onclick = null;
        }, 300);
    }

    /**
     * Show cache info (for debugging)
     */
    showCacheInfo(info) {
        const ageMinutes = Math.round(info.age / 1000 / 60);
        const ageText = ageMinutes < 1 ? 'just now' :
                       ageMinutes < 60 ? `${ageMinutes}m ago` :
                       `${Math.round(ageMinutes / 60)}h ago`;

        console.log(`[Cache] Loaded ${info.photoCount} photos from cache (${ageText})`);

        // Optional: Show subtle badge
        if (ageMinutes > 60) {
            // Show cache age if > 1 hour
            const badge = document.createElement('div');
            badge.style.cssText = `
                position: fixed;
                top: 70px;
                right: 20px;
                background: rgba(100, 100, 100, 0.7);
                color: white;
                padding: 4px 12px;
                border-radius: 12px;
                font-size: 11px;
                z-index: 999;
                opacity: 0.6;
            `;
            badge.textContent = `Cached ${ageText}`;
            document.body.appendChild(badge);

            setTimeout(() => {
                badge.style.opacity = '0';
                setTimeout(() => badge.remove(), 300);
            }, 3000);
        }
    }
}

// Export singleton instance
export const cacheIndicator = new PhotoSetCacheIndicator();
