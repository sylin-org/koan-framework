/**
 * AnalysisDetailView - Unified dashboard for viewing and configuring an analysis pipeline.
 * Combines the former read-only detail view with the workspace experience.
 */
export class AnalysisDetailView {
	constructor(api, eventBus, router, toast) {
		this.api = api;
		this.eventBus = eventBus;
		this.router = router;
		this.toast = toast;

		this.analysis = null;
		this.analysisTypes = [];
		this.sourceTypes = [];
		this.documents = [];
		this.notes = '';
		this.deliverable = null;
		this.deliverableCanonical = null;
		this.pipelineQuality = null;

		this.isCreating = false;
		this.isLoading = false;
		this.activeTab = 'overview';

		this.filesToUpload = [];
		this.pendingUploads = new Map();

		this.host = null;
		this.boundHandlers = {};

		this.expandedDocuments = new Set();
		this.expandedFacts = new Set();
		this.inlineEdit = {
			activeField: null,
			draft: '',
		};
		this.pendingPipelineUpdates = {};
		this.pendingAnalysisType = null;
		this.isDirty = false;
		this.isRefreshingPipeline = false;
		this.activeJob = null;
	}

	async initCreate() {
		this.isCreating = true;
		this.isLoading = false;
		this.activeTab = 'overview';
		this.analysis = {
			id: null,
			name: '',
			description: '',
			analysisTypeId: '',
			analysisTypeName: '',
			status: 'Draft',
			documentCount: 0,
			updatedAt: new Date().toISOString(),
			createdAt: new Date().toISOString()
		};
		this.notes = '';
		this.documents = [];
		this.deliverable = null;
		this.deliverableCanonical = null;
		this.pipelineQuality = null;
		this.filesToUpload = [];
		this.pendingUploads.clear();
		this.expandedDocuments.clear();
		this.expandedFacts.clear();
		this.inlineEdit = { activeField: null, draft: '' };
		this.pendingPipelineUpdates = {};
		this.pendingAnalysisType = null;
		this.isDirty = false;
		this.isRefreshingPipeline = false;
		this.activeJob = null;

		await Promise.all([
			this.loadAnalysisTypes(),
			this.loadSourceTypes()
		]);
	}

	async load(id) {
		this.isCreating = false;
		this.isLoading = true;
		this.pendingUploads.clear();
		this.filesToUpload = [];
		this.expandedDocuments.clear();
		this.expandedFacts.clear();
		this.inlineEdit = { activeField: null, draft: '' };
		this.pendingPipelineUpdates = {};
		this.pendingAnalysisType = null;
		this.isDirty = false;
		this.isRefreshingPipeline = false;
		this.activeJob = null;

		try {
			const [graphResponse, analysisTypes, sourceTypes] = await Promise.all([
				this.api.getPipelineGraph(id),
				this.api.getAnalysisTypes(),
				this.api.getSourceTypes()
			]);

			const graph = graphResponse?.graph || graphResponse?.Graph;
			if (!graph) {
				throw new Error('Graph data unavailable');
			}

			const pipeline = graph.pipeline || graph.Pipeline;
			this.analysis = this.normalizeAnalysis(pipeline);
			this.analysisTypes = Array.isArray(analysisTypes) ? analysisTypes : [];
			this.sourceTypes = Array.isArray(sourceTypes) ? sourceTypes : [];

			const rawDocuments = graph.documents || graph.Documents || [];
			this.documents = Array.isArray(rawDocuments) 
				? rawDocuments.map(doc => this.decorateDocument(doc)) 
				: [];

			const rawNotes = graph.notes || graph.Notes;
			this.notes = rawNotes?.authoritativeNotes || rawNotes?.AuthoritativeNotes || '';

			const rawDeliverable = graph.deliverable || graph.Deliverable;
			this.deliverable = rawDeliverable || null;

			const rawCanonical = graph.canonical || graph.Canonical;
			this.deliverableCanonical = rawCanonical || null;

			const rawQuality = graph.quality || graph.Quality;
			this.pipelineQuality = this.normalizeQuality(rawQuality || this.analysis.quality || this.deliverable?.quality);

			// Extract active job (Pending or Processing status)
			const rawJobs = graph.jobs || graph.Jobs || [];
			const jobs = Array.isArray(rawJobs) ? rawJobs : [];
			this.activeJob = jobs.find(job => {
				const status = (job.status || job.Status || '').toString().toLowerCase();
				return status === 'pending' || status === 'processing';
			}) || null;

			if (!this.analysis?.id) {
				throw new Error('Analysis identifier missing');
			}
		} catch (error) {
			console.error('Failed to load analysis detail view', error);
			this.toast.error('Failed to load analysis');
			this.analysis = null;
		} finally {
			this.isLoading = false;
		}
	}
	renderSkeleton() {
		return `
			<div class="analysis-dashboard loading">
				<div class="analysis-loading">
					<svg class="spinner icon-spin" width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
						<circle cx="12" cy="12" r="10"></circle>
					</svg>
					<p>Loading analysis...</p>
				</div>
			</div>
		`;
	}

	render() {
		if (this.isLoading) {
			return this.renderSkeleton();
		}

		if (!this.isCreating && !this.analysis) {
			return `
				<div class="analysis-dashboard error">
					<div class="analysis-error">
						<h2>Analysis unavailable</h2>
						<p>The requested analysis could not be loaded. It may have been deleted.</p>
						<button class="btn" data-action="back">Back</button>
					</div>
				</div>
			`;
		}

		return `
			<div class="analysis-dashboard" data-tab="${this.escapeAttr(this.activeTab)}">
				${this.renderHero()}
				<div class="analysis-surface">
					${this.renderTabNav()}
					<div class="analysis-tab-content">
						${this.renderActiveTab()}
					</div>
				</div>
				<input type="file" data-file-input multiple accept=".pdf,.txt,.doc,.docx" hidden />
			</div>
		`;
	}

	renderHero() {
		const isNew = this.isCreating;
		const statusInfo = this.formatPipelineStatus(this.analysis?.status);
		const metrics = this.buildHeroMetrics();
		const actions = this.buildHeroActions();
		const dirtyBadge = this.isDirty ? '<span class="hero-dirty-indicator">Unsaved changes</span>' : '';

		return `
			<header class="analysis-hero">
				<div class="analysis-hero-header">
					<button class="btn-link" data-action="back" aria-label="Back to analyses list">
						<svg class="icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
							<polyline points="15 18 9 12 15 6"></polyline>
							<line x1="20" y1="12" x2="9" y2="12"></line>
						</svg>
						Back
					</button>
					<div class="analysis-hero-identity">
						${this.renderInlineField('name', {
							placeholder: isNew ? 'Name this analysis' : 'Untitled analysis'
						})}
						${this.renderInlineField('description', {
							placeholder: isNew ? 'Describe the goal for this analysis' : 'Add a short description'
						})}
					</div>
					<div class="analysis-hero-type">
						<label>Analysis Type</label>
						${this.renderInlineField('analysisTypeId', {
							displayOnly: this.analysisTypes.length === 0 && !this.isCreating
						})}
					</div>
				</div>
				<div class="analysis-hero-card">
					<div class="analysis-hero-status ${statusInfo?.key ? `status-${statusInfo.key}` : ''}">
						<div class="status-indicator">
							${this.renderStatusIcon(statusInfo?.key)}
							<span>${this.escape(statusInfo?.label || (isNew ? 'Draft' : 'Unknown status'))}</span>
						</div>
						${actions.primary}
					</div>
					<div class="analysis-hero-metrics">
						${metrics.map(metric => this.renderMetricChip(metric)).join('')}
					</div>
					<div class="analysis-hero-actions">
						${dirtyBadge}
						${actions.secondary.join('')}
					</div>
				</div>
				${this.renderProgressBar()}
			</header>
		`;
	}

	renderProgressBar() {
		if (!this.activeJob) {
			return '';
		}

		const processed = this.activeJob.processedDocuments ?? this.activeJob.ProcessedDocuments ?? 0;
		const total = this.activeJob.totalDocuments ?? this.activeJob.TotalDocuments ?? 0;
		const progressPercent = this.activeJob.progressPercent ?? this.activeJob.ProgressPercent ?? 0;
		const status = this.activeJob.status ?? this.activeJob.Status ?? 'Processing';

		return `
			<div class="analysis-progress-bar">
				<div class="progress-info">
					<span class="progress-label">${this.escape(status)}: ${processed} of ${total} documents processed</span>
					<span class="progress-percent">${Math.round(progressPercent)}%</span>
				</div>
				<div class="progress-track">
					<div class="progress-fill" style="width: ${Math.round(progressPercent)}%"></div>
				</div>
			</div>
		`;
	}

	buildHeroMetrics() {
		const metrics = [];

		if (!this.isCreating) {
			const documentCount = this.documents.length || this.analysis?.documentCount || 0;
			metrics.push({
				key: 'documents',
				label: 'Documents',
				value: `${documentCount}`,
				icon: '🗂'
			});

			const factCount = this.deliverableCanonical?.fields
				? Object.keys(this.deliverableCanonical.fields).length
				: 0;
			metrics.push({
				key: 'facts',
				label: 'Facts',
				value: factCount > 0 ? `${factCount}` : 'Pending',
				icon: '📊'
			});

			const confidence = this.calculateAverageConfidence();
			metrics.push({
				key: 'confidence',
				label: 'Avg Confidence',
				value: confidence ? `${confidence}%` : 'Pending',
				icon: '🎯'
			});

			const updated = this.formatRelativeDate(this.analysis?.updatedAt || this.analysis?.lastUpdated);
			metrics.push({
				key: 'updated',
				label: 'Updated',
				value: updated,
				icon: '🔄'
			});
		} else {
			metrics.push(
				{ key: 'step', label: 'Step 1', value: 'Describe the analysis', icon: '📝' },
				{ key: 'step2', label: 'Step 2', value: 'Upload documents', icon: '📄' },
				{ key: 'step3', label: 'Step 3', value: 'Review insights', icon: '✨' }
			);
		}

		return metrics;
	}

