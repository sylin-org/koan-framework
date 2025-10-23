/**
 * InsightsPanel - Compact SnapVault-inspired insights display
 * Replaces large card grid with information-dense fact rows
 */
export class InsightsPanel {
  constructor(api, state) {
    this.api = api;
    this.state = state;
  }

  /**
   * Render the insights panel
   * @param {Object} deliverable - Analysis deliverable with insights
   * @param {Object} authoritativeNotes - Parsed authoritative notes
   * @returns {string} HTML string
   */
  render(deliverable, authoritativeNotes = {}) {
    if (!deliverable || !deliverable.insights) {
      return this.renderEmpty();
    }

    const insights = deliverable.insights;
    const sections = this.organizeSections(insights, authoritativeNotes);

    return `
      <div class="insights-panel">
        ${this.renderOverview(sections.overview)}
        ${this.renderSummary(insights.summary)}
        ${this.renderGroupedFacts(sections.facts)}
        ${this.renderActions()}
      </div>
    `;
  }

  /**
   * Organize insights into hierarchical sections
   */
  organizeSections(insights, authoritativeNotes) {
    const sections = {
      overview: [],
      facts: {}
    };

    // Extract overview fields (high-level metrics)
    const overviewFields = ['title', 'status', 'confidence', 'completeness', 'lastUpdated'];
    overviewFields.forEach(field => {
      if (insights[field] !== undefined) {
        sections.overview.push({
          key: field,
          value: insights[field],
          metadata: this.getFieldMetadata(field, insights, authoritativeNotes)
        });
      }
    });

    // Group remaining fields by category
    Object.entries(insights).forEach(([key, value]) => {
      if (overviewFields.includes(key) || key === 'summary') return;

      const category = this.categorizeField(key);
      if (!sections.facts[category]) {
        sections.facts[category] = [];
      }

      sections.facts[category].push({
        key,
        value,
        metadata: this.getFieldMetadata(key, insights, authoritativeNotes)
      });
    });

    return sections;
  }

  /**
   * Get metadata for a field (source, confidence, override info)
   */
  getFieldMetadata(key, insights, authoritativeNotes) {
    // Check if this field is overridden by authoritative notes
    const notesValue = authoritativeNotes[key];
    const isNotesSourced = notesValue !== undefined;

    // Get confidence from insights metadata
    const confidenceData = insights._metadata?.confidence?.[key];
    const confidence = isNotesSourced ? 100 : (confidenceData || 85);

    // Check for override scenario
    const docValue = !isNotesSourced && insights[key];
    const overrideNotice = isNotesSourced && docValue && docValue !== notesValue
      ? `Doc said ${this.formatValue(docValue)} (overridden)`
      : null;

    return {
      source: isNotesSourced ? 'notes' : 'doc',
      confidence,
      overrideNotice,
      docReference: insights._metadata?.sources?.[key]
    };
  }

  /**
   * Categorize field into sections
   */
  categorizeField(key) {
    const categories = {
      financial: ['revenue', 'cost', 'budget', 'price', 'value', 'amount'],
      temporal: ['date', 'time', 'deadline', 'duration', 'period'],
      location: ['address', 'location', 'city', 'state', 'country'],
      contact: ['email', 'phone', 'contact', 'name'],
      technical: ['version', 'status', 'type', 'format'],
    };

    const keyLower = key.toLowerCase();
    for (const [category, keywords] of Object.entries(categories)) {
      if (keywords.some(kw => keyLower.includes(kw))) {
        return category;
      }
    }

    return 'general';
  }

  /**
   * Render overview section (key metrics)
   */
  renderOverview(fields) {
    if (fields.length === 0) return '';

    return `
      <div class="insights-section">
        <div class="insights-section-header">
          <h3 class="insights-section-title">Overview</h3>
        </div>
        <div class="insights-rows">
          ${fields.map(field => this.renderFactRow(field)).join('')}
        </div>
      </div>
    `;
  }

  /**
   * Render summary section
   */
  renderSummary(summary) {
    if (!summary) return '';

    return `
      <div class="insights-section">
        <div class="insights-section-header">
          <h3 class="insights-section-title">Summary</h3>
        </div>
        <div class="insights-summary-content">
          ${this.escapeHtml(summary)}
        </div>
      </div>
    `;
  }

