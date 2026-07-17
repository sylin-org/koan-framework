/**
 * Split Button Component
 * Hybrid button/dropdown for primary action with style selection
 * Primary action: Regenerate with last-used style
 * Dropdown: Select specific analysis style (portrait, product, landscape, etc.)
 */


import { escapeHtml } from '../utils/html.js';
export class SplitButton {
  constructor(options) {
    this.options = {
      primaryLabel: options.primaryLabel || 'Regenerate',
      primaryIcon: options.primaryIcon || null, // SVG string
      onPrimaryClick: options.onPrimaryClick || (() => {}),
      onStyleSelect: options.onStyleSelect || (() => {}),
      styles: options.styles || [], // Array of AnalysisStyleDefinition
      lastUsedStyle: options.lastUsedStyle || 'smart',
      ariaLabel: options.ariaLabel || 'Regenerate description',
      disabled: options.disabled || false
    };

    this.container = null;
    this.dropdown = null;
    this.isDropdownOpen = false;
    this.createDOM();
    this.setupEventListeners();
  }

  createDOM() {
    const { primaryLabel, primaryIcon, ariaLabel, disabled } = this.options;

    // Create split button container
    const wrapper = document.createElement('div');
    wrapper.className = 'split-button-wrapper';
    wrapper.setAttribute('role', 'group');
    wrapper.setAttribute('aria-label', ariaLabel);

    // Primary action button (left side)
    const primaryBtn = document.createElement('button');
    primaryBtn.className = 'split-button-primary btn-regenerate-ai';
    primaryBtn.setAttribute('aria-label', ariaLabel);
    primaryBtn.disabled = disabled;

    if (primaryIcon) {
      primaryBtn.innerHTML = `
        ${primaryIcon}
        <span class="action-label">${primaryLabel}</span>
      `;
    } else {
      primaryBtn.innerHTML = `<span class="action-label">${primaryLabel}</span>`;
    }

    // Dropdown trigger button (right side)
    const dropdownBtn = document.createElement('button');
    dropdownBtn.className = 'split-button-dropdown';
    dropdownBtn.setAttribute('aria-label', 'Select analysis style');
    dropdownBtn.setAttribute('aria-haspopup', 'menu');
    dropdownBtn.setAttribute('aria-expanded', 'false');
    dropdownBtn.disabled = disabled;
    dropdownBtn.innerHTML = `
      <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
        <polyline points="6 9 12 15 18 9"></polyline>
      </svg>
    `;

    // Dropdown menu
    const dropdown = document.createElement('div');
    dropdown.className = 'split-button-menu';
    dropdown.setAttribute('role', 'menu');
    dropdown.style.display = 'none';

    // Render menu items from styles array
    if (this.options.styles.length > 0) {
      dropdown.innerHTML = this.options.styles.map((style, index) => `
        <button class="split-button-menu-item"
                data-style-id="${escapeHtml(style.id)}"
                role="menuitem"
                tabindex="${index === 0 ? '0' : '-1'}">
          <span class="menu-item-icon">${style.icon || '🔍'}</span>
          <div class="menu-item-content">
            <div class="menu-item-label">${escapeHtml(style.label)}</div>
            <div class="menu-item-desc">${escapeHtml(style.description)}</div>
          </div>
        </button>
      `).join('');
    } else {
      dropdown.innerHTML = '<div class="menu-loading">Loading styles...</div>';
    }

    // Assemble split button
    wrapper.appendChild(primaryBtn);
    wrapper.appendChild(dropdownBtn);
    wrapper.appendChild(dropdown);

    this.container = wrapper;
    this.dropdown = dropdown;
    this.primaryBtn = primaryBtn;
    this.dropdownBtn = dropdownBtn;
  }