	buildHeroActions() {
		if (this.isCreating) {
			return {
				primary: `
					<button class="btn btn-primary" data-action="create-analysis">
						<svg class="icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
							<line x1="12" y1="5" x2="12" y2="19"></line>
							<line x1="5" y1="12" x2="19" y2="12"></line>
						</svg>
						Create Analysis
					</button>
				`,
				secondary: [
					`<button class="btn btn-neutral" data-action="cancel-create">Cancel</button>`
				]
			};
		}

		const secondary = [];

		const hasActiveJob = this.activeJob !== null;
		const refreshIconClass = this.isRefreshingPipeline ? 'icon icon-spin' : 'icon';
		const refreshButtonClass = 'btn btn-secondary';
		const refreshDisabledAttr = (this.isRefreshingPipeline || hasActiveJob) ? ' disabled' : '';
		const refreshLabel = this.isRefreshingPipeline ? 'Refreshing...' : (hasActiveJob ? 'Job Running...' : 'Refresh');

		secondary.push(`
			<button class="${refreshButtonClass}" data-action="refresh-analysis"${refreshDisabledAttr}>
				<svg class="${refreshIconClass}" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
					<polyline points="23 4 23 10 17 10"></polyline>
					<polyline points="1 20 1 14 7 14"></polyline>
					<path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15"></path>
				</svg>
				${refreshLabel}
			</button>
		`);

		if (this.deliverable) {
			secondary.push(`
				<button class="btn btn-secondary" data-action="reprocess-pipeline" title="Regenerate deliverable with latest merge logic">
					<svg class="icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
						<path d="M21.5 2v6h-6M2.5 22v-6h6M2 11.5a10 10 0 0 1 18.8-4.3M22 12.5a10 10 0 0 1-18.8 4.2"></path>
					</svg>
					Reprocess
				</button>
			`);
		}

		secondary.push(`
			<button class="btn btn-secondary" data-action="export-report">
				<svg class="icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
					<path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
					<polyline points="7 10 12 15 17 10"></polyline>
					<line x1="12" y1="15" x2="12" y2="3"></line>
				</svg>
				Export Report
			</button>
		`);

		if (this.isDirty && !this.isCreating) {
			secondary.unshift(`
				<button class="btn btn-primary" data-action="save-pipeline" aria-label="Save pending changes">
					<svg class="icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
						<polyline points="20 6 9 17 4 12"></polyline>
					</svg>
					Save changes
				</button>
			`);
		}

		const primary = this.deliverable
			? `
					<button class="btn btn-primary" data-action="download-deliverable">
						<svg class="icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
							<polyline points="8 4 16 12 8 20"></polyline>
						</svg>
						Download Markdown
					</button>
				`
			: `
					<span class="analysis-hero-hint">Upload documents to generate a deliverable.</span>
				`;

		return { primary, secondary };
	}

	renderInlineField(field, options = {}) {
		const active = this.inlineEdit.activeField === field;
		const showDirty = this.hasPendingChange(field);
		const placeholder = options.placeholder || '';

		const currentValue = (() => {
			if (field === 'analysisTypeId') {
				if (this.pendingAnalysisType !== null && this.pendingAnalysisType !== undefined) {
					return this.pendingAnalysisType;
				}
				return (this.analysis?.analysisTypeId || '').toString();
			}
			if (Object.prototype.hasOwnProperty.call(this.pendingPipelineUpdates, field)) {
				return this.pendingPipelineUpdates[field];
			}
			if (field === 'description') {
				return this.analysis?.description || '';
			}
			if (field === 'name') {
				return this.analysis?.name || '';
			}
			return this.analysis?.[field] || '';
		})();

		if (field === 'analysisTypeId' && options.displayOnly) {
			return `<div class="inline-display">${this.escape(this.resolveAnalysisTypeName(currentValue) || 'Not set')}</div>`;
		}

		if (active) {
			const value = this.inlineEdit.draft;
			if (field === 'analysisTypeId') {
				return `
					<select class="inline-input" data-inline-input="analysisTypeId" autofocus>
						<option value="">Select a type...</option>
						${this.analysisTypes.map(type => {
							const typeId = (type.id || type.Id || '').toString();
							const selected = typeId === value ? 'selected' : '';
							return `<option value="${this.escapeAttr(typeId)}" ${selected}>${this.escape(type.name || type.Name || 'Unnamed Type')}</option>`;
						}).join('')}
					</select>
				`;
			}

			if (field === 'description') {
				return `<textarea class="inline-input" data-inline-input="description" rows="2" maxlength="512" autofocus>${this.escape(value)}</textarea>`;
			}

			return `<input class="inline-input" data-inline-input="${this.escapeAttr(field)}" type="text" maxlength="128" value="${this.escapeAttr(value)}" placeholder="${this.escapeAttr(placeholder)}" autofocus />`;
		}

		const displayValue = field === 'analysisTypeId'
			? (this.resolveAnalysisTypeName(currentValue) || 'Select analysis type')
			: currentValue || placeholder || (field === 'name' ? 'Untitled analysis' : 'Add details');

		const wrapperTag = field === 'name' ? 'h1' : field === 'description' ? 'p' : 'div';
		const classes = ['inline-display'];
		if (showDirty) {
			classes.push('is-dirty');
		}
		return `
			<${wrapperTag} class="${classes.join(' ')}" data-inline-trigger="${this.escapeAttr(field)}" tabindex="0">
				${this.escape(displayValue)}
				<span class="inline-edit-hint">Edit</span>
			</${wrapperTag}>
		`;
	}

	hasPendingChange(field) {
		if (field === 'analysisTypeId') {
			const current = (this.analysis?.analysisTypeId || '').toString();
			const pending = this.pendingAnalysisType !== null && this.pendingAnalysisType !== undefined
				? this.pendingAnalysisType
				: current;
			return pending !== current;
		}

		if (Object.prototype.hasOwnProperty.call(this.pendingPipelineUpdates, field)) {
			return this.pendingPipelineUpdates[field] !== (this.analysis?.[field] || '');
		}

		return false;
	}

	renderMetricChip(metric) {
		return `
			<div class="hero-metric" data-key="${this.escapeAttr(metric.key)}">
				<span class="hero-metric-icon">${metric.icon || ''}</span>
				<div class="hero-metric-content">
					<span class="hero-metric-label">${this.escape(metric.label)}</span>
					<span class="hero-metric-value">${this.escape(metric.value)}</span>
				</div>
			</div>
		`;
	}

	renderRowChevron(expanded) {
		const points = expanded ? '6 9 12 15 18 9' : '9 6 15 12 9 18';
		return `
			<svg class="chevron-icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
				<polyline points="${points}"></polyline>
			</svg>
		`;
	}

		renderTabNav() {
		const tabs = [
			{ id: 'overview', label: 'Overview' },
			{ id: 'configuration', label: 'Configuration' }
		];

		return `
			<nav class="analysis-tabs" role="tablist">
				${tabs.map(tab => {
					const isActive = this.activeTab === tab.id;
					const disabled = tab.disabled ? 'aria-disabled="true"' : '';
					return `
						<button
							type="button"
							class="analysis-tab ${isActive ? 'active' : ''}"
							role="tab"
							data-tab="${tab.id}"
							aria-selected="${isActive}"
							${disabled}
						>
							${this.escape(tab.label)}
						</button>
					`;
				}).join('')}
			</nav>
		`;
	}

		renderActiveTab() {
		switch (this.activeTab) {
			case 'configuration':
				return this.renderConfigurationTab();
			case 'overview':
			default:
				return this.renderOverviewTab();
		}
	}

	renderOverviewTab() {
		if (this.isCreating) {
			return this.renderCreateOverview();
		}

		return `
			<section class="overview-layout">
				${this.renderOverviewStatsRow()}
				<div class="overview-split">
					${this.renderOverviewDocumentsPane()}
					${this.renderOverviewFactsPane()}
				</div>
			</section>
		`;
	}

	renderCreateOverview() {
		return `
			<section class="overview-layout">
				<div class="overview-mini-hero">
					${this.renderOverviewStat({
						key: 'queued-files',
						label: 'Queued documents',
						value: `${this.filesToUpload.length}`,
						helper: this.filesToUpload.length ? 'Ready to upload when created' : 'Drop or browse to add files'
					})}
					${this.renderOverviewStat({
						key: 'analysis-name',
						label: 'Analysis name',
						value: this.analysis?.name?.trim() ? 'Named' : 'Needs name',
						helper: this.analysis?.name?.trim() || 'Add a descriptive title'
					})}
					${this.renderOverviewStat({
						key: 'analysis-type',
						label: 'Analysis type',
						value: this.analysis?.analysisTypeId ? 'Selected' : 'Not set',
						helper: this.resolveAnalysisTypeName(this.analysis?.analysisTypeId) || 'Pick a type in configuration'
					})}
				</div>
				<div class="overview-split">
					<div class="overview-pane overview-documents">
						<div class="pane-header">
							<h2>Queued Documents</h2>
							<button class="btn btn-secondary" data-action="open-file-picker">Add</button>
						</div>
						<div class="pane-body">
							${this.renderSelectedFiles()}
						</div>
						${this.renderDropZone({ label: 'Drop documents to queue uploads', helperText: 'Supported: PDF, DOCX, DOC, TXT — up to 200 MB', compact: true })}
					</div>
					<div class="overview-pane overview-facts">
						<div class="pane-header">
							<h2>Facts</h2>
						</div>
						<div class="pane-empty">
							<p>Create the analysis to generate facts.</p>
						</div>
					</div>
				</div>
			</section>
		`;
	}