  /**
   * Render grouped facts by category
   */
  renderGroupedFacts(factsByCategory) {
    return Object.entries(factsByCategory)
      .sort(([a], [b]) => {
        const order = ['financial', 'temporal', 'technical', 'location', 'contact', 'general'];
        return order.indexOf(a) - order.indexOf(b);
      })
      .map(([category, facts]) => {
        if (facts.length === 0) return '';

        return `
          <div class="insights-section">
            <div class="insights-section-header">
              <h3 class="insights-section-title">${this.formatCategoryName(category)}</h3>
            </div>
            <div class="insights-rows">
              ${facts.map(field => this.renderFactRow(field)).join('')}
            </div>
          </div>
        `;
      })
      .join('');
  }

  /**
   * Render a single fact row (SnapVault pattern)
   * 12px label, 14px value, tag-based metadata
   */
  renderFactRow(field) {
    const { key, value, metadata } = field;
    const isNotesSourced = metadata.source === 'notes';
    const confidenceClass = this.getConfidenceClass(metadata.confidence);

    return `
      <div class="insight-row ${isNotesSourced ? 'insight-row-notes' : ''}" data-source="${metadata.source}">
        <div class="insight-label">${this.formatFieldName(key)}</div>
        <div class="insight-value-container">
          <span class="insight-value">${this.formatValue(value)}</span>
          <div class="insight-tags">
            <span class="tag tag-source" data-source="${metadata.source}">
              ${metadata.source}
            </span>
            <span class="tag tag-confidence" data-confidence="${confidenceClass}">
              ${isNotesSourced ? '⭐' : '✓'} ${metadata.confidence}%
            </span>
          </div>
        </div>
        ${metadata.overrideNotice ? `
          <div class="insight-override-notice">
            ⚠️ ${this.escapeHtml(metadata.overrideNotice)}
          </div>
        ` : ''}
        ${metadata.docReference ? `
          <div class="insight-doc-reference">
            From: ${this.escapeHtml(metadata.docReference)}
          </div>
        ` : ''}
      </div>
    `;
  }

  /**
   * Render actions section
   */
  renderActions() {
    return `
      <div class="insights-section insights-actions">
        <button class="btn btn-secondary btn-sm" data-action="export-insights">
          <svg class="icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
            <polyline points="7 10 12 15 17 10"></polyline>
            <line x1="12" y1="15" x2="12" y2="3"></line>
          </svg>
          Export Insights
        </button>
        <button class="btn btn-secondary btn-sm" data-action="refresh-analysis">
          <svg class="icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <polyline points="23 4 23 10 17 10"></polyline>
            <polyline points="1 20 1 14 7 14"></polyline>
            <path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15"></path>
          </svg>
          Refresh Analysis
        </button>
      </div>
    `;
  }

  /**
   * Render empty state
   */
  renderEmpty() {
    return `
      <div class="insights-panel-empty">
        <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1">
          <circle cx="12" cy="12" r="10"></circle>
          <line x1="12" y1="8" x2="12" y2="12"></line>
          <line x1="12" y1="16" x2="12.01" y2="16"></line>
        </svg>
        <p>No insights yet</p>
        <p class="empty-hint">Upload documents to generate insights</p>
      </div>
    `;
  }

  /**
   * Get confidence CSS class
   */
  getConfidenceClass(confidence) {
    if (confidence >= 90) return 'high';
    if (confidence >= 70) return 'medium';
    return 'low';
  }

  /**
   * Format field name (camelCase -> Title Case)
   */
  formatFieldName(key) {
    return key
      .replace(/([A-Z])/g, ' $1')
      .replace(/^./, str => str.toUpperCase())
      .trim();
  }

  /**
   * Format category name
   */
  formatCategoryName(category) {
    return category.charAt(0).toUpperCase() + category.slice(1);
  }

  /**
   * Format value for display
   */
  formatValue(value) {
    if (value === null || value === undefined) return 'N/A';
    if (typeof value === 'boolean') return value ? 'Yes' : 'No';
    if (typeof value === 'number') {
      // Format currency
      if (value > 1000 && value < 1000000000) {
        return new Intl.NumberFormat('en-US', {
          style: 'currency',
          currency: 'USD'
        }).format(value);
      }
      return value.toLocaleString();
    }
    if (typeof value === 'object') return JSON.stringify(value, null, 2);
    return String(value);
  }

  /**
   * Escape HTML
   */
  escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
  }

  /**
   * Attach event handlers
   */
  attachEventHandlers(container, eventBus) {
    container.addEventListener('click', (e) => {
      const target = e.target.closest('[data-action]');
      if (!target) return;

      const action = target.dataset.action;

      switch (action) {
        case 'export-insights':
          eventBus.emit('export-insights');
          break;
        case 'refresh-analysis':
          eventBus.emit('refresh-analysis');
          break;
      }
    });
  }
}
