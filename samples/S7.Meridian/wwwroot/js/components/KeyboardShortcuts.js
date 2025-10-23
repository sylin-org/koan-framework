/**
 * KeyboardShortcuts - Comprehensive keyboard navigation system
 *
 * Features:
 * - Command palette (Cmd/Ctrl+K)
 * - Navigation shortcuts
 * - Action shortcuts
 * - Help overlay (?)
 * - Visual shortcut hints
 * - Accessibility-focused
 */
export class KeyboardShortcuts {
  constructor(eventBus, router) {
    this.eventBus = eventBus;
    this.router = router;
    this.isCommandPaletteOpen = false;
    this.isHelpOpen = false;
    this.shortcuts = this.defineShortcuts();
    this.commandPaletteItems = this.defineCommandPaletteItems();
    this.filteredCommands = [...this.commandPaletteItems];
    this.selectedCommandIndex = 0;
  }

  /**
   * Define all keyboard shortcuts
   */
  defineShortcuts() {
    return {
      // Command Palette
      'mod+k': {
        description: 'Open command palette',
        action: () => this.toggleCommandPalette(),
        category: 'General'
      },

      // Help
      '?': {
        description: 'Show keyboard shortcuts',
        action: () => this.toggleHelp(),
        category: 'General'
      },

      // Navigation
      'g h': {
        description: 'Go to home',
        action: () => this.router.navigate(''),
        category: 'Navigation'
      },
      'g a': {
        description: 'Go to analyses',
        action: () => this.router.navigate('analyses'),
        category: 'Navigation'
      },
      'g t': {
        description: 'Go to analysis types',
        action: () => this.eventBus.emit('open-settings', 'analysis-types'),
        category: 'Navigation'
      },
      'g s': {
        description: 'Go to source types',
        action: () => this.eventBus.emit('open-settings', 'source-types'),
        category: 'Navigation'
      },

      // Actions
      'c': {
        description: 'Create new (context-aware)',
        action: () => this.eventBus.emit('create-new'),
        category: 'Actions',
        contextAware: true
      },
      'e': {
        description: 'Edit selected item',
        action: () => this.eventBus.emit('edit-selected'),
        category: 'Actions',
        contextAware: true
      },
      'mod+s': {
        description: 'Save',
        action: (e) => {
          e.preventDefault();
          this.eventBus.emit('save');
        },
        category: 'Actions'
      },
      'mod+enter': {
        description: 'Submit form',
        action: (e) => {
          e.preventDefault();
          this.eventBus.emit('submit-form');
        },
        category: 'Actions'
      },

      // Search
      '/': {
        description: 'Focus search',
        action: (e) => {
          e.preventDefault();
          this.focusSearch();
        },
        category: 'Search'
      },
      'mod+f': {
        description: 'Focus search',
        action: (e) => {
          e.preventDefault();
          this.focusSearch();
        },
        category: 'Search'
      },

      // Settings
      'mod+,': {
        description: 'Open settings',
        action: () => this.eventBus.emit('open-settings'),
        category: 'General'
      },

      // Escape
      'escape': {
        description: 'Close modals/panels',
        action: () => {
          if (this.isCommandPaletteOpen) {
            this.closeCommandPalette();
          } else if (this.isHelpOpen) {
            this.closeHelp();
          } else {
            this.eventBus.emit('escape-pressed');
          }
        },
        category: 'General',
        alwaysActive: true
      }
    };
  }