	renderOverviewStatsRow() {
		const stats = this.buildOverviewStats();
		if (!stats.length) {
			return '';
		}

		return `
			<div class="overview-mini-hero">
				${stats.map(stat => this.renderOverviewStat(stat)).join('')}
			</div>
		`;
	}

	renderOverviewStat(stat) {
		if (!stat) {
			return '';
		}

		const classes = ['mini-hero-tile'];
		if (stat.intent) {
			classes.push(`intent-${stat.intent}`);
		}

		return `
			<article class="${classes.join(' ')}" data-stat="${this.escapeAttr(stat.key || '')}">
				<div class="mini-hero-value">${this.escape(stat.value ?? '')}</div>
				${stat.helper ? `<div class="mini-hero-helper">${this.escape(stat.helper)}</div>` : ''}
			</article>
		`;
	}

	buildOverviewStats() {
		const stats = [];
		const documents = this.documents.length || this.analysis?.documentCount || 0;
		const updated = this.analysis?.updatedAt || this.analysis?.lastUpdated;
		const updatedLabel = updated ? this.formatRelativeDate(updated) : '';

		stats.push({
			key: 'documents',
			label: 'Documents',
			value: `${documents}`,
			helper: updatedLabel || 'Latest snapshot'
		});

		const quality = this.pipelineQuality;
		const factCount = this.estimateFactCount();
		const highConfidence = quality?.highConfidence;
		const conflicts = quality?.totalConflicts;
		const coverage = quality?.citationCoverage;
		const overrides = quality?.notesSourced ?? this.countAuthoritativeOverrides();

		stats.push({
			key: 'high-confidence',
			label: 'High-confidence facts',
			value: highConfidence != null ? `${highConfidence}` : factCount ? 'Pending' : '—',
			intent: highConfidence > 0 ? 'success' : 'neutral'
		});

		stats.push({
			key: 'conflicts',
			label: 'Conflicts',
			value: conflicts != null ? `${conflicts}` : factCount ? 'Pending' : '—',
			intent: conflicts > 0 ? 'alert' : 'neutral'
		});

		stats.push({
			key: 'coverage',
			label: 'Citation coverage',
			value: coverage != null ? `${Math.round(coverage)}%` : factCount ? 'Pending' : '—',
			helper: coverage != null ? 'Facts with sources' : ''
		});

		stats.push({
			key: 'overrides',
			label: 'Authoritative overrides',
			value: overrides != null ? `${overrides}` : '0',
			intent: overrides > 0 ? 'info' : 'neutral'
		});

		return stats;
	}

	beginInlineEdit(field) {
		if (!field) {
			return;
		}

		let draft = '';
		if (field === 'analysisTypeId') {
			if (this.pendingAnalysisType !== null && this.pendingAnalysisType !== undefined) {
				draft = this.pendingAnalysisType || '';
			} else {
				draft = (this.analysis?.analysisTypeId || '').toString();
			}
		} else if (Object.prototype.hasOwnProperty.call(this.pendingPipelineUpdates, field)) {
			draft = this.pendingPipelineUpdates[field] || '';
		} else {
			draft = this.analysis?.[field] || '';
		}

		this.inlineEdit = { activeField: field, draft };
		this.refresh();
	}

	cancelInlineEdit() {
		this.inlineEdit = { activeField: null, draft: '' };
		this.refresh();
	}

	finalizeInlineEdit(field, rawValue) {
		if (!field || this.inlineEdit.activeField !== field) {
			return;
		}

		let value = rawValue;
		if (field === 'name' || field === 'analysisTypeId') {
			value = (value || '').trim();
		} else if (field === 'description') {
			value = (value || '').trim();
		}

		if (field === 'analysisTypeId') {
			const current = (this.analysis?.analysisTypeId || '').toString();
			if (this.isCreating && this.analysis) {
				this.analysis.analysisTypeId = value || '';
				this.analysis.analysisTypeName = this.resolveAnalysisTypeName(value);
				this.pendingAnalysisType = null;
			} else if (value === current || (!value && !current)) {
				this.pendingAnalysisType = null;
			} else {
				this.pendingAnalysisType = value || '';
			}
		} else {
			const current = this.analysis?.[field] || '';
			if (this.isCreating && this.analysis) {
				this.analysis[field] = value;
				delete this.pendingPipelineUpdates[field];
			} else if (value === current) {
				delete this.pendingPipelineUpdates[field];
			} else {
				this.pendingPipelineUpdates[field] = value;
			}
		}

		this.inlineEdit = { activeField: null, draft: '' };
		if (!this.isCreating) {
			this.updateDirtyState();
		}
		this.refresh();
	}

	updateDirtyState() {
		this.isDirty = this.hasPendingChanges();
	}

	hasPendingChanges() {
		if (Object.keys(this.pendingPipelineUpdates).length > 0) {
			return true;
		}
		const current = (this.analysis?.analysisTypeId || '').toString();
		const pending = this.pendingAnalysisType;
		if (pending !== null && pending !== undefined && pending.toString() !== current) {
			return true;
		}
		return false;
	}

	renderOverviewDocumentsPane() {
		const list = this.documents.map(doc => this.renderOverviewDocumentItem(doc)).join('');

		return `
			<div class="overview-pane overview-documents">
				<div class="pane-header">
					<h2>Documents</h2>
					<div class="pane-actions">
						<button class="btn btn-secondary" data-action="open-file-picker">Add</button>
					</div>
				</div>
				<div class="pane-body doc-pane-body">
					${list ? `<ul class="overview-doc-list">${list}</ul>` : `<div class="pane-empty"><p>Drop files to start collecting insights.</p></div>`}
				</div>
				${this.renderDropZone({ compact: true })}
			</div>
		`;
	}

	toggleDocumentRow(documentId) {
		if (!documentId) {
			return;
		}

		if (this.expandedDocuments.has(documentId)) {
			this.expandedDocuments.delete(documentId);
		} else {
			this.expandedDocuments.add(documentId);
		}
		this.refresh();
	}

	renderOverviewDocumentItem(doc) {
		if (!doc) {
			return '';
		}

		const id = (doc.id || doc.Id || '').toString();
		const fileName = doc.originalFileName || doc.OriginalFileName || doc.fileName || 'Untitled Document';
		const typeName = doc.sourceTypeName || doc.SourceTypeName || this.lookupSourceTypeName(this.getDocumentSourceTypeId(doc));
		const statusValue = doc.status || doc.Status || 'Pending';
		const status = this.formatDocumentStatus(statusValue);
		const statusKey = status.toLowerCase().replace(/[^a-z0-9]+/g, '-');
		const confidence = this.formatConfidence(doc.classificationConfidence || doc.ClassificationConfidence);
		const uploadedAt = doc.uploadedAt || doc.UploadedAt || doc.createdAt || doc.CreatedAt;
		const size = doc.size || doc.Size || 0;
		const pageCount = doc.pageCount || doc.PageCount || 0;
		const expanded = this.expandedDocuments.has(id);
		
		// File icon based on extension
		const fileExt = fileName.split('.').pop()?.toLowerCase() || '';
		const fileIcon = this.getFileIcon(fileExt);
		
		const summaryPieces = [typeName || 'Unclassified'];
		if (pageCount > 0) {
			summaryPieces.push(`${pageCount} page${pageCount === 1 ? '' : 's'}`);
		}
		if (confidence) {
			summaryPieces.push(`${confidence} confidence`);
		}
		if (uploadedAt) {
			summaryPieces.push(this.formatRelativeDate(uploadedAt));
		}
		const summaryMeta = summaryPieces.map(part => `<span>${this.escape(part)}</span>`).join('<span class="meta-separator">•</span>');

		return `
			<li class="doc-row ${expanded ? 'expanded' : ''}" data-document-id="${this.escapeAttr(id)}">
				<button class="doc-row-toggle" data-action="toggle-document" data-document-id="${this.escapeAttr(id)}" aria-expanded="${expanded}" aria-controls="doc-details-${this.escapeAttr(id)}">
					<div class="doc-summary">
						<span class="doc-icon">${fileIcon}</span>
						<div class="doc-summary-text">
							<span class="doc-name">${this.escape(fileName)}</span>
							${summaryMeta ? `<div class="doc-row-meta">${summaryMeta}</div>` : ''}
						</div>
					</div>
					<span class="status-badge status-${this.escapeAttr(statusKey)}">${this.escape(status)}</span>
					<span class="row-chevron" aria-hidden="true">${this.renderRowChevron(expanded)}</span>
				</button>
				<div class="doc-row-details" id="doc-details-${this.escapeAttr(id)}" ${expanded ? '' : 'hidden'}>
					<div class="doc-detail-grid">
						<div>
							<h3>Document Type</h3>
							<select data-action="document-type" data-document-id="${this.escapeAttr(id)}" data-current-value="${this.escapeAttr(this.getDocumentSourceTypeId(doc))}">
								${this.buildSourceTypeOptions(this.getDocumentSourceTypeId(doc))}
							</select>
						</div>
						<div>
							<h3>Details</h3>
							<ul class="doc-detail-meta">
								${pageCount ? `<li><strong>Pages:</strong> ${pageCount}</li>` : ''}
								${size ? `<li><strong>Size:</strong> ${this.escape(this.formatFileSize(size))}</li>` : ''}
								${uploadedAt ? `<li><strong>Uploaded:</strong> ${this.escape(this.formatRelativeDate(uploadedAt))}</li>` : ''}
								${confidence ? `<li><strong>Confidence:</strong> ${this.escape(confidence)}</li>` : ''}
							</ul>
						</div>
					</div>
					<div class="doc-actions">
						<button class="btn btn-danger" data-action="remove-document" data-document-id="${this.escapeAttr(id)}">Remove</button>
					</div>
				</div>
			</li>
		`;
	}

