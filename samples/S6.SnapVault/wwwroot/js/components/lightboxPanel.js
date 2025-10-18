/**
 * Lightbox Unified Info Panel
 * Consolidates metadata, AI insights, actions, and keyboard shortcuts
 */

export class LightboxPanel {
  constructor(lightbox, app) {
    this.lightbox = lightbox;
    this.app = app;
    this.isOpen = false;
    this.currentPhotoData = null;
    this.container = null;
    this.createDOM();
    this.setupMobileGestures();
  }

  createDOM() {
    const panel = document.createElement('div');
    panel.className = 'info-panel';
    panel.id = 'info-panel';

    // ARIA: Complementary role (Phase 6)
    panel.setAttribute('role', 'complementary');
    panel.setAttribute('aria-labelledby', 'panel-title');

    panel.innerHTML = `
      <!-- Mobile drag handle -->
      <div class="drag-handle" aria-hidden="true"></div>

      <!-- Panel Header -->
      <div class="panel-header">
        <h2>Photo Information</h2>
        <button class="btn-close-panel" aria-label="Close panel" title="Close panel (I)">
          <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <line x1="18" y1="6" x2="6" y2="18"></line>
            <line x1="6" y1="6" x2="18" y2="18"></line>
          </svg>
        </button>
      </div>

      <!-- Panel Content (Scrollable) -->
      <div class="panel-content">
        <!-- Metadata Section -->
        <section class="panel-section" id="metadata-section">
          <h3>Details</h3>
          <div class="metadata-grid">
            <div class="metadata-item">
              <span class="label">Camera</span>
              <span class="value" id="meta-camera">—</span>
            </div>
            <div class="metadata-item">
              <span class="label">Lens</span>
              <span class="value" id="meta-lens">—</span>
            </div>
            <div class="metadata-item">
              <span class="label">Settings</span>
              <span class="value" id="meta-settings">—</span>
            </div>
            <div class="metadata-item">
              <span class="label">Captured</span>
              <span class="value" id="meta-date">—</span>
            </div>
            <div class="metadata-item">
              <span class="label">Dimensions</span>
              <span class="value" id="meta-dimensions">—</span>
            </div>
          </div>
        </section>

        <!-- AI Insights Section -->
        <section class="panel-section" id="ai-section">
          <h3>AI Insights</h3>
          <div class="ai-content" data-state="empty">
            <p class="ai-description" id="ai-description"></p>
            <div class="ai-tags" id="ai-tags"></div>
            <button class="btn-regenerate-ai" id="btn-regenerate" style="display: none;">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <polyline points="23 4 23 10 17 10"></polyline>
                <polyline points="1 20 1 14 7 14"></polyline>
                <path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15"></path>
              </svg>
              Regenerate Description
            </button>
          </div>
        </section>

        <!-- Actions Section -->
        <section class="panel-section" id="actions-section">
          <h3>Actions</h3>
          <div class="actions-grid">
            <button class="btn-action" id="btn-favorite">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M20.84 4.61a5.5 5.5 0 0 0-7.78 0L12 5.67l-1.06-1.06a5.5 5.5 0 0 0-7.78 7.78l1.06 1.06L12 21.23l7.78-7.78 1.06-1.06a5.5 5.5 0 0 0 0-7.78z"></path>
              </svg>
              <span class="action-label">Favorite</span>
            </button>
            <button class="btn-action" id="btn-download">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
                <polyline points="7 10 12 15 17 10"></polyline>
                <line x1="12" y1="15" x2="12" y2="3"></line>
              </svg>
              <span class="action-label">Download</span>
            </button>
            <button class="btn-action btn-destructive" id="btn-delete">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <polyline points="3 6 5 6 21 6"></polyline>
                <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path>
              </svg>
              <span class="action-label">Delete</span>
            </button>
          </div>

          <!-- Star Rating -->
          <div class="rating-section">
            <label class="rating-label">Rating</label>
            <div class="rating-stars">
              ${[1, 2, 3, 4, 5].map(star => `
                <button class="star-btn" data-rating="${star}" aria-label="Rate ${star} stars" title="Rate ${star} stars">
                  <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"></polygon>
                  </svg>
                </button>
              `).join('')}
            </div>
          </div>
        </section>

        <!-- Keyboard Shortcuts Section (Collapsible) -->
        <section class="panel-section" id="shortcuts-section">
          <details>
            <summary>
              <h3>Keyboard Shortcuts</h3>
              <svg class="chevron" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <polyline points="6 9 12 15 18 9"></polyline>
              </svg>
            </summary>
            <div class="shortcuts-grid">
              <div class="shortcut-item">
                <kbd>I</kbd>
                <span>Toggle this panel</span>
              </div>
              <div class="shortcut-item">
                <kbd>ESC</kbd>
                <span>Close lightbox</span>
              </div>
              <div class="shortcut-item">
                <kbd>←</kbd> <kbd>→</kbd>
                <span>Navigate photos</span>
              </div>
              <div class="shortcut-item">
                <kbd>S</kbd>
                <span>Toggle favorite</span>
              </div>
              <div class="shortcut-item">
                <kbd>D</kbd>
                <span>Download</span>
              </div>
              <div class="shortcut-item">
                <kbd>F</kbd>
                <span>Toggle Fit/Fill</span>
              </div>
              <div class="shortcut-item">
                <kbd>1-5</kbd>
                <span>Rate photo</span>
              </div>
              <div class="shortcut-item">
                <kbd>+</kbd> <kbd>-</kbd>
                <span>Zoom in/out</span>
              </div>
              <div class="shortcut-item">
                <kbd>0</kbd>
                <span>Reset zoom</span>
              </div>
            </div>
          </details>
        </section>
      </div>
    `;

    // Append to lightbox overlay
    this.container = panel;

    // Setup event listeners
    this.setupEventListeners();
  }