  /**
   * Define command palette items
   */
  defineCommandPaletteItems() {
    return [
      {
        id: 'home',
        title: 'Go to Home',
        subtitle: 'Dashboard',
        icon: '<path d="m3 9 9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"></path><polyline points="9 22 9 12 15 12 15 22"></polyline>',
        action: () => this.router.navigate(''),
        keywords: ['home', 'dashboard']
      },
      {
        id: 'analyses',
        title: 'Go to Analyses',
        subtitle: 'View all analyses',
        icon: '<path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path><polyline points="14 2 14 8 20 8"></polyline>',
        action: () => this.router.navigate('analyses'),
        keywords: ['analyses', 'pipelines']
      },
      {
        id: 'analysis-types',
        title: 'Manage Analysis Types',
        subtitle: 'Configuration',
        icon: '<rect x="3" y="3" width="18" height="18" rx="2" ry="2"></rect>',
        action: () => this.eventBus.emit('open-settings', 'analysis-types'),
        keywords: ['analysis', 'types', 'config', 'settings']
      },
      {
        id: 'source-types',
        title: 'Manage Source Types',
        subtitle: 'Configuration',
        icon: '<path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path>',
        action: () => this.eventBus.emit('open-settings', 'source-types'),
        keywords: ['source', 'types', 'config', 'settings']
      },
      {
        id: 'create-analysis',
        title: 'Create New Analysis',
        subtitle: 'Start a new analysis pipeline',
        icon: '<line x1="12" y1="5" x2="12" y2="19"></line><line x1="5" y1="12" x2="19" y2="12"></line>',
        action: () => this.eventBus.emit('create-analysis'),
        keywords: ['create', 'new', 'analysis']
      },
      {
        id: 'settings',
        title: 'Open Settings',
        subtitle: 'Application settings',
        icon: '<circle cx="12" cy="12" r="3"></circle><path d="M12 1v6m0 6v6m8.66-13.66l-4.24 4.24m-4.84 4.84l-4.24 4.24M23 12h-6m-6 0H1m20.66 8.66l-4.24-4.24m-4.84-4.84l-4.24-4.24"></path>',
        action: () => this.eventBus.emit('open-settings'),
        keywords: ['settings', 'preferences', 'config']
      }
    ];
  }

  /**
   * Initialize keyboard shortcuts
   */
  init() {
    this.attachKeyboardListeners();
    this.createCommandPalette();
    this.createHelpOverlay();
  }

  /**
   * Attach keyboard event listeners
   */
  attachKeyboardListeners() {
    let keySequence = '';
    let keySequenceTimeout = null;

    document.addEventListener('keydown', (e) => {
      // Skip if user is typing in an input
      if (this.isTypingInInput(e) && !this.isCommandPaletteOpen) {
        return;
      }

      const key = this.getKeyString(e);

      // Handle key sequences (like 'g h')
      if (keySequence && keySequenceTimeout) {
        clearTimeout(keySequenceTimeout);
        keySequence += ' ' + key;

        const shortcut = this.shortcuts[keySequence];
        if (shortcut) {
          e.preventDefault();
          shortcut.action(e);
          keySequence = '';
          return;
        }
      }

      // Check if this is the start of a sequence
      if (key === 'g') {
        keySequence = key;
        keySequenceTimeout = setTimeout(() => {
          keySequence = '';
        }, 1000);
        return;
      }

      // Check for direct shortcuts
      const shortcut = this.shortcuts[key];
      if (shortcut) {
        if (shortcut.alwaysActive || !this.isTypingInInput(e)) {
          shortcut.action(e);
        }
      }
    });
  }

  /**
   * Get key string from event
   */
  getKeyString(e) {
    const parts = [];

    if (e.ctrlKey || e.metaKey) parts.push('mod');
    if (e.shiftKey) parts.push('shift');
    if (e.altKey) parts.push('alt');

    const key = e.key.toLowerCase();
    parts.push(key);

    return parts.join('+');
  }

  /**
   * Check if user is typing in an input field
   */
  isTypingInInput(e) {
    const target = e.target;
    const tagName = target.tagName.toLowerCase();
    return (
      tagName === 'input' ||
      tagName === 'textarea' ||
      tagName === 'select' ||
      target.isContentEditable
    );
  }

  /**
   * Focus search input
   */
  focusSearch() {
    const searchInput = document.querySelector('[data-search-input]');
    if (searchInput) {
      searchInput.focus();
      searchInput.select();
    }
  }

  /**
   * Toggle command palette
   */
  toggleCommandPalette() {
    if (this.isCommandPaletteOpen) {
      this.closeCommandPalette();
    } else {
      this.openCommandPalette();
    }
  }