	toggleFactRow(factKey) {
		if (!factKey) {
			return;
		}

		if (this.expandedFacts.has(factKey)) {
			this.expandedFacts.delete(factKey);
		} else {
			this.expandedFacts.add(factKey);
		}
		this.refresh();
	}

	async handleRemoveDocument(documentId) {
		if (!documentId || !this.analysis?.id) {
			return;
		}

		const doc = this.documents.find(d => (d.id || d.Id || '').toString() === documentId.toString());
		const name = doc?.originalFileName || doc?.fileName || 'this document';
		const confirmed = window.confirm(`Remove ${name}? This cannot be undone.`);
		if (!confirmed) {
			return;
		}

		try {
			await this.api.deleteDocument(this.analysis.id, documentId);
			this.toast.success('Document removed');
			await this.reload();
		} catch (error) {
			console.error('Failed to remove document', error);
			this.toast.error('Failed to remove document');
		}
	}

	async reload() {
		if (!this.analysis?.id) {
			return;
		}

		const id = this.analysis.id;
		await this.load(id);
		this.refresh();
	}

	async handleRefreshAnalysis() {
		if (!this.analysis?.id || this.isRefreshingPipeline) {
			return;
		}

		this.isRefreshingPipeline = true;
		this.refresh();

		try {
			this.toast.info('Scheduling refresh...');
			const response = await this.api.refreshPipeline(this.analysis.id);
			const jobId = response?.jobId || response?.JobId;
			const rawDocumentCount = response?.documentCount ?? response?.DocumentCount;
			const documentCount = typeof rawDocumentCount === 'number'
				? rawDocumentCount
				: Number.parseInt(rawDocumentCount, 10);

			if (Number.isFinite(documentCount) && documentCount > 0) {
				const label = documentCount === 1 ? 'document' : 'documents';
				this.toast.info(`Queued ${documentCount} ${label} for refresh`);
			}

			if (jobId) {
				await this.api.waitForJob(this.analysis.id, jobId);
			}

			this.toast.success('Refresh completed');
			this.isRefreshingPipeline = false;
			await this.reload();
		} catch (error) {
			this.isRefreshingPipeline = false;

			const errorMessage = error?.data?.error || error?.message;
			if (error?.status === 400 && errorMessage) {
				this.toast.warning(errorMessage);
			} else {
				this.toast.error('Failed to refresh analysis');
			}
			console.error('Failed to refresh analysis', error);
			this.refresh();
		}
	}

	async handleReprocessPipeline() {
		if (!this.analysis?.id) {
			return;
		}

		const confirmed = window.confirm(
			'Reprocess this analysis? This will regenerate the deliverable with the latest merge logic and may take a few moments.'
		);

		if (!confirmed) {
			return;
		}

		try {
			this.toast.info('Reprocessing pipeline...');
			
			// Trigger reprocess by updating notes with reProcess=true flag
			await this.api.setNotes(
				this.analysis.id, 
				this.notes || '', 
				true // reProcess flag
			);

			this.toast.success('Reprocess started. Refreshing...');
			
			// Wait a moment for the job to start, then reload
			await new Promise(resolve => setTimeout(resolve, 2000));
			await this.reload();
		} catch (error) {
			console.error('Failed to reprocess pipeline', error);
			this.toast.error('Failed to start reprocess');
		}
	}

	renderOverviewFactsPane() {
		const facts = this.buildOverviewFacts();
		const list = facts.map(fact => this.renderOverviewFactItem(fact)).join('');
		const hasDeliverable = Boolean(this.deliverable);

		return `
			<div class="overview-pane overview-facts">
				<div class="pane-header">
					<h2>Facts</h2>
					${hasDeliverable ? '<div class="pane-actions"><button class="btn btn-secondary" data-action="download-deliverable">Download</button></div>' : ''}
				</div>
				<div class="pane-body fact-pane-body">
					${list ? `<ul class="overview-facts-list">${list}</ul>` : `<div class="pane-empty"><p>Facts will appear after processing completes.</p></div>`}
				</div>
			</div>
		`;
	}

	buildOverviewFacts(limit = 12) {
		const canonical = this.deliverableCanonical;
		if (!canonical) {
			return [];
		}

		const rawDataJson = canonical.dataJson || canonical.DataJson;
		let parsedData = null;
		if (rawDataJson) {
			try {
				parsedData = typeof rawDataJson === 'string' ? JSON.parse(rawDataJson) : rawDataJson;
			} catch (error) {
				console.warn('Failed to parse dataJson payload', error);
			}
		}

		let resolvedFactsSource = canonical.resolvedFacts || canonical.ResolvedFacts;
		let evidenceSource = canonical.evidence || canonical.Evidence;
		let fields = canonical.fields || canonical.Fields;

		if (!resolvedFactsSource && parsedData) {
			resolvedFactsSource = parsedData.resolvedFacts || parsedData.ResolvedFacts;
		}

		if (!evidenceSource && parsedData) {
			evidenceSource = parsedData.evidence || parsedData.Evidence;
		}

		if (!fields && parsedData) {
			fields = parsedData.fields || parsedData.Fields;
		}

		if (!fields || typeof fields !== 'object') {
			return [];
		}

		const buildLookup = (source) => {
			if (!source || typeof source !== 'object') {
				return () => null;
			}

			const map = new Map();
			Object.entries(source).forEach(([name, value]) => {
				if (typeof name !== 'string') {
					return;
				}
				if (!map.has(name)) {
					map.set(name, value);
				}
				const lower = name.toLowerCase();
				if (!map.has(lower)) {
					map.set(lower, value);
				}
			});

			return (lookupKey) => {
				if (!lookupKey) {
					return null;
				}
				if (map.has(lookupKey)) {
					return map.get(lookupKey);
				}
				const lower = lookupKey.toLowerCase();
				return map.has(lower) ? map.get(lower) : null;
			};
		};

		const getResolved = buildLookup(resolvedFactsSource);
		const getEvidence = buildLookup(evidenceSource);

		const entries = Object.entries(fields).map(([key, raw]) => {
			const normalized = raw && typeof raw === 'object' && !Array.isArray(raw)
				? raw
				: { value: raw };

			const baseValue = normalized.value ?? normalized.text ?? normalized;
			const resolved = getResolved(key);
			const evidenceData = resolved?.evidence || getEvidence(key);

			const displayValue = typeof resolved?.displayText === 'string' && resolved.displayText.trim().length > 0
				? resolved.displayText
				: this.formatFactValue(baseValue);

			const valueHtml = typeof resolved?.displayHtml === 'string' && resolved.displayHtml.trim().length > 0
				? resolved.displayHtml
				: null;

			const footnotes = Array.isArray(resolved?.footnotes)
				? resolved.footnotes.map(item => {
					const rawIndex = item?.index ?? item?.Index;
					const numericIndex = typeof rawIndex === 'number' ? rawIndex : Number(rawIndex);
					if (!Number.isFinite(numericIndex)) {
						return null;
					}
					const rawContent = item?.content ?? item?.Content ?? '';
					return {
						index: numericIndex,
						content: typeof rawContent === 'string' ? rawContent : String(rawContent ?? '')
					};
				}).filter(Boolean)
				: [];
			if (footnotes.length > 1) {
				footnotes.sort((a, b) => a.index - b.index);
			}

			const confidenceRaw = resolved?.confidence
				?? evidenceData?.confidence
				?? normalized.confidence
				?? normalized.Confidence
				?? normalized.confidenceScore
				?? normalized.ConfidenceScore;
			const confidencePercent = this.normalizeConfidencePercent(confidenceRaw);

			const conflictsRaw = normalized.conflicts ?? normalized.Conflicts ?? [];
			const conflictCount = Array.isArray(conflictsRaw) ? conflictsRaw.length : (Number(conflictsRaw) || 0);

			const sourceCandidates = new Set();
			const addSourceCandidate = (candidate, fallbackLabel) => {
				if (candidate == null) {
					return;
				}
				let value = candidate;
				if (typeof value === 'object') {
					const label = value.name ?? value.label ?? value.title ?? value.id ?? fallbackLabel;
					value = label != null ? label : JSON.stringify(value);
				}
				const text = typeof value === 'string' ? value.trim() : String(value ?? '').trim();
				if (!text) {
					return;
				}
				sourceCandidates.add(text);
			};

			const sourcesRaw = normalized.sources ?? normalized.Sources;
			if (Array.isArray(sourcesRaw)) {
				sourcesRaw.forEach(item => addSourceCandidate(item));
			} else if (sourcesRaw != null) {
				addSourceCandidate(sourcesRaw);
			}

			if (Array.isArray(evidenceData?.sources)) {
				evidenceData.sources.forEach(item => addSourceCandidate(item));
			}
			if (evidenceData?.sourceId) {
				addSourceCandidate(evidenceData.sourceId);
			}
			if (evidenceData?.sourceFileName) {
				const pageQualifier = evidenceData.page ? `#${evidenceData.page}` : '';
				addSourceCandidate(`${evidenceData.sourceFileName}${pageQualifier}`);
			}

			if (footnotes.length > 0) {
				footnotes.forEach(item => addSourceCandidate(`footnote-${item.index}`, item.content));
			}

			const sourceCount = sourceCandidates.size;

			const authoritativeFlag = normalized.isAuthoritative ?? normalized.authoritative ?? normalized.Authoritative ?? false;
			const origin = normalized.origin ?? normalized.Origin ?? '';
			const authoritative = Boolean(authoritativeFlag || (typeof origin === 'string' && origin.toLowerCase().includes('authoritative')));

			return {
				key,
				label: evidenceData?.metadata?.fieldDisplayName || this.formatFactLabel(key),
				value: displayValue,
				valueHtml,
				footnotes,
				confidencePercent,
				confidenceText: confidencePercent != null ? `${Math.round(confidencePercent)}%` : null,
				conflictCount,
				sourceCount,
				authoritative,
				sourceFileName: evidenceData?.sourceFileName,
				page: evidenceData?.page,
				section: evidenceData?.section,
				reasoning: evidenceData?.metadata?.factReasoning,
				supportingFacts: evidenceData?.metadata?.supportingFacts,
				mergeStrategy: evidenceData?.metadata?.mergeStrategy,
				span: evidenceData?.span,
				evidenceSummary: typeof resolved?.evidenceSummary === 'string' ? resolved.evidenceSummary : null
			};
		}).filter(entry => entry.value);

		entries.sort((a, b) => {
			const aScore = (a.authoritative ? 1000 : 0) + (a.confidencePercent ?? 0);
			const bScore = (b.authoritative ? 1000 : 0) + (b.confidencePercent ?? 0);
			if (aScore === bScore) {
				return a.label.localeCompare(b.label);
			}
			return bScore - aScore;
		});

		return entries.slice(0, limit);
	}