  setupEventListeners() {
    const closeBtn = this.container.querySelector('.btn-close-panel');
    closeBtn.addEventListener('click', () => this.close());

    // Action buttons (Phase 4: Connected to LightboxActions)
    const favoriteBtn = this.container.querySelector('#btn-favorite');
    const downloadBtn = this.container.querySelector('#btn-download');
    const deleteBtn = this.container.querySelector('#btn-delete');
    const regenerateBtn = this.container.querySelector('#btn-regenerate');
    const starBtns = this.container.querySelectorAll('.star-btn');

    favoriteBtn.addEventListener('click', () => {
      if (this.lightbox.actions) {
        this.lightbox.actions.toggleFavorite();
      }
    });

    downloadBtn.addEventListener('click', () => {
      if (this.lightbox.actions) {
        this.lightbox.actions.download();
      }
    });

    deleteBtn.addEventListener('click', () => {
      if (this.lightbox.actions) {
        this.lightbox.actions.deletePhoto();
      }
    });

    regenerateBtn.addEventListener('click', () => {
      if (this.lightbox.actions) {
        this.lightbox.actions.regenerateAI();
      }
    });

    starBtns.forEach(btn => {
      btn.addEventListener('click', async () => {
        const rating = parseInt(btn.dataset.rating);
        if (this.lightbox.actions) {
          await this.lightbox.actions.setRating(rating);
        }
      });
    });
  }

  render(photoData) {
    this.currentPhotoData = photoData;
    this.renderMetadata(photoData);
    this.renderAIInsights(photoData);
    this.updateActionStates(photoData);
  }

