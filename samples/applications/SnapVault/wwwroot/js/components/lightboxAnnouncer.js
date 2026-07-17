/**
 * Lightbox Announcement Manager
 * Manages screen reader announcements for accessibility
 */

export class AnnouncementManager {
  constructor() {
    this.liveRegion = this.createLiveRegion();
  }

  createLiveRegion() {
    const region = document.createElement('div');
    region.className = 'sr-only';
    region.setAttribute('aria-live', 'polite');
    region.setAttribute('aria-atomic', 'true');
    document.body.appendChild(region);
    return region;
  }

  announce(message, priority = 'polite') {
    if (!message) return;

    // Change priority to assertive for errors
    this.liveRegion.setAttribute('aria-live', priority);

    // Clear and set message (timeout ensures screen readers pick up the change)
    this.liveRegion.textContent = '';
    setTimeout(() => {
      this.liveRegion.textContent = message;
    }, 100);
  }

  announcePhotoChange(index, total, filename) {
    this.announce(`Photo ${index + 1} of ${total}. ${filename}`);
  }

  announceZoomChange(mode) {
    const modeText = {
      'fit': 'Fit to screen',
      'fill': 'Fill screen',
      '100%': '100 percent',
      'custom': `Zoom level ${Math.round(mode * 100)} percent`
    };

    const text = typeof mode === 'string' ? modeText[mode] : modeText['custom'];
    this.announce(`Zoom changed to ${text}`);
  }

  announcePanelState(isOpen) {
    this.announce(isOpen ? 'Photo information panel opened' : 'Photo information panel closed');
  }

  announceAction(action) {
    const messages = {
      'favorite': 'Added to favorites',
      'unfavorite': 'Removed from favorites',
      'download': 'Download started',
      'delete': 'Photo deleted',
      'rating': 'Rating updated'
    };

    if (messages[action]) {
      this.announce(messages[action]);
    }
  }

  announceError(message) {
    this.announce(message, 'assertive');
  }

  destroy() {
    if (this.liveRegion && this.liveRegion.parentNode) {
      this.liveRegion.parentNode.removeChild(this.liveRegion);
    }
  }
}