	renderOverviewFactItem(fact) {
		if (!fact) {
			return '';
		}

		const expanded = this.expandedFacts.has(fact.key);
		const badges = [];
		if (fact.conflictCount > 0) {
			badges.push(`<span class="fact-pill pill-alert">${this.escape(`${fact.conflictCount} conflict${fact.conflictCount === 1 ? '' : 's'}`)}</span>`);
		}
		if (fact.authoritative) {
			badges.push('<span class="fact-pill pill-authoritative">Authoritative</span>');
		}

		const summaryValue = fact.value != null && String(fact.value).trim().length > 0
			? String(fact.value)
			: '--';
		const resolvedValueHtml = typeof fact.valueHtml === 'string' && fact.valueHtml.trim().length > 0
			? fact.valueHtml
			: null;
		const summaryHtml = resolvedValueHtml ?? this.escape(summaryValue).replace(/\n/g, '<br />');
		const badgesHtml = badges.length ? `<div class="fact-badges">${badges.join('')}</div>` : '';

		const confidencePercent = Number.isFinite(fact.confidencePercent) ? fact.confidencePercent : null;
		const confidenceClass = this.resolveConfidenceIntent(confidencePercent);
		const confidenceLabel = fact.confidenceText ?? (confidencePercent != null ? `${Math.round(confidencePercent)}%` : null);
		const confidenceIndicatorHtml = confidenceClass && confidenceLabel
			? `<span class="fact-confidence-indicator" title="Confidence ${this.escape(confidenceLabel)}">
				<span class="fact-confidence-dot ${confidenceClass}" aria-hidden="true"></span>
				<span class="sr-only">Confidence ${this.escape(confidenceLabel)}</span>
			</span>`
			: '';
		const labelHtml = `<span class="fact-label">${confidenceIndicatorHtml}<span class="fact-label-text">${this.escape(fact.label)}</span></span>`;

		// Build enhanced metadata section
		const metadataLines = [];
		
		if (fact.sourceFileName) {
			let sourceInfo = this.escape(fact.sourceFileName);
			if (fact.page) {
				sourceInfo += ` (p. ${fact.page})`;
			}
			if (fact.section) {
				sourceInfo += ` - ${this.escape(fact.section)}`;
			}
			metadataLines.push(`<div class="fact-source"><strong>Source:</strong> ${sourceInfo}</div>`);
		}
		
		if (fact.reasoning) {
			metadataLines.push(`<div class="fact-reasoning"><strong>Reasoning:</strong> ${this.escape(fact.reasoning)}</div>`);
		}
		if (fact.evidenceSummary) {
			const evidenceParts = fact.evidenceSummary
				.split('|')
				.map(part => part.trim())
				.filter(Boolean)
				.filter(part => {
					const lower = part.toLowerCase();
					if (fact.reasoning && lower === fact.reasoning.trim().toLowerCase()) {
						return false;
					}
					if (fact.sourceFileName && part.startsWith(fact.sourceFileName)) {
						return false;
					}
					return true;
				});
			if (evidenceParts.length > 0) {
				const evidenceSummaryHtml = evidenceParts
					.map(part => this.escape(part).replace(/\n/g, '<br />'))
					.join('<br />');
				metadataLines.push(`<div class="fact-evidence"><strong>Evidence:</strong> ${evidenceSummaryHtml}</div>`);
			}
		}
		
		const additionalInfo = [];
		if (fact.supportingFacts) {
			additionalInfo.push(`${fact.supportingFacts} supporting fact${fact.supportingFacts === 1 ? '' : 's'}`);
		}
		if (fact.mergeStrategy) {
			additionalInfo.push(`Strategy: ${fact.mergeStrategy}`);
		}
		if (fact.span) {
			const spanStart = fact.span.Start ?? fact.span.start ?? '';
			const spanEnd = fact.span.End ?? fact.span.end ?? '';
			if (spanStart || spanEnd) {
				const startLabel = spanStart || '--';
				const endLabel = spanEnd || '--';
				additionalInfo.push(`Span: ${startLabel} - ${endLabel}`);
			}
		}
		
		if (additionalInfo.length > 0) {
			metadataLines.push(`<div class="fact-technical"><strong>Details:</strong> ${this.escape(additionalInfo.join(' | '))}</div>`);
		}
		if (Array.isArray(fact.footnotes) && fact.footnotes.length > 0) {
			const footnoteItems = fact.footnotes.map(footnote => {
				const indexLabel = this.escape(footnote.index ?? footnote.Index ?? '?');
				const contentHtml = this.escape(footnote.content || footnote.Content || '').replace(/\n/g, '<br />');
				return `
					<div class="fact-footnote-entry">
						<span class="fact-footnote-index">[${indexLabel}]</span>
						<span class="fact-footnote-text">${contentHtml}</span>
					</div>
				`;
			}).join('');
			metadataLines.push(`<div class="fact-footnotes"><strong>Footnotes:</strong><div class="fact-footnote-list">${footnoteItems}</div></div>`);
		}

		const metadataHtml = metadataLines.length > 0 ? `<div class="fact-metadata">${metadataLines.join('')}</div>` : '';

		const metrics = [];
		if (fact.confidenceText) {
			metrics.push(`
				<div class="fact-detail">
					<dt>Confidence</dt>
					<dd>${this.escape(fact.confidenceText)}</dd>
				</div>
			`);
		}
		if (fact.sourceCount != null) {
			const sourceLabel = fact.sourceCount === 1 ? 'Source' : 'Sources';
			const sourceValue = fact.sourceCount > 0 ? `${fact.sourceCount}` : 'None';
			metrics.push(`
				<div class="fact-detail">
					<dt>${this.escape(sourceLabel)}</dt>
					<dd>${this.escape(sourceValue)}</dd>
				</div>
			`);
		}
		if (fact.conflictCount != null) {
			const conflictValue = fact.conflictCount > 0
				? `${fact.conflictCount}`
				: 'None';
			metrics.push(`
				<div class="fact-detail">
					<dt>Conflicts</dt>
					<dd>${this.escape(conflictValue)}</dd>
				</div>
			`);
		}
		if (fact.authoritative) {
			metrics.push(`
				<div class="fact-detail">
					<dt>Authority</dt>
					<dd>Authoritative</dd>
				</div>
			`);
		}

		const metricsHtml = metrics.length > 0
			? `<dl class="fact-detail-list">${metrics.join('')}</dl>`
			: '';

		const detailSections = [];
		if (metricsHtml) {
			detailSections.push(metricsHtml);
		}
		if (metadataHtml) {
			detailSections.push(metadataHtml);
		}
		if (detailSections.length === 0) {
			detailSections.push('<div class="fact-metadata fact-metadata-empty">No additional context available.</div>');
		}

		const detailsHtml = detailSections.join('');
		const chevronHtml = `<span class="row-chevron" aria-hidden="true">${this.renderRowChevron(expanded)}</span>`;

		return `
			<li class="fact-row ${expanded ? 'expanded' : ''}" data-fact="${this.escapeAttr(fact.key)}">
				<button class="fact-row-toggle" data-action="toggle-fact" data-fact="${this.escapeAttr(fact.key)}" aria-expanded="${expanded}" aria-controls="fact-details-${this.escapeAttr(fact.key)}">
					<div class="fact-summary">
						${labelHtml}
						<div class="fact-primary-value">${summaryHtml}</div>
						${badgesHtml}
					</div>
					${chevronHtml}
				</button>
				<div class="fact-row-details" id="fact-details-${this.escapeAttr(fact.key)}" ${expanded ? '' : 'hidden'}>
					${detailsHtml}
				</div>
			</li>
		`;
	}

	estimateFactCount() {
		const canonical = this.deliverableCanonical;
		if (!canonical) {
			return 0;
		}

		const fields = canonical.fields || canonical.Fields;
		if (!fields || typeof fields !== 'object') {
			return 0;
		}

		return Object.keys(fields).length;
	}

	countAuthoritativeOverrides() {
		const overrides = this.parseAuthoritativeNotes(this.notes || '');
		return Object.keys(overrides).length;
	}

	formatFactLabel(key) {
		return (key || '')
			.replace(/[._-]+/g, ' ')
			.replace(/([a-z])([A-Z])/g, '$1 $2')
			.replace(/\s+/g, ' ')
			.trim()
			.replace(/^./, char => char.toUpperCase());
	}

	formatFactValue(raw) {
		if (raw == null) {
			return '';
		}
		if (typeof raw === 'string' || typeof raw === 'number' || typeof raw === 'boolean') {
			return String(raw);
		}
		if (Array.isArray(raw)) {
			return raw.map(item => this.formatFactValue(item)).filter(Boolean).join(', ');
		}
		if (typeof raw === 'object') {
			if (raw.text != null) {
				return this.formatFactValue(raw.text);
			}
			if (raw.value != null) {
				return this.formatFactValue(raw.value);
			}
			if (Array.isArray(raw.values)) {
				return raw.values.map(item => this.formatFactValue(item)).filter(Boolean).join(', ');
			}
		}
		return '';
	}