  setupEventListeners() {
    // Primary button click
    this.primaryBtn.addEventListener('click', async () => {
      await this.options.onPrimaryClick(this.options.lastUsedStyle);
    });

    // Dropdown trigger click
    this.dropdownBtn.addEventListener('click', (e) => {
      e.stopPropagation();
      this.toggleDropdown();
    });

    // Menu item clicks
    this.dropdown.querySelectorAll('.split-button-menu-item').forEach(item => {
      item.addEventListener('click', async () => {
        const styleId = item.dataset.styleId;
        this.closeDropdown();
        await this.options.onStyleSelect(styleId);
      });
    });

    // Close dropdown when clicking outside
    document.addEventListener('click', (e) => {
      if (this.isDropdownOpen && !this.container.contains(e.target)) {
        this.closeDropdown();
      }
    });

    // Keyboard navigation for menu
    this.dropdown.addEventListener('keydown', (e) => {
      const items = Array.from(this.dropdown.querySelectorAll('.split-button-menu-item'));
      const currentIndex = items.findIndex(item => item === document.activeElement);

      switch (e.key) {
        case 'ArrowDown':
          e.preventDefault();
          const nextIndex = currentIndex < items.length - 1 ? currentIndex + 1 : 0;
          items[nextIndex].focus();
          break;
        case 'ArrowUp':
          e.preventDefault();
          const prevIndex = currentIndex > 0 ? currentIndex - 1 : items.length - 1;
          items[prevIndex].focus();
          break;
        case 'Escape':
          e.preventDefault();
          this.closeDropdown();
          this.dropdownBtn.focus();
          break;
        case 'Enter':
        case ' ':
          e.preventDefault();
          if (document.activeElement.classList.contains('split-button-menu-item')) {
            document.activeElement.click();
          }
          break;
      }
    });
  }

  toggleDropdown() {
    if (this.isDropdownOpen) {
      this.closeDropdown();
    } else {
      this.openDropdown();
    }
  }

  openDropdown() {
    this.isDropdownOpen = true;
    this.dropdown.style.display = 'block';
    this.dropdownBtn.setAttribute('aria-expanded', 'true');

    // Focus first menu item
    const firstItem = this.dropdown.querySelector('.split-button-menu-item');
    if (firstItem) {
      firstItem.focus();
    }
  }

  closeDropdown() {
    this.isDropdownOpen = false;
    this.dropdown.style.display = 'none';
    this.dropdownBtn.setAttribute('aria-expanded', 'false');
  }

  setLoading(loading) {
    this.primaryBtn.disabled = loading;
    this.dropdownBtn.disabled = loading;

    if (loading) {
      this.primaryBtn.classList.add('loading');
      this.primaryBtn.querySelector('.action-label').textContent = 'Regenerating...';
    } else {
      this.primaryBtn.classList.remove('loading');
      this.primaryBtn.querySelector('.action-label').textContent = this.options.primaryLabel;
    }
  }

  updateStyles(styles) {
    this.options.styles = styles;

    // Re-render dropdown menu
    this.dropdown.innerHTML = styles.map((style, index) => `
      <button class="split-button-menu-item"
              data-style-id="${escapeHtml(style.id)}"
              role="menuitem"
              tabindex="${index === 0 ? '0' : '-1'}">
        <span class="menu-item-icon">${style.icon || '🔍'}</span>
        <div class="menu-item-content">
          <div class="menu-item-label">${escapeHtml(style.label)}</div>
          <div class="menu-item-desc">${escapeHtml(style.description)}</div>
        </div>
      </button>
    `).join('');

    // Re-attach event listeners to new menu items
    this.dropdown.querySelectorAll('.split-button-menu-item').forEach(item => {
      item.addEventListener('click', async () => {
        const styleId = item.dataset.styleId;
        this.closeDropdown();
        await this.options.onStyleSelect(styleId);
      });
    });
  }

  updateLastUsedStyle(styleId) {
    this.options.lastUsedStyle = styleId;
  }

  getElement() {
    return this.container;
  }

  escapeHtml(str) {
    if (!str) return '';
    const div = document.createElement('div');
    div.textContent = str;
    return div.innerHTML;
  }

  destroy() {
    // Clean up event listeners
    if (this.container && this.container.parentNode) {
      this.container.parentNode.removeChild(this.container);
    }
  }
}
