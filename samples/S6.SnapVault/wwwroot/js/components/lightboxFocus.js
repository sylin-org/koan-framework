/**
 * Lightbox Focus Manager
 * Manages focus trap and keyboard navigation for accessibility
 */

export class FocusManager {
  constructor(lightbox) {
    this.lightbox = lightbox;
    this.previousFocus = null;
    this.focusableElements = [];
  }

  captureFocus() {
    // Save currently focused element
    this.previousFocus = document.activeElement;

    // Get all focusable elements in lightbox
    this.updateFocusableElements();

    // Move focus to first element (close button)
    if (this.focusableElements.length > 0) {
      this.focusableElements[0].focus();
    }

    // Set up focus trap
    document.addEventListener('keydown', this.handleFocusTrap);
  }

  restoreFocus() {
    // Restore focus to previous element
    if (this.previousFocus && this.previousFocus.focus) {
      this.previousFocus.focus();
    }

    // Remove focus trap
    document.removeEventListener('keydown', this.handleFocusTrap);
  }

  updateFocusableElements() {
    const container = this.lightbox.container;
    if (!container) return;

    const selector = 'button:not(:disabled), [href], input, select, textarea, [tabindex]:not([tabindex="-1"])';
    this.focusableElements = Array.from(container.querySelectorAll(selector));
  }

  handleFocusTrap = (event) => {
    if (event.key !== 'Tab') return;

    this.updateFocusableElements();

    if (this.focusableElements.length === 0) return;

    const firstElement = this.focusableElements[0];
    const lastElement = this.focusableElements[this.focusableElements.length - 1];

    if (event.shiftKey) {
      // Shift+Tab: Move backwards
      if (document.activeElement === firstElement) {
        event.preventDefault();
        lastElement.focus();
      }
    } else {
      // Tab: Move forwards
      if (document.activeElement === lastElement) {
        event.preventDefault();
        firstElement.focus();
      }
    }
  };

  focusPanel() {
    // Move focus to panel close button when panel opens
    const panelCloseBtn = this.lightbox.container.querySelector('.btn-close-panel');
    if (panelCloseBtn) {
      panelCloseBtn.focus();
    }
  }

  focusInfoToggle() {
    // Return focus to info toggle when panel closes
    const infoToggle = this.lightbox.container.querySelector('.btn-info');
    if (infoToggle) {
      infoToggle.focus();
    }
  }
}