	normalizeConfidencePercent(value) {
		if (value == null || value === '') {
			return null;
		}

		const numeric = Number(value);
		if (!Number.isFinite(numeric)) {
			return null;
		}

		const percent = numeric <= 1 ? numeric * 100 : numeric;
		const clamped = Math.max(0, Math.min(100, Math.round(percent)));
		return clamped;
	}

		resolveConfidenceIntent(percent) {
			if (percent == null || !Number.isFinite(percent)) {
				return null;
			}
			if (percent >= 90) {
				return 'confidence-strong';
			}
			if (percent >= 60) {
				return 'confidence-medium';
			}
			return 'confidence-low';
		}

	renderSelectedFiles() {
		if (!this.filesToUpload.length) {
			return `
				<div class="documents-empty-state">
					<p>No files selected yet.</p>
				</div>
			`;
		}

		return `
			<ul class="selected-files">
				${this.filesToUpload.map(file => `
					<li>
						<span class="file-name">${this.escape(file.name)}</span>
						<span class="file-size">${this.escape(this.formatFileSize(file.size))}</span>
					</li>
				`).join('')}
			</ul>
		`;
	}

	renderDropZone(options = {}) {
		const { label, helperText, compact } = options;
		const resolvedLabel = label || (this.isCreating ? 'Drop documents to queue uploads' : 'Drop documents to upload');
		const resolvedHelper = helperText || 'Supported: PDF, DOCX, DOC, TXT — up to 200 MB';
		const classes = ['documents-dropzone'];
		if (compact) {
			classes.push('documents-dropzone-compact');
		}

		return `
			<div class="${classes.join(' ')}" data-dropzone="documents" tabindex="0">
				<svg class="icon" width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
					<path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path>
					<polyline points="14 2 14 8 20 8"></polyline>
					<line x1="12" y1="11" x2="12" y2="17"></line>
					<polyline points="9 14 12 17 15 14"></polyline>
				</svg>
				<div class="dropzone-text">
					<strong>${this.escape(resolvedLabel)}</strong>
					<span>${this.escape(resolvedHelper)}</span>
					<button class="btn btn-link" data-action="open-file-picker">Browse files</button>
				</div>
			</div>
		`;
	}

	renderConfigurationTab() {
		const analysisTypeDisplay = this.resolveAnalysisTypeName(this.analysis?.analysisTypeId) || this.analysis?.analysisTypeName || 'Not set';

		return `
			<section class="configuration-grid">
				<div class="config-card">
					<div class="section-heading">
						<h2>Identity</h2>
					</div>
					<div class="config-inline-hint">Use the inline fields in the summary above to edit identity details.</div>
					<dl class="identity-summary">
						<div>
							<dt>Name</dt>
							<dd>${this.escape(this.analysis?.name || 'Untitled analysis')}</dd>
						</div>
						<div>
							<dt>Description</dt>
							<dd>${this.escape(this.analysis?.description || 'No description')}</dd>
						</div>
						<div>
							<dt>Type</dt>
							<dd>${this.escape(analysisTypeDisplay)}</dd>
						</div>
					</dl>
					${!this.isCreating && this.analysis?.analysisTypeId ? `<a class="btn btn-link" href="#/analysis-types/${this.escapeAttr(this.analysis.analysisTypeId)}/view">View type definition →</a>` : ''}
				</div>

				<div class="config-card">
					<div class="section-heading">
						<h2>Authoritative Notes</h2>
					</div>
					<textarea rows="10" data-field="notes" data-save="${this.isCreating ? 'local' : 'notes'}" placeholder="Add authoritative information in natural language...\nPRIMARY CONTACT: Jordan Kim is the VP of Enterprise Solutions\nREVENUE: FY2024 revenue was $52.3M USD\nEMPLOYEE COUNT: 175 employees as of October 2024">${this.escape(this.notes)}</textarea>
					<p class="field-hint">Notes override document facts and auto-save when you leave the field.</p>
				</div>

				<div class="config-card meta-card">
					<div class="section-heading">
						<h2>Metadata</h2>
					</div>
					${this.renderMetadataList()}
					${!this.isCreating ? `<button class="btn btn-danger" data-action="delete-analysis">Delete analysis</button>` : ''}
				</div>
			</section>
		`;
	}

	renderMetadataList() {
		if (this.isCreating) {
			return `<p class="meta-placeholder">Metadata appears after creation.</p>`;
		}

		const items = [];
		const id = this.analysis?.id;
		if (id) {
			items.push(['ID', id]);
		}

		const created = this.formatTimestamp(this.analysis?.createdAt);
		if (created) {
			items.push(['Created', created]);
		}

		const updated = this.formatTimestamp(this.analysis?.updatedAt || this.analysis?.lastUpdated);
		if (updated) {
			items.push(['Updated', updated]);
		}

		const status = this.formatPipelineStatus(this.analysis?.status)?.label;
		if (status) {
			items.push(['Status', status]);
		}

		return `
			<dl class="meta-list">
				${items.map(([label, value]) => `
					<div class="meta-row">
						<dt>${this.escape(label)}</dt>
						<dd>${this.escape(value)}</dd>
					</div>
				`).join('')}
			</dl>
		`;
	}

	attachEventHandlers(host) {
		if (!host) {
			return;
		}

		if (this.host !== host) {
			this.detachEventHandlers();
			this.host = host;
			this.boundHandlers.click = this.onClick.bind(this);
			this.boundHandlers.change = this.onChange.bind(this);
			this.boundHandlers.input = this.onInput.bind(this);
			this.boundHandlers.keydown = this.onKeyDown.bind(this);
			this.boundHandlers.focusout = this.onFocusOut.bind(this);
			this.boundHandlers.dragover = this.onDragOver.bind(this);
			this.boundHandlers.dragleave = this.onDragLeave.bind(this);
			this.boundHandlers.drop = this.onDrop.bind(this);

			host.addEventListener('click', this.boundHandlers.click);
			host.addEventListener('change', this.boundHandlers.change);
			host.addEventListener('input', this.boundHandlers.input);
			host.addEventListener('keydown', this.boundHandlers.keydown);
			host.addEventListener('focusout', this.boundHandlers.focusout);
			host.addEventListener('dragover', this.boundHandlers.dragover);
			host.addEventListener('dragleave', this.boundHandlers.dragleave);
			host.addEventListener('drop', this.boundHandlers.drop);
		}
	}

	detachEventHandlers() {
		if (!this.host) {
			return;
		}

		Object.entries(this.boundHandlers).forEach(([event, handler]) => {
			if (handler && typeof handler === 'function') {
				this.host.removeEventListener(event, handler);
			}
		});

		this.boundHandlers = {};
		this.host = null;
	}

		onClick(event) {
		const tab = event.target.closest('.analysis-tab[data-tab]');
		if (tab && !tab.hasAttribute('aria-disabled')) {
			const tabId = tab.dataset.tab;
			this.setActiveTab(tabId);
			return;
		}

		const inlineTrigger = event.target.closest('[data-inline-trigger]');
		if (inlineTrigger) {
			const field = inlineTrigger.dataset.inlineTrigger;
			this.beginInlineEdit(field);
			return;
		}

		const actionEl = event.target.closest('[data-action]');
		if (!actionEl) {
			return;
		}

		const action = actionEl.dataset.action;

		switch (action) {
			case 'back':
				window.history.back();
				break;
			case 'create-analysis':
				this.handleCreate();
				break;
			case 'cancel-create':
				this.eventBus.emit('navigate', 'analyses-list');
				break;
			case 'refresh-analysis':
				this.handleRefreshAnalysis();
				break;
			case 'reprocess-pipeline':
				this.handleReprocessPipeline();
				break;
			case 'save-pipeline':
				this.savePendingPipelineUpdates();
				break;
			case 'download-deliverable':
				this.handleDownloadDeliverable();
				break;
			case 'export-report':
				this.handleExportReport();
				break;
			case 'open-file-picker':
				this.openFilePicker();
				break;
			case 'delete-analysis':
				this.handleDelete();
				break;
			case 'toggle-document':
				this.toggleDocumentRow(actionEl.dataset.documentId);
				break;
			case 'toggle-fact':
				this.toggleFactRow(actionEl.dataset.fact);
				break;
			case 'remove-document':
				this.handleRemoveDocument(actionEl.dataset.documentId);
				break;
			default:
				break;
		}
	}

	onChange(event) {
		const field = event.target.dataset.field;
		if (field) {
			this.handleFieldChange(field, event.target.value, event.target.dataset.save);
			return;
		}

		const inlineField = event.target.dataset.inlineInput;
		if (inlineField) {
			this.inlineEdit.draft = event.target.value;
			this.finalizeInlineEdit(inlineField, event.target.value);
			return;
		}

		if (event.target.dataset.action === 'document-type') {
			const docId = event.target.dataset.documentId;
			const nextValue = event.target.value;
			this.handleDocumentTypeOverride(docId, nextValue, event.target);
		}
	}

	onInput(event) {
		if (event.target.dataset.inlineInput) {
			this.inlineEdit.draft = event.target.value;
			return;
		}

		if (event.target.dataset.field && event.target.dataset.save === 'local') {
			const field = event.target.dataset.field;
			if (field === 'name' || field === 'description' || field === 'analysisTypeId') {
				this.analysis[field] = event.target.value;
			} else if (field === 'notes') {
				this.notes = event.target.value;
			}
		}
	}