  renderMetadata(photo) {
    // Camera - hide if unknown
    const cameraItem = this.container.querySelector('#meta-camera').closest('.metadata-item');
    if (photo.cameraModel) {
      this.container.querySelector('#meta-camera').textContent = photo.cameraModel;
      cameraItem.style.display = '';
    } else {
      cameraItem.style.display = 'none';
    }

    // Lens - hide if unknown
    const lensItem = this.container.querySelector('#meta-lens').closest('.metadata-item');
    if (photo.lensModel) {
      this.container.querySelector('#meta-lens').textContent = photo.lensModel;
      lensItem.style.display = '';
    } else {
      lensItem.style.display = 'none';
    }

    // Settings - hide if no data
    const settingsItem = this.container.querySelector('#meta-settings').closest('.metadata-item');
    const settings = [];
    if (photo.aperture) settings.push(photo.aperture);
    if (photo.shutterSpeed) settings.push(photo.shutterSpeed);
    if (photo.iso) settings.push(`ISO ${photo.iso}`);
    if (settings.length > 0) {
      this.container.querySelector('#meta-settings').textContent = settings.join(' • ');
      settingsItem.style.display = '';
    } else {
      settingsItem.style.display = 'none';
    }

    // Date - always show
    const dateText = photo.capturedAt
      ? new Date(photo.capturedAt).toLocaleString('en-US', {
          year: 'numeric', month: 'short', day: 'numeric',
          hour: '2-digit', minute: '2-digit'
        })
      : new Date(photo.uploadedAt).toLocaleString('en-US', {
          year: 'numeric', month: 'short', day: 'numeric'
        });
    this.container.querySelector('#meta-date').textContent = dateText;

    // Dimensions - always show
    const dimensions = `${photo.width} × ${photo.height}`;
    this.container.querySelector('#meta-dimensions').textContent = dimensions;
  }

  renderAIInsights(photo) {
    const aiContent = this.container.querySelector('.ai-content');
    const regenerateBtn = this.container.querySelector('#btn-regenerate');

    // Use structured AI analysis if available (new format)
    const analysis = photo.aiAnalysis;

    // Render structured format if available
    if (analysis && analysis.tags && analysis.summary) {
      aiContent.setAttribute('data-state', 'loaded');
      aiContent.innerHTML = `
        <!-- Tags (clickable chips) -->
        <div class="ai-tags" role="list" aria-label="Photo tags">
          ${analysis.tags.map(tag => `
            <button
              class="ai-tag"
              data-tag="${this.escapeHtml(tag)}"
              role="listitem"
              aria-label="Tag: ${this.escapeHtml(tag)}"
              title="Click to filter by ${this.escapeHtml(tag)}">
              ${this.escapeHtml(tag)}
            </button>
          `).join('')}
        </div>

        <!-- Summary (highlighted) -->
        <div class="ai-summary" role="region" aria-label="Photo description">
          <p>${this.escapeHtml(analysis.summary)}</p>
        </div>

        <!-- Facts table with pill values -->
        ${analysis.facts && Object.keys(analysis.facts).length > 0 ? `
          <div class="ai-facts" role="table" aria-label="Photo details">
            ${Object.entries(analysis.facts).map(([key, value]) => {
              const isLocked = analysis.lockedFactKeys && analysis.lockedFactKeys.includes(key);
              return this.renderFactRow(key, value, isLocked);
            }).join('')}
          </div>
        ` : ''}

        <!-- Regenerate button -->
        <button class="btn-regenerate-ai" id="btn-regenerate-new" aria-label="Regenerate AI analysis">
          <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <polyline points="23 4 23 10 17 10"></polyline>
            <polyline points="1 20 1 14 7 14"></polyline>
            <path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15"></path>
          </svg>
          <span class="action-label">Regenerate Description</span>
        </button>
      `;

      // Add tag click handlers for filtering (future feature)
      aiContent.querySelectorAll('.ai-tag').forEach(tagBtn => {
        tagBtn.addEventListener('click', () => {
          const tag = tagBtn.dataset.tag;
          // TODO: Implement tag-based filtering in main gallery
          this.app.components.toast.show(`Filter by "${tag}" - Coming soon!`, { icon: '🔍' });
        });
      });

      // Add lock button click handlers
      aiContent.querySelectorAll('.lock-btn').forEach(btn => {
        btn.addEventListener('click', async (e) => {
          e.stopPropagation();
          // Use getAttribute to preserve exact case (e.g., "Subject Count" not "subject count")
          const factKey = btn.getAttribute('data-fact-key');
          await this.toggleFactLock(factKey, btn);
        });
      });

      // Regenerate button handler
      const newRegenerateBtn = aiContent.querySelector('#btn-regenerate-new');
      newRegenerateBtn.addEventListener('click', async () => {
        await this.regenerateAIAnalysis();
      });

      return;
    }

    // Fallback: Legacy markdown format or empty
    if (!photo.detailedDescription || !photo.detailedDescription.trim()) {
      aiContent.setAttribute('data-state', 'empty');
      aiContent.innerHTML = `
        <p class="ai-empty">No AI analysis available yet.</p>
        <button class="btn-regenerate-ai" id="btn-regenerate-fallback" aria-label="Generate AI analysis">
          <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <polyline points="23 4 23 10 17 10"></polyline>
            <polyline points="1 20 1 14 7 14"></polyline>
            <path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15"></path>
          </svg>
          <span class="action-label">Generate Description</span>
        </button>
      `;

      const fallbackBtn = aiContent.querySelector('#btn-regenerate-fallback');
      fallbackBtn.addEventListener('click', () => {
        if (this.lightbox.actions) {
          this.lightbox.actions.regenerateAI();
        }
      });

      return;
    }

    // Render legacy markdown format
    aiContent.setAttribute('data-state', 'loaded');
    aiContent.innerHTML = `
      <div class="ai-description">${this.formatMarkdown(photo.detailedDescription)}</div>
      ${(photo.autoTags && photo.autoTags.length > 0) ? `
        <div class="ai-tags">
          ${photo.autoTags.slice(0, 10).map(tag =>
            `<span class="ai-tag">${this.escapeHtml(tag)}</span>`
          ).join('')}
        </div>
      ` : ''}
      <button class="btn-regenerate-ai" id="btn-regenerate-legacy" aria-label="Regenerate AI analysis">
        <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <polyline points="23 4 23 10 17 10"></polyline>
          <polyline points="1 20 1 14 7 14"></polyline>
          <path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15"></path>
        </svg>
        <span class="action-label">Regenerate Description</span>
      </button>
    `;

    const legacyBtn = aiContent.querySelector('#btn-regenerate-legacy');
    legacyBtn.addEventListener('click', () => {
      if (this.lightbox.actions) {
        this.lightbox.actions.regenerateAI();
      }
    });
  }

