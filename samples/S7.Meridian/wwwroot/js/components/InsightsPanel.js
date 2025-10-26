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
   * @param {Object} context - Render context containing canonical fact payload, notes, quality metrics, and deliverable metadata
   * @returns {string} HTML string
   */
  render(context = {}) {
    const canonical = context.canonical;
    const notes = context.notes ?? {};
    const quality = context.quality;

    if (!canonical || typeof canonical !== 'object') {
      return this.renderEmpty(context);
    }

    const summary = this.extractSummary(canonical);
    const factEntries = this.buildFactEntries(canonical, notes);
    const overview = this.buildOverviewEntries(quality, canonical.metadata, context.deliverable);
    const groupedFacts = this.organizeFacts(factEntries);

    const hasFacts = Object.values(groupedFacts).some(group => group.length > 0);
    const hasOverview = overview.length > 0;
    const hasSummary = Boolean(summary);

    if (!hasFacts && !hasOverview && !hasSummary) {
      return this.renderEmpty(context);
    }

    return `
      <div class="insights-panel">
        ${hasOverview ? this.renderOverview(overview) : ''}
        ${hasSummary ? this.renderSummary(summary) : ''}
        ${hasFacts ? this.renderGroupedFacts(groupedFacts) : ''}
        ${this.renderActions()}
      </div>
    `;
  }

  extractSummary(canonical) {
    const formatted = canonical?.formatted ?? {};
    if (typeof formatted.summary === 'string' && formatted.summary.trim()) {
      return formatted.summary.trim();
    }

    const fields = canonical?.fields ?? {};
    if (typeof fields.summary === 'string' && fields.summary.trim()) {
      return fields.summary.trim();
    }

    return null;
  }

  buildFactEntries(canonical, notes) {
    const fields = canonical?.fields ?? {};
    const formatted = canonical?.formatted ?? {};
    const evidence = canonical?.evidence ?? {};
    const reserved = new Set(['summary']);
    const entries = [];

    Object.keys(fields).forEach((key) => {
      if (reserved.has(key)) {
        return;
      }

      const rawValue = fields[key];
      if (rawValue === null || rawValue === undefined || rawValue === '') {
        return;
      }

      const formattedValue = formatted[key] ?? rawValue;
      const evidenceToken = evidence[key];
      const noteValue = this.lookupNoteValue(notes, key);
      const metadata = this.buildFieldMetadata(key, noteValue, evidenceToken);

      entries.push({
        key,
        value: formattedValue,
        metadata
      });
    });

    return entries;
  }

  buildOverviewEntries(quality, metadata, deliverable) {
    const entries = [];

    if (quality) {
      if (typeof quality.highConfidence === 'number') {
        entries.push({
          key: 'highConfidence',
          value: `${quality.highConfidence} high-confidence fact${quality.highConfidence === 1 ? '' : 's'}`,
          metadata: { source: 'system', confidence: 100 }
        });
      }

      if (typeof quality.mediumConfidence === 'number' && quality.mediumConfidence > 0) {
        entries.push({
          key: 'mediumConfidence',
          value: `${quality.mediumConfidence} medium-confidence fact${quality.mediumConfidence === 1 ? '' : 's'}`,
          metadata: { source: 'system', confidence: 100 }
        });
      }

      if (typeof quality.totalConflicts === 'number') {
        entries.push({
          key: 'totalConflicts',
          value: `${quality.totalConflicts} conflict${quality.totalConflicts === 1 ? '' : 's'}`,
          metadata: { source: 'system', confidence: 100 }
        });
      }
    }

    const generated = metadata?.generatedAt ?? metadata?.GeneratedAt;
    if (generated) {
      entries.push({
        key: 'generatedAt',
        value: this.formatTimestamp(generated),
        metadata: { source: 'system', confidence: 100 }
      });
    }

    if (deliverable?.version) {
      entries.push({
        key: 'deliverableVersion',
        value: `Deliverable v${deliverable.version}`,
        metadata: { source: 'system', confidence: 100 }
      });
    }

    return entries;
  }

  organizeFacts(entries) {
    return entries.reduce((acc, entry) => {
      const category = this.categorizeField(entry.key);
      if (!acc[category]) {
        acc[category] = [];
      }
      acc[category].push(entry);
      return acc;
    }, {});
  }

  lookupNoteValue(notes, key) {
    if (!notes || typeof notes !== 'object') {
      return undefined;
    }

    const normalized = key.toLowerCase();
    if (Object.prototype.hasOwnProperty.call(notes, normalized)) {
      return notes[normalized];
    }

    const underscored = normalized.replace(/[^a-z0-9]+/g, '_');
    if (Object.prototype.hasOwnProperty.call(notes, underscored)) {
      return notes[underscored];
    }

    return undefined;
  }

  buildFieldMetadata(key, noteValue, evidenceToken) {
    const source = noteValue !== undefined ? 'notes' : 'doc';
    const confidence = noteValue !== undefined ? 100 : this.extractConfidence(evidenceToken);
    const docReference = this.buildDocReference(evidenceToken);

    const metadata = {
      source,
      confidence,
      docReference
    };

    if (source === 'notes' && docReference) {
      metadata.overrideNotice = 'Authoritative notes override document values for this field.';
    }

    if (evidenceToken?.text) {
      metadata.excerpt = evidenceToken.text;
    }

    return metadata;
  }

  extractConfidence(evidenceToken) {
    if (!evidenceToken) {
      return 85;
    }

    const value = typeof evidenceToken.confidence === 'number'
      ? evidenceToken.confidence
      : undefined;

    if (value === undefined) {
      return 85;
    }

    if (value > 1) {
      return Math.round(value);
    }

    return Math.round(value * 100);
  }

  buildDocReference(evidenceToken) {
    if (!evidenceToken) {
      return null;
    }

    const parts = [];
    if (evidenceToken.sourceFileName) {
      parts.push(evidenceToken.sourceFileName);
    }
    if (typeof evidenceToken.page === 'number' && evidenceToken.page > 0) {
      parts.push(`p. ${evidenceToken.page}`);
    }
    if (evidenceToken.sectionHeading) {
      parts.push(evidenceToken.sectionHeading);
    }

    return parts.length > 0 ? parts.join(' · ') : null;
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
  renderEmpty(context = {}) {
    const documentCount = Array.isArray(context.documents) ? context.documents.length : 0;
    const notesCount = context.notes ? Object.keys(context.notes).length : 0;

    let hint = 'Upload documents to generate insights';
    if (documentCount > 0) {
      hint = 'Processing in progress. Refresh once the pipeline completes.';
    } else if (notesCount > 0) {
      hint = 'Add source documents so authoritative notes can be validated.';
    }

    return `
      <div class="insights-panel-empty">
        <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1">
          <circle cx="12" cy="12" r="10"></circle>
          <line x1="12" y1="8" x2="12" y2="12"></line>
          <line x1="12" y1="16" x2="12.01" y2="16"></line>
        </svg>
        <p>No insights yet</p>
        <p class="empty-hint">${this.escapeHtml(hint)}</p>
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

  formatTimestamp(value) {
    if (!value) {
      return 'Generated just now';
    }

    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
      return String(value);
    }

    return date.toLocaleString(undefined, {
      year: 'numeric',
      month: 'short',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit'
    });
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