	onKeyDown(event) {
		if (event.key === 'Enter' && event.target.matches('.analysis-tabs .analysis-tab')) {
			const tabId = event.target.dataset.tab;
			if (tabId) {
				this.setActiveTab(tabId);
			}
		}

		if ((event.key === 'Enter' || event.key === ' ') && event.target.matches('[data-dropzone="documents"]')) {
			event.preventDefault();
			this.openFilePicker();
		}

		if ((event.key === 'Enter' || event.key === ' ') && event.target.dataset?.inlineTrigger) {
			event.preventDefault();
			this.beginInlineEdit(event.target.dataset.inlineTrigger);
			return;
		}

		const inlineField = event.target.dataset?.inlineInput;
		if (inlineField) {
			if (event.key === 'Escape') {
				event.preventDefault();
				this.cancelInlineEdit();
				return;
			}
			if (event.key === 'Enter' && inlineField !== 'description') {
				event.preventDefault();
				this.finalizeInlineEdit(inlineField, event.target.value);
			}
		}
	}

	onDragOver(event) {
		const dropzone = event.target.closest('[data-dropzone="documents"]');
		if (!dropzone) {
			return;
		}

		event.preventDefault();
		dropzone.classList.add('drag-over');
	}

	onDragLeave(event) {
		const dropzone = event.target.closest('[data-dropzone="documents"]');
		if (dropzone) {
			dropzone.classList.remove('drag-over');
		}
	}

	onFocusOut(event) {
		const inlineField = event.target.dataset?.inlineInput;
		if (inlineField && this.inlineEdit.activeField === inlineField) {
			this.finalizeInlineEdit(inlineField, event.target.value);
		}
	}

	onDrop(event) {
		const dropzone = event.target.closest('[data-dropzone="documents"]');
		if (!dropzone) {
			return;
		}

		event.preventDefault();
		dropzone.classList.remove('drag-over');

		const files = Array.from(event.dataTransfer?.files || []).filter(file => file && file.size > 0);
		if (files.length === 0) {
			return;
		}

		this.handleFileSelection(files);
	}

	setActiveTab(tabId) {
		if (!tabId || this.activeTab === tabId) {
			return;
		}

		this.activeTab = tabId;
		this.refresh();
	}

	refresh() {
		if (!this.host) {
			return;
		}

		const html = this.render();
		this.host.innerHTML = html;
	}

	async handleCreate() {
		const payload = this.collectCreatePayload();
		if (!payload.name.trim()) {
			this.toast.error('Please provide a name for the analysis');
			return;
		}

		try {
			const pipeline = await this.api.createPipeline(payload);
			const pipelineId = pipeline.id || pipeline.Id;
			this.toast.success(`Analysis "${payload.name}" created`);

			if (this.notes.trim()) {
				try {
					await this.api.setNotes(pipelineId, this.notes, false);
				} catch (error) {
					console.warn('Failed to set notes during creation', error);
				}
			}

			if (this.filesToUpload.length > 0) {
				await this.uploadQueuedFiles(pipelineId);
			}

			this.eventBus.emit('navigate', 'analysis-view', { id: pipelineId });
		} catch (error) {
			console.error('Failed to create analysis', error);
			this.toast.error('Failed to create analysis');
		}
	}

	collectCreatePayload() {
		return {
			name: this.analysis?.name?.trim() || '',
			description: this.analysis?.description?.trim() || '',
			analysisTypeId: this.analysis?.analysisTypeId || null
		};
	}

	async uploadQueuedFiles(pipelineId) {
		for (const file of this.filesToUpload) {
			await this.uploadSingleFile(pipelineId, file);
		}

		this.toast.success(`${this.filesToUpload.length} file(s) uploaded`);
	}

	async handleFieldChange(field, value, saveMode) {
		if (saveMode === 'local') {
			if (field === 'analysisTypeId') {
				this.analysis.analysisTypeId = value;
				this.analysis.analysisTypeName = this.resolveAnalysisTypeName(value);
			} else {
				this.analysis[field] = value;
			}
			return;
		}

		if (!this.analysis?.id) {
			return;
		}

		if (saveMode === 'notes') {
			this.notes = value;
			await this.saveNotes(value);
			return;
		}

		if (saveMode === 'pipeline') {
			await this.updatePipelineField(field, value);
		}
	}

	async handleDocumentTypeOverride(documentId, nextValue, selectEl) {
		if (!this.analysis?.id || !documentId || !nextValue) {
			return;
		}

		if (nextValue === selectEl.dataset.currentValue) {
			return;
		}

		selectEl.disabled = true;

		try {
			await this.api.overrideDocumentType(this.analysis.id, documentId, {
				typeId: nextValue,
				confidence: 1.0
			});

			this.toast.success('Document type updated');
			selectEl.dataset.currentValue = nextValue;
			await this.reload();
		} catch (error) {
			console.error('Failed to override document type', error);
			this.toast.error('Failed to update document type');
			selectEl.value = selectEl.dataset.currentValue || '';
		} finally {
			selectEl.disabled = false;
		}
	}

	openFilePicker() {
		if (!this.host) {
			return;
		}

		const input = this.host.querySelector('input[data-file-input]');
		if (!input) {
			return;
		}

		input.value = '';
		input.addEventListener('change', () => {
			if (input.files && input.files.length > 0) {
				this.handleFileSelection(Array.from(input.files));
			}
		}, { once: true });

		input.click();
	}

	handleFileSelection(files) {
		if (!Array.isArray(files) || files.length === 0) {
			return;
		}

		if (this.isCreating) {
			const existingKeys = new Set(this.filesToUpload.map(file => this.buildFileKey(file)));
			files.forEach(file => {
				const key = this.buildFileKey(file);
				if (!existingKeys.has(key)) {
					this.filesToUpload.push(file);
					existingKeys.add(key);
				}
			});
			this.refresh();
			return;
		}

		if (!this.analysis?.id) {
			return;
		}

		files.forEach(file => {
			this.uploadSingleFile(this.analysis.id, file).catch(error => {
				console.error('File upload failed', error);
			});
		});
	}

	async uploadSingleFile(pipelineId, file) {
		this.toast.info(`Uploading ${file.name}...`);

		try {
			const result = await this.api.uploadDocument(pipelineId, file);
			const jobId = result?.jobId || result?.JobId;

			if (jobId) {
				this.toast.info('Processing document...');
				await this.api.waitForJob(pipelineId, jobId);
			}

			this.toast.success(`${file.name} processed`);
			await this.reload();
		} catch (error) {
			console.error('Failed to upload document', error);
			this.toast.error(`Failed to upload ${file.name}`);
		}
	}

	async saveNotes(notes) {
		if (!this.analysis?.id) {
			return;
		}

		try {
			await this.api.setNotes(this.analysis.id, notes, false);
			this.notes = notes;
			this.toast.success('Notes saved');
		} catch (error) {
			console.error('Failed to save notes', error);
			this.toast.error('Failed to save notes');
		}
	}

	async savePendingPipelineUpdates() {
		if (!this.analysis?.id) {
			return;
		}

		const updates = { ...this.pendingPipelineUpdates };
		if (this.pendingAnalysisType !== null && this.pendingAnalysisType !== undefined) {
			updates.analysisTypeId = this.pendingAnalysisType || '';
		}

		if (Object.keys(updates).length === 0) {
			this.updateDirtyState();
			return;
		}

		const payload = {
			...this.analysis,
			...updates
		};

		try {
			const updated = await this.api.savePipeline(payload);
			this.analysis = this.normalizeAnalysis(updated);
			if (this.analysis && updates.analysisTypeId !== undefined) {
				this.analysis.analysisTypeName = this.resolveAnalysisTypeName(updates.analysisTypeId) || this.analysis.analysisTypeName;
			}
			this.pendingPipelineUpdates = {};
			this.pendingAnalysisType = null;
			this.updateDirtyState();
			this.refresh();
			this.toast.success('Analysis updated');
			await this.reload();
		} catch (error) {
			console.error('Failed to save analysis updates', error);
			this.toast.error('Failed to save changes');
		}
	}

	async handleDownloadDeliverable() {
		if (!this.analysis?.id) {
			this.toast.error('Analysis ID is unavailable');
			return;
		}

		try {
			const markdown = await this.api.getDeliverableMarkdown(this.analysis.id);
			if (!markdown) {
				this.toast.info('Deliverable is empty');
				return;
			}

			const blob = new Blob([markdown], { type: 'text/markdown' });
			const url = URL.createObjectURL(blob);
			const link = document.createElement('a');
			link.href = url;
			link.download = this.buildDeliverableFileName();
			document.body.appendChild(link);
			link.click();
			document.body.removeChild(link);
			URL.revokeObjectURL(url);

			this.toast.success('Deliverable downloaded');
		} catch (error) {
			console.error('Failed to download deliverable', error);
			this.toast.error('Failed to download deliverable');
		}
	}

	async handleExportReport() {
		if (!this.analysis?.id) {
			this.toast.warning('No analysis selected');
			return;
		}

		try {
			const markdown = await this.api.getDeliverableMarkdown(this.analysis.id);
			if (!markdown) {
				this.toast.info('Deliverable is empty');
				return;
			}

			const blob = new Blob([markdown], { type: 'text/markdown' });
			const url = URL.createObjectURL(blob);
			const a = document.createElement('a');
			a.href = url;
			a.download = this.buildDeliverableFileName();
			a.click();
			URL.revokeObjectURL(url);
			this.toast.success('Report exported');
		} catch (error) {
			console.error('Failed to export report', error);
			this.toast.error('Failed to export report');
		}
	}

	async handleDelete() {
		if (!this.analysis?.id) {
			return;
		}

		const name = this.analysis.name || 'this analysis';
		const confirmed = window.confirm(`Delete "${name}"? This cannot be undone.`);
		if (!confirmed) {
			return;
		}

		try {
			await this.api.deletePipeline(this.analysis.id);
			this.toast.success('Analysis deleted');
			this.eventBus.emit('navigate', 'analyses-list');
		} catch (error) {
			console.error('Failed to delete analysis', error);
			this.toast.error('Failed to delete analysis');
		}
	}

	async loadAnalysisTypes() {
		try {
			const types = await this.api.getAnalysisTypes();
			this.analysisTypes = Array.isArray(types) ? types : [];
		} catch (error) {
			console.warn('Failed to load analysis types', error);
			this.analysisTypes = [];
		}
	}