  escapeHtml(str) {
    if (!str) return '';
    const div = document.createElement('div');
    div.textContent = str;
    return div.innerHTML;
  }

  renderFactRow(label, value, isLocked = false) {
    // Split comma-separated values into individual pills
    const values = value.split(',').map(v => v.trim()).filter(v => v.length > 0);

    return `
      <div class="fact-row"
           data-fact-key="${this.escapeHtml(label)}"
           data-locked="${isLocked}"
           role="row">
        <span class="fact-label" role="rowheader">
          ${this.escapeHtml(label)}
        </span>
        <div class="fact-values" role="cell">
          ${values.map(v => `
            <span class="fact-pill" data-fact-type="${this.escapeHtml(label)}" data-fact-value="${this.escapeHtml(v)}">
              ${this.escapeHtml(v)}
            </span>
          `).join('')}
        </div>
        <button class="lock-btn ${isLocked ? 'locked' : ''}"
                data-fact-key="${this.escapeHtml(label)}"
                aria-label="${isLocked ? 'Unlock' : 'Lock'} ${this.escapeHtml(label)}"
                title="Click to ${isLocked ? 'unlock' : 'lock'} this fact">
          <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            ${isLocked
              ? '<rect x="3" y="11" width="18" height="11" rx="2" ry="2"></rect><path d="M7 11V7a5 5 0 0 1 10 0v4"></path>'
              : '<rect x="3" y="11" width="18" height="11" rx="2" ry="2"></rect><path d="M7 11V7a5 5 0 0 1 9.9-1"></path>'}
          </svg>
        </button>
      </div>
    `;
  }

  updateActionStates(photo) {
    // Update favorite button
    const favoriteBtn = this.container.querySelector('#btn-favorite');
    const favoriteSvg = favoriteBtn.querySelector('svg');
    if (photo.isFavorite) {
      favoriteBtn.classList.add('active');
      favoriteSvg.setAttribute('fill', 'currentColor');
    } else {
      favoriteBtn.classList.remove('active');
      favoriteSvg.setAttribute('fill', 'none');
    }

    // Update rating stars
    this.updateRatingStars(photo.rating || 0);
  }