  /**
   * Open command palette
   */
  openCommandPalette() {
    this.isCommandPaletteOpen = true;
    const palette = document.querySelector('[data-command-palette]');
    if (palette) {
      palette.classList.add('visible');
      const input = palette.querySelector('[data-command-input]');
      if (input) {
        input.focus();
        input.value = '';
        this.filterCommands('');
      }
    }
    document.body.style.overflow = 'hidden';
  }

  /**
   * Close command palette
   */
  closeCommandPalette() {
    this.isCommandPaletteOpen = false;
    const palette = document.querySelector('[data-command-palette]');
    if (palette) {
      palette.classList.remove('visible');
    }
    document.body.style.overflow = '';
  }

  /**
   * Filter commands based on search query
   */
  filterCommands(query) {
    const lowerQuery = query.toLowerCase().trim();

    if (!lowerQuery) {
      this.filteredCommands = [...this.commandPaletteItems];
    } else {
      this.filteredCommands = this.commandPaletteItems.filter(item => {
        return (
          item.title.toLowerCase().includes(lowerQuery) ||
          item.subtitle.toLowerCase().includes(lowerQuery) ||
          item.keywords.some(keyword => keyword.includes(lowerQuery))
        );
      });
    }

    this.selectedCommandIndex = 0;
    this.renderCommandList();
  }

  /**
   * Render command list
   */
  renderCommandList() {
    const list = document.querySelector('[data-command-list]');
    if (!list) return;

    if (this.filteredCommands.length === 0) {
      list.innerHTML = `
        <div class="command-palette-empty">
          <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <circle cx="11" cy="11" r="8"></circle>
            <line x1="21" y1="21" x2="16.65" y2="16.65"></line>
          </svg>
          <p>No commands found</p>
        </div>
      `;
      return;
    }

    list.innerHTML = this.filteredCommands.map((item, index) => `
      <button
        class="command-palette-item ${index === this.selectedCommandIndex ? 'selected' : ''}"
        data-command-item="${item.id}"
        data-index="${index}"
      >
        <div class="command-palette-item-icon">
          <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            ${item.icon}
          </svg>
        </div>
        <div class="command-palette-item-content">
          <div class="command-palette-item-title">${this.escapeHtml(item.title)}</div>
          <div class="command-palette-item-subtitle">${this.escapeHtml(item.subtitle)}</div>
        </div>
      </button>
    `).join('');

    // Attach click handlers
    list.querySelectorAll('[data-command-item]').forEach((btn, index) => {
      btn.addEventListener('click', () => {
        this.executeCommand(index);
      });
    });
  }

  /**
   * Execute selected command
   */
  executeCommand(index) {
    const command = this.filteredCommands[index];
    if (command) {
      command.action();
      this.closeCommandPalette();
    }
  }

  /**
   * Create command palette DOM
   */
  createCommandPalette() {
    const palette = document.createElement('div');
    palette.className = 'command-palette';
    palette.setAttribute('data-command-palette', '');
    palette.innerHTML = `
      <div class="command-palette-backdrop" data-palette-backdrop></div>
      <div class="command-palette-container">
        <div class="command-palette-search">
          <svg class="command-palette-search-icon" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <circle cx="11" cy="11" r="8"></circle>
            <line x1="21" y1="21" x2="16.65" y2="16.65"></line>
          </svg>
          <input
            type="text"
            class="command-palette-input"
            placeholder="Type a command or search..."
            data-command-input
            aria-label="Command palette search"
          />
          <kbd class="command-palette-hint">ESC</kbd>
        </div>
        <div class="command-palette-list" data-command-list></div>
      </div>
    `;

    document.body.appendChild(palette);

    // Attach event handlers
    const backdrop = palette.querySelector('[data-palette-backdrop]');
    backdrop.addEventListener('click', () => this.closeCommandPalette());

    const input = palette.querySelector('[data-command-input]');
    input.addEventListener('input', (e) => this.filterCommands(e.target.value));
    input.addEventListener('keydown', (e) => this.handleCommandPaletteKeydown(e));

    this.renderCommandList();
  }

