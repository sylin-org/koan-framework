/**
 * Keyboard Shortcuts Component
 * Global keyboard navigation and shortcuts
 */

export class KeyboardShortcuts {
  constructor(app) {
    this.app = app;
    this.setupListeners();
  }

  setupListeners() {
    document.addEventListener('keydown', (e) => {
      // Don't handle shortcuts when typing in input fields or contentEditable elements
      if (e.target.matches('input, textarea, select') ||
          e.target.isContentEditable) {
        // Allow Escape to blur inputs and contentEditable
        if (e.key === 'Escape') {
          e.target.blur();
        }
        return;
      }

      this.handleShortcut(e);
    });
  }

  handleShortcut(e) {
    // Navigation
    if (e.key === '/') {
      e.preventDefault();
      this.app.components.search.focus();
      return;
    }

    if (e.key === 'Escape') {
      // Close any modals
      if (this.app.components.lightbox.isOpen) {
        this.app.components.lightbox.close();
      }
      if (this.app.components.upload.isOpen) {
        this.app.components.upload.close();
      }
      return;
    }

    // Upload
    if (e.key === 'u' || e.key === 'U') {
      e.preventDefault();
      this.app.components.upload.open();
      return;
    }

    // View preset controls
    if (e.key === '1') {
      e.preventDefault();
      this.app.setViewPreset('gallery');
      return;
    }
    if (e.key === '2') {
      e.preventDefault();
      this.app.setViewPreset('comfortable');
      return;
    }
    if (e.key === '3') {
      e.preventDefault();
      this.app.setViewPreset('cozy');
      return;
    }
    if (e.key === '4') {
      e.preventDefault();
      this.app.setViewPreset('compact');
      return;
    }

    // Workspace and Library navigation
    if (e.key === 'g') {
      // Wait for second key
      document.addEventListener('keydown', (e2) => {
        if (e2.key === 'e') {
          this.app.switchWorkspace('gallery');
        } else if (e2.key === 't') {
          this.app.switchWorkspace('timeline');
        } else if (e2.key === 'a') {
          // Go to All Photos
          if (this.app.components.collectionView) {
            this.app.components.collectionView.setView('all-photos');
          }
        } else if (e2.key === 'f') {
          // Go to Favorites
          if (this.app.components.collectionView) {
            this.app.components.collectionView.setView('favorites');
          }
        }
      }, { once: true });
      return;
    }

    // Help
    if (e.key === '?') {
      this.showShortcutsHelp();
      return;
    }
  }

  showShortcutsHelp() {
    const shortcuts = `
      <div class="shortcuts-help">
        <h3>Keyboard Shortcuts</h3>
        <div class="shortcuts-grid">
          <div class="shortcut-group">
            <h4>Navigation</h4>
            <dl>
              <dt><kbd>/</kbd></dt><dd>Focus search</dd>
              <dt><kbd>G</kbd> <kbd>E</kbd></dt><dd>Go to Gallery</dd>
              <dt><kbd>G</kbd> <kbd>T</kbd></dt><dd>Go to Timeline</dd>
              <dt><kbd>G</kbd> <kbd>A</kbd></dt><dd>Go to All Photos</dd>
              <dt><kbd>G</kbd> <kbd>F</kbd></dt><dd>Go to Favorites</dd>
              <dt><kbd>Esc</kbd></dt><dd>Close/Cancel</dd>
            </dl>
          </div>
          <div class="shortcut-group">
            <h4>Actions</h4>
            <dl>
              <dt><kbd>U</kbd></dt><dd>Upload photos</dd>
              <dt><kbd>F</kbd></dt><dd>Toggle favorite</dd>
              <dt><kbd>1-5</kbd></dt><dd>Rate photo</dd>
            </dl>
          </div>
          <div class="shortcut-group">
            <h4>View Presets</h4>
            <dl>
              <dt><kbd>1</kbd></dt><dd>Gallery view</dd>
              <dt><kbd>2</kbd></dt><dd>Comfortable view</dd>
              <dt><kbd>3</kbd></dt><dd>Cozy view</dd>
              <dt><kbd>4</kbd></dt><dd>Compact view</dd>
            </dl>
          </div>
        </div>
        <button class="btn-primary btn-close-help">Close</button>
      </div>
    `;

    this.app.components.toast.show(shortcuts, {
      duration: 0, // Don't auto-close
      allowHtml: true
    });
  }
}