  updateRatingStars(rating) {
    const starBtns = this.container.querySelectorAll('.star-btn');
    starBtns.forEach((btn, index) => {
      const starRating = index + 1;
      const svg = btn.querySelector('svg');
      if (starRating <= rating) {
        btn.classList.add('active');
        svg.setAttribute('fill', 'currentColor');
      } else {
        btn.classList.remove('active');
        svg.setAttribute('fill', 'none');
      }
    });
  }

  formatMarkdown(text) {
    // Simple markdown-to-HTML converter
    let html = text;

    // Headers (## Header)
    html = html.replace(/^## (.+)$/gm, '<h4>$1</h4>');
    html = html.replace(/^# (.+)$/gm, '<h3>$1</h3>');

    // Bold (**text**)
    html = html.replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>');

    // Italic (*text*)
    html = html.replace(/\*(.+?)\*/g, '<em>$1</em>');

    // Line breaks (preserve double newlines as paragraphs)
    const paragraphs = html.split('\n\n').filter(p => p.trim());
    html = paragraphs.map(p => {
      // Don't wrap headers in paragraphs
      if (p.trim().startsWith('<h')) {
        return p;
      }
      // Replace single newlines within paragraphs with <br>
      const withBreaks = p.replace(/\n/g, '<br>');
      return `<p>${withBreaks}</p>`;
    }).join('');

    return html;
  }

  open() {
    this.isOpen = true;
    this.container.classList.add('open');

    // Update ARIA expanded state (Phase 6)
    const infoToggle = document.querySelector('.btn-info');
    if (infoToggle) {
      infoToggle.setAttribute('aria-expanded', 'true');
    }

    // Announce panel state (Phase 6)
    if (this.lightbox && this.lightbox.announcer) {
      this.lightbox.announcer.announcePanelState(true);
    }

    // Move focus to panel close button (Phase 6)
    if (this.lightbox && this.lightbox.focusManager) {
      this.lightbox.focusManager.focusPanel();
    }

    // Trigger photo reflow in lightbox
    if (this.lightbox && this.lightbox.applyPhotoLayout) {
      this.lightbox.applyPhotoLayout({ open: true });
    }
  }

  close() {
    this.isOpen = false;
    this.container.classList.remove('open');

    // Update ARIA expanded state (Phase 6)
    const infoToggle = document.querySelector('.btn-info');
    if (infoToggle) {
      infoToggle.setAttribute('aria-expanded', 'false');
    }

    // Announce panel state (Phase 6)
    if (this.lightbox && this.lightbox.announcer) {
      this.lightbox.announcer.announcePanelState(false);
    }

    // Return focus to info toggle (Phase 6)
    if (this.lightbox && this.lightbox.focusManager) {
      this.lightbox.focusManager.focusInfoToggle();
    }

    // Restore photo to original position in lightbox
    if (this.lightbox && this.lightbox.applyPhotoLayout) {
      this.lightbox.applyPhotoLayout({ open: false });
    }
  }

  toggle() {
    if (this.isOpen) {
      this.close();
    } else {
      this.open();
    }
  }

  async toggleFactLock(factKey, btnElement) {
    if (!this.currentPhotoData || !this.currentPhotoData.id) return;

    const factRow = btnElement.closest('.fact-row');
    const isCurrentlyLocked = btnElement.classList.contains('locked');

    // Optimistic UI update
    btnElement.classList.toggle('locked');
    btnElement.setAttribute('aria-label', `${!isCurrentlyLocked ? 'Unlock' : 'Lock'} ${factKey}`);
    factRow.setAttribute('data-locked', !isCurrentlyLocked);

    // Update SVG icon
    const svg = btnElement.querySelector('svg');
    const lockedIcon = '<rect x="3" y="11" width="18" height="11" rx="2" ry="2"></rect><path d="M7 11V7a5 5 0 0 1 10 0v4"></path>';
    const unlockedIcon = '<rect x="3" y="11" width="18" height="11" rx="2" ry="2"></rect><path d="M7 11V7a5 5 0 0 1 9.9-1"></path>';
    svg.innerHTML = !isCurrentlyLocked ? lockedIcon : unlockedIcon;

    try {
      // Call toggle API endpoint
      const response = await this.app.api.post(
        `/api/photos/${this.currentPhotoData.id}/facts/${encodeURIComponent(factKey)}/toggle-lock`
      );

      // Sync with server response to handle multi-tab scenarios
      if (!this.currentPhotoData.aiAnalysis.lockedFactKeys) {
        this.currentPhotoData.aiAnalysis.lockedFactKeys = [];
      }

      // Update entire locked facts list from server response
      this.currentPhotoData.aiAnalysis.lockedFactKeys = response.lockedFactKeys || [];

      // Ensure UI is in sync with server state
      const serverIsLocked = response.isLocked;
      btnElement.classList.toggle('locked', serverIsLocked);
      btnElement.setAttribute('aria-label', `${serverIsLocked ? 'Unlock' : 'Lock'} ${factKey}`);
      factRow.setAttribute('data-locked', serverIsLocked);
      svg.innerHTML = serverIsLocked ? lockedIcon : unlockedIcon;

    } catch (error) {
      console.error('Failed to toggle fact lock:', error);

      // Revert UI on error
      btnElement.classList.toggle('locked', isCurrentlyLocked);
      btnElement.setAttribute('aria-label', `${isCurrentlyLocked ? 'Unlock' : 'Lock'} ${factKey}`);
      factRow.setAttribute('data-locked', isCurrentlyLocked);
      svg.innerHTML = isCurrentlyLocked ? lockedIcon : unlockedIcon;

      this.app.components.toast.show('Failed to toggle lock', { icon: '⚠️', type: 'error' });
    }
  }

  async regenerateAIAnalysis() {
    if (!this.currentPhotoData || !this.currentPhotoData.id) return;

    const regenerateBtn = this.container.querySelector('#btn-regenerate-new');
    if (!regenerateBtn) return;

    // Show loading state
    regenerateBtn.disabled = true;
    regenerateBtn.classList.add('loading');
    const originalText = regenerateBtn.querySelector('.action-label').textContent;
    regenerateBtn.querySelector('.action-label').textContent = 'Regenerating...';

    try {
      this.app.components.toast.show('Regenerating AI analysis...', { icon: '🎲', duration: 2000 });

      // Call new regenerate endpoint that preserves locked facts
      const updatedPhoto = await this.app.api.post(
        `/api/photos/${this.currentPhotoData.id}/regenerate-ai-analysis`
      );

      // Update current photo data
      this.currentPhotoData = updatedPhoto;

      // Re-render AI insights
      this.renderAIInsights(updatedPhoto);

      this.app.components.toast.show('AI analysis regenerated!', { icon: '✨', type: 'success' });

    } catch (error) {
      console.error('Failed to regenerate AI analysis:', error);
      this.app.components.toast.show('Failed to regenerate AI analysis', { icon: '⚠️', type: 'error' });

      // Restore button state
      regenerateBtn.disabled = false;
      regenerateBtn.classList.remove('loading');
      regenerateBtn.querySelector('.action-label').textContent = originalText;
    }
  }

  setupMobileGestures() {
    // Only setup on mobile
    if (window.innerWidth >= 768) return;

    let startY = 0;
    let currentY = 0;
    let isDragging = false;

    const handle = this.container.querySelector('.drag-handle');
    if (!handle) return;

    handle.addEventListener('touchstart', (e) => {
      startY = e.touches[0].clientY;
      isDragging = true;
      this.container.style.transition = 'none';
    });

    document.addEventListener('touchmove', (e) => {
      if (!isDragging || !this.isOpen) return;
      currentY = e.touches[0].clientY;
      const deltaY = currentY - startY;

      // Only allow dragging down
      if (deltaY > 0) {
        this.container.style.transform = `translateY(${deltaY}px)`;
      }
    });

    document.addEventListener('touchend', () => {
      if (!isDragging) return;
      isDragging = false;

      const deltaY = currentY - startY;
      const panelHeight = this.container.offsetHeight;
      const dismissThreshold = panelHeight * 0.5;

      this.container.style.transition = '';

      if (deltaY > dismissThreshold) {
        // Dismiss panel
        this.close();
      } else {
        // Snap back to original position
        this.container.style.transform = '';
      }
    });
  }

  // Get the DOM element to append to lightbox
  getElement() {
    return this.container;
  }
}