	async loadSourceTypes() {
		try {
			const types = await this.api.getSourceTypes();
			this.sourceTypes = Array.isArray(types) ? types : [];
		} catch (error) {
			console.warn('Failed to load source types', error);
			this.sourceTypes = [];
		}
	}

	normalizeAnalysis(data) {
		if (!data || typeof data !== 'object') {
			return null;
		}

		const id = (data.id || data.Id || '').toString();
		return {
			...data,
			id,
			name: data.name || data.Name || 'Untitled Analysis',
			description: data.description || data.Description || '',
			analysisTypeId: (data.analysisTypeId || data.AnalysisTypeId || '').toString(),
			analysisTypeName: data.analysisTypeName || data.AnalysisTypeName || '',
			status: data.status || data.Status || 'Unknown',
			documentCount: data.documentCount || data.DocumentCount || 0,
			createdAt: data.createdAt || data.CreatedAt || null,
			updatedAt: data.updatedAt || data.UpdatedAt || data.lastUpdated || data.LastUpdated || null
		};
	}

	decorateDocument(doc) {
		if (!doc || typeof doc !== 'object') {
			return doc;
		}

		const clone = { ...doc };
		const sourceTypeId = this.getDocumentSourceTypeId(clone);
		const friendlyName = clone.sourceTypeName || clone.SourceTypeName || this.lookupSourceTypeName(sourceTypeId);
		if (friendlyName) {
			clone.sourceTypeName = friendlyName;
			clone.SourceTypeName = friendlyName;
		}
		return clone;
	}

	buildSourceTypeOptions(selectedId) {
		const normalizedSelected = (selectedId || '').toString();
		const entries = Array.isArray(this.sourceTypes)
			? this.sourceTypes.map(type => {
					const id = (type.id || type.Id || '').toString();
					const name = type.name || type.Name || '';
					return { id, name };
				}).filter(entry => entry.id)
			: [];

		entries.sort((a, b) => {
			const labelA = a.name || this.formatSourceTypeId(a.id);
			const labelB = b.name || this.formatSourceTypeId(b.id);
			return labelA.localeCompare(labelB, undefined, { sensitivity: 'base' });
		});

		const options = [`<option value="">Unclassified</option>`];
		const seen = new Set();

		if (normalizedSelected && !entries.some(entry => entry.id === normalizedSelected)) {
			options.push(`<option value="${this.escapeAttr(normalizedSelected)}" selected>${this.escape(this.formatSourceTypeId(normalizedSelected))}</option>`);
			seen.add(normalizedSelected);
		}

		entries.forEach(entry => {
			if (seen.has(entry.id)) {
				return;
			}
			const label = entry.name || this.formatSourceTypeId(entry.id);
			const selected = normalizedSelected === entry.id ? 'selected' : '';
			options.push(`<option value="${this.escapeAttr(entry.id)}" ${selected}>${this.escape(label)}</option>`);
			seen.add(entry.id);
		});

		return options.join('');
	}

	getDocumentSourceTypeId(doc) {
		const candidates = [
			doc.sourceType,
			doc.SourceType,
			doc.classifiedTypeId,
			doc.ClassifiedTypeId
		];

		for (const candidate of candidates) {
			if (candidate) {
				return candidate.toString();
			}
		}

		return '';
	}

	lookupSourceTypeName(sourceTypeId) {
		const normalized = (sourceTypeId || '').toString();
		if (!normalized) {
			return '';
		}

		const match = Array.isArray(this.sourceTypes)
			? this.sourceTypes.find(type => (type.id || type.Id || '').toString() === normalized)
			: null;

		return match ? (match.name || match.Name || '') : '';
	}

	resolveAnalysisTypeName(typeId) {
		if (!typeId) {
			return '';
		}

		const match = Array.isArray(this.analysisTypes)
			? this.analysisTypes.find(type => (type.id || type.Id || '').toString() === (typeId || '').toString())
			: null;

		return match ? (match.name || match.Name || '') : '';
	}

	formatSourceTypeId(value) {
		return (value || '')
			.replace(/[._-]+/g, ' ')
			.replace(/([a-z])([A-Z])/g, '$1 $2')
			.replace(/\s+/g, ' ')
			.trim()
			.replace(/^./, char => char.toUpperCase());
	}

	formatDocumentStatus(statusValue) {
		const map = {
			0: 'Pending',
			1: 'Extracted',
			2: 'Indexed',
			3: 'Classified',
			4: 'Failed',
			Pending: 'Pending',
			Extracted: 'Extracted',
			Indexed: 'Indexed',
			Classified: 'Classified',
			Failed: 'Failed'
		};

		return map[statusValue] || statusValue || 'Pending';
	}

	formatConfidence(value) {
		if (value == null || value === '') {
			return '';
		}

		const numeric = Number(value);
		if (!Number.isFinite(numeric)) {
			return String(value);
		}

		if (numeric <= 1) {
			return `${Math.round(numeric * 100)}%`;
		}

		return `${Math.round(numeric)}%`;
	}

	formatFileSize(bytes) {
		if (bytes < 1024) {
			return `${bytes} B`;
		}
		if (bytes < 1024 * 1024) {
			return `${(bytes / 1024).toFixed(1)} KB`;
		}
		return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
	}

	getFileIcon(extension) {
		const icons = {
			'pdf': '📄',
			'doc': '📝',
			'docx': '📝',
			'txt': '📃',
			'md': '📋',
			'xls': '📊',
			'xlsx': '📊',
			'csv': '📊',
			'ppt': '📊',
			'pptx': '📊',
			'jpg': '🖼',
			'jpeg': '🖼',
			'png': '🖼',
			'gif': '🖼',
			'zip': '📦',
			'rar': '📦',
			'7z': '📦'
		};
		return icons[extension] || '📄';
	}

	formatTimestamp(value) {
		if (!value) {
			return '';
		}

		try {
			const date = new Date(value);
			if (Number.isNaN(date.getTime())) {
				return String(value);
			}
			return date.toLocaleString();
		} catch (error) {
			return String(value);
		}
	}

	formatPipelineStatus(status) {
		if (!status) {
			return null;
		}

		const raw = status.toString().trim();
		if (!raw) {
			return null;
		}

		const key = raw.toLowerCase();
		const label = raw
			.replace(/([a-z])([A-Z])/g, '$1 $2')
			.replace(/[-_]/g, ' ')
			.replace(/^./, char => char.toUpperCase());

		return { key, label };
	}

	parseAuthoritativeNotes(notes) {
		if (!notes) {
			return {};
		}

		const parsed = {};
		const lines = notes.split('\n');
		lines.forEach(line => {
			const match = line.match(/^([A-Z\s]+):\s*(.+)$/);
			if (match) {
				const key = match[1].trim().toLowerCase().replace(/\s+/g, '_');
				const value = match[2].trim();
				parsed[key] = value;
			}
		});
		return parsed;
	}

	calculateAverageConfidence() {
		const values = this.documents
			.map(doc => Number(doc.classificationConfidence || doc.ClassificationConfidence))
			.filter(Number.isFinite);

		if (!values.length) {
			return null;
		}

		const avg = values.reduce((sum, val) => sum + (val <= 1 ? val * 100 : val), 0) / values.length;
		return Math.round(avg);
	}

	formatRelativeDate(value) {
		if (!value) {
			return 'Just now';
		}

		const date = new Date(value);
		if (Number.isNaN(date.getTime())) {
			return 'Just now';
		}

		const diff = Date.now() - date.getTime();
		const minutes = Math.floor(diff / (1000 * 60));
		if (minutes < 1) {
			return 'Just now';
		}
		if (minutes < 60) {
			return `${minutes} min ago`;
		}
		const hours = Math.floor(minutes / 60);
		if (hours < 24) {
			return `${hours} hr ago`;
		}
		const days = Math.floor(hours / 24);
		if (days < 7) {
			return `${days} day${days === 1 ? '' : 's'} ago`;
		}
		return date.toLocaleDateString();
	}

	normalizeQuality(raw) {
		if (!raw) {
			return null;
		}

		const coerce = (value) => {
			const numeric = typeof value === 'string' ? Number(value) : value;
			return Number.isFinite(numeric) ? numeric : undefined;
		};

		return {
			citationCoverage: coerce(raw.citationCoverage ?? raw.CitationCoverage),
			highConfidence: coerce(raw.highConfidence ?? raw.HighConfidence),
			mediumConfidence: coerce(raw.mediumConfidence ?? raw.MediumConfidence),
			totalConflicts: coerce(raw.totalConflicts ?? raw.TotalConflicts),
			notesSourced: coerce(raw.notesSourced ?? raw.NotesSourced)
		};
	}

	buildFileKey(file) {
		return [file.name, file.size, file.lastModified].join('::');
	}

	buildDeliverableFileName() {
		const name = this.analysis?.name || 'analysis';
		const safe = name.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '') || 'analysis';
		return `${safe}-deliverable.md`;
	}

	renderStatusIcon(key) {
		switch (key) {
			case 'completed':
			case 'complete':
				return '<span class="status-icon">✓</span>';
			case 'processing':
			case 'running':
				return '<span class="status-icon">⏳</span>';
			case 'failed':
				return '<span class="status-icon">⚠️</span>';
			default:
				return '<span class="status-icon">●</span>';
		}
	}

	escape(text) {
		if (text == null) {
			return '';
		}
		const div = document.createElement('div');
		div.textContent = String(text);
		return div.innerHTML;
	}

	escapeAttr(text) {
		if (text == null) {
			return '';
		}
		return String(text)
			.replace(/&/g, '&amp;')
			.replace(/"/g, '&quot;')
			.replace(/'/g, '&#39;')
			.replace(/</g, '&lt;')
			.replace(/>/g, '&gt;');
	}
}