  /**
   * Handle command palette keyboard navigation
   */
  handleCommandPaletteKeydown(e) {
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      this.selectedCommandIndex = Math.min(
        this.selectedCommandIndex + 1,
        this.filteredCommands.length - 1
      );
      this.renderCommandList();
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      this.selectedCommandIndex = Math.max(this.selectedCommandIndex - 1, 0);
      this.renderCommandList();
    } else if (e.key === 'Enter') {
      e.preventDefault();
      this.executeCommand(this.selectedCommandIndex);
    }
  }

  /**
   * Toggle help overlay
   */
  toggleHelp() {
    if (this.isHelpOpen) {
      this.closeHelp();
    } else {
      this.openHelp();
    }
  }

  /**
   * Open help overlay
   */
  openHelp() {
    this.isHelpOpen = true;
    const help = document.querySelector('[data-keyboard-help]');
    if (help) {
      help.classList.add('visible');
    }
    document.body.style.overflow = 'hidden';
  }

  /**
   * Close help overlay
   */
  closeHelp() {
    this.isHelpOpen = false;
    const help = document.querySelector('[data-keyboard-help]');
    if (help) {
      help.classList.remove('visible');
    }
    document.body.style.overflow = '';
  }

  /**
   * Create help overlay DOM
   */
  createHelpOverlay() {
    const help = document.createElement('div');
    help.className = 'keyboard-help';
    help.setAttribute('data-keyboard-help', '');

    // Group shortcuts by category
    const categories = {};
    Object.entries(this.shortcuts).forEach(([key, shortcut]) => {
      if (!categories[shortcut.category]) {
        categories[shortcut.category] = [];
      }
      categories[shortcut.category].push({ key, ...shortcut });
    });

    help.innerHTML = `
      <div class="keyboard-help-backdrop" data-help-backdrop></div>
      <div class="keyboard-help-container">
        <div class="keyboard-help-header">
          <h2 class="keyboard-help-title">Keyboard Shortcuts</h2>
          <button class="keyboard-help-close" data-help-close aria-label="Close">
            <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="18" y1="6" x2="6" y2="18"></line>
              <line x1="6" y1="6" x2="18" y2="18"></line>
            </svg>
          </button>
        </div>
        <div class="keyboard-help-content">
          ${Object.entries(categories).map(([category, shortcuts]) => `
            <div class="keyboard-help-section">
              <h3 class="keyboard-help-section-title">${category}</h3>
              <div class="keyboard-help-shortcuts">
                ${shortcuts.map(shortcut => `
                  <div class="keyboard-help-shortcut">
                    <div class="keyboard-help-shortcut-keys">
                      ${this.renderShortcutKeys(shortcut.key)}
                    </div>
                    <div class="keyboard-help-shortcut-description">
                      ${this.escapeHtml(shortcut.description)}
                    </div>
                  </div>
                `).join('')}
              </div>
            </div>
          `).join('')}
        </div>
      </div>
    `;

    document.body.appendChild(help);

    // Attach event handlers
    const backdrop = help.querySelector('[data-help-backdrop]');
    backdrop.addEventListener('click', () => this.closeHelp());

    const closeBtn = help.querySelector('[data-help-close]');
    closeBtn.addEventListener('click', () => this.closeHelp());
  }

  /**
   * Render shortcut keys as kbd elements
   */
  renderShortcutKeys(keyString) {
    const parts = keyString.split(' ');
    return parts.map(part => {
      const keys = part.split('+');
      return keys.map(key => {
        const displayKey = key === 'mod' ? (navigator.platform.includes('Mac') ? '⌘' : 'Ctrl') : key;
        return `<kbd>${displayKey}</kbd>`;
      }).join('<span class="keyboard-help-plus">+</span>');
    }).join('<span class="keyboard-help-then">then</span>');
  }

  /**
   * Escape HTML
   */
  escapeHtml(text) {
    if (!text) return '';
    const div = document.createElement('div');
    div.textContent = String(text);
    return div.innerHTML;
  }
}
