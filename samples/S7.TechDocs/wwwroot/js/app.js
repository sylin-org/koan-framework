// S7 TechDocs Main Application
class TechDocsApp {
  constructor() {
    this.currentMode = 'browse';
    this.currentDocument = null;
    this.documents = [];
    this.collections = [];
    this.users = [];
    this.searchResults = [];
    
    this.init();
  }

  async init() {
    console.log('🚀 Initializing S7 TechDocs...');
    
    // Initialize auth first
    if (window.auth) {
      window.auth.init();
    }
    
    // Load initial data
    await this.loadCollections();
    await this.loadDocuments();
    
  // Set up event listeners
    this.setupEventListeners();
    
    // Router: support legacy ?view=... and new path routes
    try {
      const url = new URL(window.location);
      const legacyViewId = url.searchParams.get('view');
      if (legacyViewId) {
        // Normalize to /view/:id
        window.history.replaceState({}, '', `/view/${legacyViewId}`);
      }
    } catch {}

    // Handle initial route and back/forward
    this.handleRoute(window.location.pathname);
    window.addEventListener('popstate', () => this.handleRoute(window.location.pathname));
    
    console.log('✅ S7 TechDocs initialized');
  }

  // Simple client-side routing
  navigateTo(path) {
    if (window.location.pathname !== path) {
      window.history.pushState({}, '', path);
    }
    this.handleRoute(path);
  }

  async handleRoute(path) {
    if (!path) path = '/';
    if (path === '/' || path === '/browse') {
      this.setMode('browse');
      return;
    }
    const viewMatch = path.match(/^\/view\/([^\/]+)$/);
    if (viewMatch) {
      const id = decodeURIComponent(viewMatch[1]);
      await this.openViewForDocument(id, /*push*/ false);
      return;
    }
    if (path === '/edit') {
      this.currentDocument = null;
      this.setMode('edit');
      return;
    }
    const editMatch = path.match(/^\/edit\/([^\/]+)$/);
    if (editMatch) {
      const id = decodeURIComponent(editMatch[1]);
      this.currentDocument = this.documents.find(d => d.id === id) || this.currentDocument;
      if (!this.currentDocument) {
        try { this.currentDocument = await window.api.getDocument(id); } catch {}
      }
      this.setMode('edit');
      return;
    }
    if (path === '/moderate') {
      this.setMode('moderate');
      return;
    }
    // Fallback
    this.setMode('browse');
  }

  async loadCollections() {
    try {
      this.collections = await window.api.getCollections();
      this.populateCollectionFilters();
      this.populateCollectionSelects();
    } catch (error) {
      console.error('Failed to load collections:', error);
      window.ui.showToast('Failed to load collections', 'error');
    }
  }

  async loadDocuments() {
    try {
      this.documents = await window.api.getDocuments();
      this.updateDocumentDisplay();
      this.updateModerationQueue();
    } catch (error) {
      console.error('Failed to load documents:', error);
      window.ui.showToast('Failed to load documents', 'error');
    }
  }

  async loadUsers() {
    if (!window.auth.hasRole('Admin')) return;
    
    try {
      this.users = await window.api.getUsers();
      this.updateUsersDisplay();
    } catch (error) {
      console.error('Failed to load users:', error);
      window.ui.showToast('Failed to load users', 'error');
    }
  }

  setupEventListeners() {
    // Search functionality
    const heroSearch = document.getElementById('hero-search');
    if (heroSearch) {
      heroSearch.addEventListener('input', window.ui.debounce((e) => {
        this.performSearch(e.target.value);
      }, 300));
    }

    // Filter changes
    const statusFilters = document.querySelectorAll('.status-filter');
    statusFilters.forEach(filter => {
      filter.addEventListener('change', () => this.applyFilters());
    });

    const collectionFilters = document.querySelectorAll('.collection-filter');
    collectionFilters.forEach(filter => {
      filter.addEventListener('change', () => this.applyFilters());
    });

    // Sort changes
    const sortSelect = document.getElementById('sort-select');
    if (sortSelect) {
      sortSelect.addEventListener('change', () => this.updateDocumentDisplay());
    }

    // Editor content preview
    const docContent = document.getElementById('doc-content');
    if (docContent) {
      docContent.addEventListener('input', window.ui.debounce((e) => {
        this.updateContentPreview(e.target.value);
      }, 500));
    }
  }

  setMode(mode) {
    console.log(`📱 Switching to ${mode} mode`);
    
    this.currentMode = mode;
    
    // Update navigation
    document.querySelectorAll('.mode-nav-item').forEach(item => {
      item.classList.remove('active');
    });
    const activeButton = document.getElementById(`mode-${mode}`);
    if (activeButton) {
      activeButton.classList.add('active');
    }

    // Update content
    document.querySelectorAll('.mode-content').forEach(content => {
      content.classList.remove('active');
    });
    const activeContent = document.getElementById(`${mode}-mode`);
    if (activeContent) {
      activeContent.classList.add('active');
    }

    // Load mode-specific data
    this.loadModeData(mode);

  // Update availability of View button
  this.updateViewNavState();

  // Clear deep-link parameter when leaving view mode
  if (mode !== 'view') {
    try {
      const url = new URL(window.location);
      url.searchParams.delete('view');
      window.history.replaceState({}, '', url);
    } catch {}
  }
  }

  async loadModeData(mode) {
    switch (mode) {
      case 'browse':
        await this.loadDocuments();
        break;
      case 'edit':
        this.setupEditMode();
        break;
      case 'moderate':
        try {
          // Load only submitted items for queue
          const queue = await window.api.getModerationQueue();
          // Merge into documents store for display helpers
          const map = new Map(this.documents.map(d => [d.id, d]));
          queue.forEach(item => map.set(item.id, item));
          this.documents = Array.from(map.values());
        } catch (e) {
          // Fallback: full load
          await this.loadDocuments();
        }
        this.updateModerationQueue();
        // Update pipeline header stats
        try {
          const stats = await window.api.getModerationStats();
          const el = document.getElementById('pipeline-status');
          if (el) el.textContent = `Pipeline: ${stats.submitted} pending • ${stats.approvedToday} approved today • ${stats.denied} returned`;
        } catch {}
        break;
      case 'admin':
        await this.loadUsers();
        this.updateAdminSettings();
        break;
    }
  }

  async performSearch(query) {
    if (!query.trim()) {
      this.searchResults = [];
      this.updateDocumentDisplay();
      return;
    }

    try {
      this.searchResults = await window.api.search(query);
      this.updateSearchResults();
    } catch (error) {
      console.error('Search failed:', error);
      window.ui.showToast('Search failed', 'error');
    }
  }

  updateSearchResults() {
    const grid = document.getElementById('documents-grid');
    const resultCount = document.getElementById('result-count');
    
    if (!grid || !resultCount) return;

    if (this.searchResults.length === 0) {
      grid.innerHTML = '<div class="col-span-full text-center text-gray-400 py-8">No results found</div>';
      resultCount.textContent = 'No results found';
      return;
    }

    grid.innerHTML = '';
    this.searchResults.forEach(result => {
      // Convert search result to document format for display
      const doc = {
        id: result.id,
        title: result.title,
        summary: result.summary,
        collectionId: this.getCollectionIdByName(result.collectionName),
        authorName: result.authorName,
        updatedAt: result.updatedAt,
        rating: result.rating,
        tags: result.tags,
        viewCount: 0, // Not provided in search results
        ratingCount: 0, // Not provided in search results
        status: 'Published' // Assume published for search results
      };
      const card = window.ui.createDocumentCard(doc);
      card.onclick = () => this.openViewForDocument(doc.id);
      grid.appendChild(card);
    });

    resultCount.textContent = `Found ${this.searchResults.length} result${this.searchResults.length !== 1 ? 's' : ''}`;
  }

  applyFilters() {
    const selectedStatuses = Array.from(document.querySelectorAll('.status-filter:checked')).map(cb => cb.value);
    const selectedCollections = Array.from(document.querySelectorAll('.collection-filter:checked')).map(cb => cb.value);
    
    let filteredDocs = this.documents;
    
    if (selectedStatuses.length > 0) {
      filteredDocs = filteredDocs.filter(doc => selectedStatuses.includes(doc.status));
    }
    
    if (selectedCollections.length > 0) {
      filteredDocs = filteredDocs.filter(doc => selectedCollections.includes(doc.collectionId));
    }
    
    this.updateDocumentDisplay(filteredDocs);
  }

  updateDocumentDisplay(docsToShow = null) {
    const docs = docsToShow || this.documents;
    const grid = document.getElementById('documents-grid');
    const resultCount = document.getElementById('result-count');
    
    if (!grid || !resultCount) return;

    // Apply sorting
    const sortBy = document.getElementById('sort-select')?.value || 'updated';
    const sortedDocs = this.sortDocuments(docs, sortBy);

    // Filter by role permissions
    const visibleDocs = this.filterDocumentsByRole(sortedDocs);

    grid.innerHTML = '';
    visibleDocs.forEach(doc => {
  const card = window.ui.createDocumentCard(doc);
  // Override card click to open View mode
  card.onclick = () => this.openViewForDocument(doc.id);
  grid.appendChild(card);
    });

    resultCount.textContent = `Showing ${visibleDocs.length} of ${docs.length} document${docs.length !== 1 ? 's' : ''}`;
  }

  async openViewForDocument(id, push = true) {
    try {
      const doc = await window.api.getDocument(id);
      this.currentDocument = doc;
      this.renderViewMode(doc);
      this.setMode('view');
      if (push) {
        try { window.history.pushState({}, '', `/view/${encodeURIComponent(id)}`); } catch {}
      }
  // Track a view (best-effort)
  try { await window.api.trackDocumentView(id); } catch {}
    } catch (error) {
      console.error('Failed to open document:', error);
      window.ui.showToast('Failed to open document', 'error');
    }
  }

  renderViewMode(doc) {
    // Basic article info
    const titleEl = document.getElementById('view-title');
    const summaryEl = document.getElementById('view-summary');
    const metaEl = document.getElementById('view-meta');
    const statusEl = document.getElementById('view-status');
    const updatedEl = document.getElementById('view-updated');
    const contentEl = document.getElementById('view-content');
    const editBtn = document.getElementById('view-edit-btn');

    if (titleEl) titleEl.textContent = doc.title;
    if (summaryEl) summaryEl.textContent = doc.summary;
    if (metaEl) {
      const collection = this.collections.find(c => c.id === doc.collectionId);
      metaEl.innerHTML = `
        <span><i class="fas fa-user mr-1"></i>by ${window.ui.escapeHtml(doc.authorName)}</span>
        <span><i class="fas fa-folder mr-1"></i>${collection?.name || 'Unknown'}</span>
        <span><i class="fas fa-eye mr-1"></i>${doc.viewCount || 0} views</span>
      `;
    }
    if (statusEl) {
      statusEl.innerHTML = '';
      statusEl.appendChild(window.ui.createStatusBadge(doc.status));
    }
    if (updatedEl) updatedEl.textContent = `Updated ${window.ui.formatTimeAgo(doc.updatedAt)}`;
    if (contentEl) contentEl.innerHTML = window.ui.markdownToHtml(doc.content || '');

    // Breadcrumbs
    this.renderBreadcrumbs(doc);

    // Tags
    this.renderTags(doc);

    // Table of Contents
    this.generateTableOfContents(doc.content);

    // Article stats
    this.renderArticleStats(doc);

    // Author info
    this.renderAuthorInfo(doc);

    // Rating and read time
    this.renderArticleMetrics(doc);

    // Related articles
    this.renderRelatedArticles(doc);

    // Edit button visibility based on role
    if (editBtn) {
      if (window.auth.hasPermission('edit')) {
        editBtn.classList.remove('hidden');
      } else {
        editBtn.classList.add('hidden');
      }
    }

    // Update bookmark state
    this.updateBookmarkState(doc.id);
  }

  renderBreadcrumbs(doc) {
    const collectionEl = document.getElementById('view-breadcrumb-collection');
    const titleEl = document.getElementById('view-breadcrumb-title');
    
    const collection = this.collections.find(c => c.id === doc.collectionId);
    if (collectionEl) {
      collectionEl.textContent = collection?.name || 'Unknown Collection';
      collectionEl.onclick = () => {
        // Filter by collection and return to browse
        this.filterByCollection(doc.collectionId);
        this.setMode('browse');
      };
    }
    if (titleEl) titleEl.textContent = doc.title;
  }

  renderTags(doc) {
    const tagsEl = document.getElementById('view-tags');
    if (!tagsEl || !doc.tags || doc.tags.length === 0) {
      if (tagsEl) tagsEl.classList.add('hidden');
      return;
    }

    tagsEl.classList.remove('hidden');
    tagsEl.innerHTML = doc.tags.map(tag => 
      `<span class="bg-slate-700 text-gray-300 px-3 py-1 rounded-full text-sm hover:bg-slate-600 cursor-pointer transition-colors" onclick="searchByTag('${window.ui.escapeHtml(tag)}')">#${window.ui.escapeHtml(tag)}</span>`
    ).join('');
  }

  generateTableOfContents(content) {
    const tocEl = document.getElementById('view-toc');
    if (!tocEl || !content) return;

    // Extract headings from markdown content
    const headings = content.match(/^#{1,3}\s+(.+)$/gm) || [];
    
    if (headings.length === 0) {
      tocEl.innerHTML = '<p class="text-gray-400 text-sm">No headings found</p>';
      return;
    }

    const tocItems = headings.map((heading, index) => {
      const level = heading.match(/^#+/)[0].length;
      const text = heading.replace(/^#+\s+/, '');
      const id = `heading-${index}`;
      const indent = level === 1 ? '' : level === 2 ? 'ml-4' : 'ml-8';
      
      return `<a href="#${id}" class="block text-sm text-gray-300 hover:text-white py-1 ${indent} transition-colors" onclick="scrollToHeading('${id}')">${window.ui.escapeHtml(text)}</a>`;
    }).join('');

    tocEl.innerHTML = tocItems;

    // Add IDs to actual headings in content for smooth scrolling
    this.addHeadingIds(content);
  }

  addHeadingIds(content) {
    const contentEl = document.getElementById('view-content');
    if (!contentEl) return;

    const headings = contentEl.querySelectorAll('h1, h2, h3');
    headings.forEach((heading, index) => {
      heading.id = `heading-${index}`;
    });
  }

  renderArticleStats(doc) {
    const viewsEl = document.getElementById('view-stat-views');
    const ratingEl = document.getElementById('view-stat-rating');
    const starsEl = document.getElementById('view-stat-stars');
    const updatedEl = document.getElementById('view-stat-updated');
    const createdEl = document.getElementById('view-stat-created');

    if (viewsEl) viewsEl.textContent = (doc.viewCount || 0).toLocaleString();
    if (ratingEl) ratingEl.textContent = (doc.rating || 0).toFixed(1);
    if (starsEl) starsEl.appendChild(window.ui.createRatingStars(doc.rating || 0));
    if (updatedEl) updatedEl.textContent = window.ui.formatTimeAgo(doc.updatedAt);
    if (createdEl) createdEl.textContent = window.ui.formatTimeAgo(doc.createdAt || doc.updatedAt);
  }

  renderAuthorInfo(doc) {
    const nameEl = document.getElementById('view-author-name');
    const roleEl = document.getElementById('view-author-role');
    const articlesEl = document.getElementById('view-author-articles');

    if (nameEl) nameEl.textContent = doc.authorName;
    if (roleEl) roleEl.textContent = 'Author'; // Could be enhanced with actual author role
    if (articlesEl) {
      // Count articles by this author
      const authorArticles = this.documents.filter(d => d.authorName === doc.authorName).length;
      articlesEl.textContent = authorArticles;
    }
  }

  renderArticleMetrics(doc) {
    const ratingEl = document.getElementById('view-rating');
    const readTimeEl = document.getElementById('view-read-time');

    if (ratingEl && doc.rating > 0) {
      ratingEl.innerHTML = `
        ${window.ui.createRatingStars(doc.rating).outerHTML}
        <span class="text-gray-400">(${doc.ratingCount || 0})</span>
      `;
    }

    if (readTimeEl) {
      const wordCount = (doc.content || '').split(/\s+/).length;
      const readTime = Math.max(1, Math.ceil(wordCount / 200)); // Average 200 WPM
      readTimeEl.innerHTML = `<i class="fas fa-clock mr-1"></i><span>${readTime} min read</span>`;
    }
  }

  renderRelatedArticles(doc) {
    const relatedEl = document.getElementById('view-related-grid');
    if (!relatedEl) return;

    // Find related articles by collection and tags
    let related = this.documents.filter(d => 
      d.id !== doc.id && 
      d.status === 'Published' &&
      (d.collectionId === doc.collectionId || 
       (doc.tags && d.tags && doc.tags.some(tag => d.tags.includes(tag))))
    );

    // Limit to 4 related articles
    related = related.slice(0, 4);

    if (related.length === 0) {
      relatedEl.innerHTML = '<p class="text-gray-400 col-span-2">No related articles found.</p>';
      return;
    }

    relatedEl.innerHTML = related.map(relDoc => `
      <div class="bg-slate-800 rounded-lg p-4 hover:bg-slate-700 transition-colors cursor-pointer" onclick="window.app.openViewForDocument('${relDoc.id}')">
        <h4 class="font-medium text-white mb-2 line-clamp-2">${window.ui.escapeHtml(relDoc.title)}</h4>
        <p class="text-sm text-gray-400 mb-3 line-clamp-2">${window.ui.escapeHtml(relDoc.summary)}</p>
        <div class="flex items-center justify-between text-xs text-gray-500">
          <span>${window.ui.formatTimeAgo(relDoc.updatedAt)}</span>
          <span class="flex items-center">
            <i class="fas fa-eye mr-1"></i>
            ${relDoc.viewCount || 0}
          </span>
        </div>
      </div>
    `).join('');
  }

  async updateBookmarkState(docId) {
    const bookmarkBtn = document.getElementById('view-bookmark-btn');
    if (!bookmarkBtn) return;

    // Check bookmark state via API; fall back to local state if unavailable
    let isBookmarked = false;
    try {
      const res = await window.api.isBookmarked(docId);
      isBookmarked = !!res.bookmarked;
    } catch {}

    if (isBookmarked) {
      bookmarkBtn.innerHTML = '<i class="fas fa-bookmark mr-2"></i>Bookmarked';
      bookmarkBtn.classList.remove('bg-slate-700', 'hover:bg-slate-600');
      bookmarkBtn.classList.add('bg-emerald-600', 'hover:bg-emerald-700');
    } else {
      bookmarkBtn.innerHTML = '<i class="far fa-bookmark mr-2"></i>Bookmark';
      bookmarkBtn.classList.add('bg-slate-700', 'hover:bg-slate-600');
      bookmarkBtn.classList.remove('bg-emerald-600', 'hover:bg-emerald-700');
    }
  }

// (moved) withdrawCurrentDraft is defined in global functions section below

  filterByCollection(collectionId) {
    // Reset search and filters
    const heroSearch = document.getElementById('hero-search');
    if (heroSearch) heroSearch.value = '';
    
    // Uncheck all collection filters
    document.querySelectorAll('.collection-filter').forEach(cb => cb.checked = false);
    
    // Check only the specified collection
    const targetFilter = document.querySelector(`.collection-filter[value="${collectionId}"]`);
    if (targetFilter) targetFilter.checked = true;
    
    // Apply filters
    this.applyFilters();
  }

  updateViewNavState() {
    const viewBtn = document.getElementById('mode-view');
    if (!viewBtn) return;
    if (this.currentDocument) {
      viewBtn.removeAttribute('disabled');
      viewBtn.title = 'View current article';
      viewBtn.onclick = () => this.setMode('view');
    } else {
      viewBtn.setAttribute('disabled', 'true');
      viewBtn.title = 'Open a document to view';
      viewBtn.onclick = () => {};
    }
  }

  sortDocuments(docs, sortBy) {
    return [...docs].sort((a, b) => {
      switch (sortBy) {
        case 'title':
          return a.title.localeCompare(b.title);
        case 'rating':
          return b.rating - a.rating;
        case 'views':
          return b.viewCount - a.viewCount;
        case 'updated':
        default:
          return new Date(b.updatedAt) - new Date(a.updatedAt);
      }
    });
  }

  filterDocumentsByRole(docs) {
    if (window.auth.hasRole('Author')) {
      return docs; // Authors can see all documents
    }
    return docs.filter(doc => doc.status === 'Published'); // Readers only see published
  }

  populateCollectionFilters() {
    const container = document.getElementById('collection-filters');
    if (!container) return;

    container.innerHTML = '';
    this.collections.forEach(collection => {
      const label = document.createElement('label');
      label.className = 'flex items-center';
      label.innerHTML = `
        <input type="checkbox" value="${collection.id}" class="collection-filter mr-2" checked>
        <span class="text-sm">${window.ui.escapeHtml(collection.name)}</span>
        <span class="text-xs text-gray-400 ml-1">(${collection.documentCount})</span>
      `;
      container.appendChild(label);
      
      // Add event listener
      const checkbox = label.querySelector('.collection-filter');
      checkbox.addEventListener('change', () => this.applyFilters());
    });
  }

  populateCollectionSelects() {
    const selects = document.querySelectorAll('#doc-collection');
    selects.forEach(select => {
      select.innerHTML = '';
      this.collections.forEach(collection => {
        const option = document.createElement('option');
        option.value = collection.id;
        option.textContent = collection.name;
        if (collection.isDefault) {
          option.selected = true;
        }
        select.appendChild(option);
      });
    });
  }

  setupEditMode() {
    const titleInput = document.getElementById('doc-title');
    const summaryInput = document.getElementById('doc-summary');
    const contentInput = document.getElementById('doc-content');
    
    if (this.currentDocument) {
      // Editing existing document
      if (titleInput) titleInput.value = this.currentDocument.title;
      if (summaryInput) summaryInput.value = this.currentDocument.summary;
      if (contentInput) contentInput.value = this.currentDocument.content;
      
      const editTitle = document.getElementById('edit-title');
      if (editTitle) editTitle.textContent = `Edit: ${this.currentDocument.title}`;
      // Toggle withdraw button if in Review status
      const withdrawBtn = document.getElementById('withdraw-btn');
      if (withdrawBtn) {
        if (this.currentDocument.status === 'Review') withdrawBtn.classList.remove('hidden');
        else withdrawBtn.classList.add('hidden');
      }
    } else {
      // Creating new document
      if (titleInput) titleInput.value = '';
      if (summaryInput) summaryInput.value = '';
      if (contentInput) contentInput.value = '';
      
      const editTitle = document.getElementById('edit-title');
      if (editTitle) editTitle.textContent = 'Create New Document';
  const withdrawBtn = document.getElementById('withdraw-btn');
  if (withdrawBtn) withdrawBtn.classList.add('hidden');
    }
  }

  updateContentPreview(content) {
    const preview = document.getElementById('content-preview');
    if (!preview) return;

    if (!content.trim()) {
      preview.innerHTML = '<div class="text-gray-400 text-center py-20">Preview will appear here as you type...</div>';
      return;
    }

    preview.innerHTML = window.ui.markdownToHtml(content);
  }

  updateModerationQueue() {
    const statuses = ['Draft', 'Review', 'Published', 'Archived'];
    const columns = {
      'Draft': 'submitted-items',
      'Review': 'review-items', 
      'Published': 'approved-items',
      'Archived': 'returned-items'
    };
    
    const counts = {
      'Draft': 'submitted-count',
      'Review': 'review-count',
      'Published': 'approved-count', 
      'Archived': 'returned-count'
    };

    statuses.forEach(status => {
      const docs = this.documents.filter(doc => doc.status === status);
      const container = document.getElementById(columns[status]);
      const countEl = document.getElementById(counts[status]);
      
      if (container) {
        container.innerHTML = '';
        docs.forEach(doc => {
          container.appendChild(window.ui.createModerationItem(doc));
        });
      }
      
      if (countEl) {
        countEl.textContent = docs.length;
      }
    });

    // Update pending count in navigation
    const pendingCount = document.getElementById('pending-count');
    const reviewDocs = this.documents.filter(doc => doc.status === 'Review');
    if (pendingCount) {
      if (reviewDocs.length > 0) {
        pendingCount.textContent = reviewDocs.length;
        pendingCount.classList.remove('hidden');
      } else {
        pendingCount.classList.add('hidden');
      }
    }
  }

  updateUsersDisplay() {
    const container = document.getElementById('users-list');
    if (!container) return;

    container.innerHTML = '';
    this.users.forEach(user => {
      container.appendChild(window.ui.createUserCard(user));
    });
  }

  updateAdminSettings() {
    // This would typically load system settings
    // For now, just showing static mock data
  }

  getCollectionIdByName(name) {
    const collection = this.collections.find(c => c.name === name);
    return collection ? collection.id : 'guides';
  }

  async refreshData() {
    console.log('🔄 Refreshing data...');
    await this.loadCollections();
    await this.loadDocuments();
    if (this.currentMode === 'admin') {
      await this.loadUsers();
    }
  }
}

// Global functions for UI interactions
window.setMode = function(mode) {
  if (!window.app) return;
  switch (mode) {
    case 'browse': return window.app.navigateTo('/browse');
    case 'moderate': return window.app.navigateTo('/moderate');
    case 'view':
      if (window.app.currentDocument) {
        return window.app.navigateTo(`/view/${encodeURIComponent(window.app.currentDocument.id)}`);
      }
      return;
    case 'edit':
      if (window.app.currentDocument) {
        return window.app.navigateTo(`/edit/${encodeURIComponent(window.app.currentDocument.id)}`);
      }
      return window.app.navigateTo('/edit');
    default:
      return window.app.setMode(mode);
  }
};

window.viewDocument = async function(id) {
  if (!window.app) return;
  window.app.navigateTo(`/view/${encodeURIComponent(id)}`);
};

window.editDocument = function(id) {
  if (!window.app) return;
  window.app.navigateTo(`/edit/${encodeURIComponent(id)}`);
  window.ui.closeModal();
};

// Used by the View mode Edit button
window.editCurrentDocument = function() {
  if (window.app && window.app.currentDocument) {
    window.app.navigateTo(`/edit/${encodeURIComponent(window.app.currentDocument.id)}`);
  }
};

window.createNewDocument = function() {
  if (window.app) {
    window.app.currentDocument = null;
    window.app.navigateTo('/edit');
  }
};

// Withdraw current document from review (edit header button)
window.withdrawCurrentDraft = async function() {
  if (!window.app || !window.app.currentDocument) return;
  const id = window.app.currentDocument.id;
  try {
    await window.api.withdrawDraft(id);
    window.ui.showToast('Submission withdrawn', 'info');
    await window.app.refreshData();
    window.app.setMode('edit');
  } catch (error) {
    console.error('Failed to withdraw draft:', error);
    window.ui.showToast('Failed to withdraw draft', 'error');
  }
};

window.saveDocument = async function() {
  const title = document.getElementById('doc-title')?.value;
  const summary = document.getElementById('doc-summary')?.value;
  const content = document.getElementById('doc-content')?.value;
  const collection = document.getElementById('doc-collection')?.value;

  if (!title || !summary || !content) {
    window.ui.showToast('Please fill in all required fields', 'warning');
    return;
  }

  const document = {
    title,
    summary,
    content,
    collectionId: collection,
    tags: [] // Could be extracted from content or user input
  };

  try {
    if (window.app.currentDocument) {
      await window.api.updateDocument(window.app.currentDocument.id, document);
      window.ui.showToast('Document updated successfully', 'success');
    } else {
      await window.api.createDocument(document);
      window.ui.showToast('Document created successfully', 'success');
    }
    
    await window.app.refreshData();
    window.app.setMode('browse');
  } catch (error) {
    console.error('Failed to save document:', error);
    window.ui.showToast('Failed to save document', 'error');
  }
};

window.publishDocument = async function() {
  await window.saveDocument();
  try {
    const id = window.app.currentDocument?.id;
    // Ensure we have the latest id (after create)
    await window.app.refreshData();
    const doc = id ? window.app.documents.find(d => d.id === id) : null;
    const targetId = doc?.id || window.app.documents.find(d => d.title === document.getElementById('doc-title')?.value)?.id;
    if (targetId) {
      // create/update draft snapshot from current fields, then submit
      const snapshot = {
        id: targetId,
        title: document.getElementById('doc-title')?.value,
        summary: document.getElementById('doc-summary')?.value,
        content: document.getElementById('doc-content')?.value,
        collectionId: document.getElementById('doc-collection')?.value
      };
      try { await window.api.createDraft(targetId, snapshot); } catch {}
      await window.api.submitDraft(targetId);
      window.ui.showToast('Draft submitted for review', 'success');
      await window.app.refreshData();
    } else {
      window.ui.showToast('Unable to find document to submit', 'error');
    }
  } catch (error) {
    console.error('Failed to submit for review:', error);
    window.ui.showToast('Failed to submit for review', 'error');
  }
};

window.approveDocument = async function(id) {
  try {
    await window.api.approveSubmission(id);
    window.ui.showToast('Submission approved', 'success');
    await window.app.refreshData();
  } catch (error) {
    console.error('Failed to approve document:', error);
    window.ui.showToast('Failed to approve document', 'error');
  }
};

// Submit current document draft for review
window.submitForReview = async function(id) {
  try {
    await window.api.submitDraft(id);
    window.ui.showToast('Draft submitted for review', 'success');
    await window.app.refreshData();
  } catch (error) {
    console.error('Failed to submit draft:', error);
    window.ui.showToast('Failed to submit draft', 'error');
  }
};

// Withdraw a submitted item back to draft
window.withdrawDraft = async function(id) {
  try {
    await window.api.withdrawDraft(id);
    window.ui.showToast('Submission withdrawn', 'info');
    await window.app.refreshData();
  } catch (error) {
    console.error('Failed to withdraw draft:', error);
    window.ui.showToast('Failed to withdraw draft', 'error');
  }
};

window.getAIAssistance = async function() {
  const content = document.getElementById('doc-content')?.value || '';
  const title = document.getElementById('doc-title')?.value || '';
  
  if (!content.trim()) {
    window.ui.showToast('Please add some content first', 'warning');
    return;
  }

  try {
    const assistance = await window.api.getAIAssistance(content, title);
    const resultsContainer = document.getElementById('ai-assistance-results');
    
    if (resultsContainer) {
      resultsContainer.innerHTML = `
        <div class="space-y-3">
          ${assistance.suggestedTags.length > 0 ? `
            <div>
              <h4 class="text-sm font-medium text-emerald-400 mb-2">Suggested Tags:</h4>
              <div class="flex flex-wrap gap-1">
                ${assistance.suggestedTags.map(tag => 
                  `<span class="bg-slate-700 text-gray-300 px-2 py-1 rounded text-xs cursor-pointer hover:bg-slate-600">${tag}</span>`
                ).join('')}
              </div>
            </div>
          ` : ''}
          
          <div>
            <h4 class="text-sm font-medium text-emerald-400 mb-2">Quality Score:</h4>
            <div class="flex items-center space-x-2">
              <div class="flex-1 bg-slate-700 rounded-full h-2">
                <div class="bg-emerald-500 h-2 rounded-full" style="width: ${assistance.qualityScore * 10}%"></div>
              </div>
              <span class="text-xs text-gray-300">${assistance.qualityScore.toFixed(1)}/10</span>
            </div>
          </div>
          
          ${assistance.qualityIssues.length > 0 ? `
            <div>
              <h4 class="text-sm font-medium text-emerald-400 mb-2">Improvements:</h4>
              <ul class="space-y-1 text-xs text-gray-300">
                ${assistance.qualityIssues.map(issue => `<li>• ${issue}</li>`).join('')}
              </ul>
            </div>
          ` : ''}
        </div>
      `;
    }
    
    window.ui.showToast('AI assistance updated', 'success');
  } catch (error) {
    console.error('Failed to get AI assistance:', error);
    window.ui.showToast('Failed to get AI assistance', 'error');
  }
};

// Initialize app when DOM is loaded
document.addEventListener('DOMContentLoaded', () => {
  window.app = new TechDocsApp();
});

// Global functions for View mode interactions
window.toggleBookmark = async function() {
  if (!window.app || !window.app.currentDocument) return;
  const docId = window.app.currentDocument.id;
  try {
    const state = await window.api.isBookmarked(docId);
    if (state.bookmarked) {
      await window.api.removeBookmark(docId);
      window.ui.showToast('Bookmark removed', 'info');
    } else {
      await window.api.addBookmark(docId);
      window.ui.showToast('Article bookmarked', 'success');
    }
  } catch (e) {
    console.error('Bookmark toggle failed:', e);
    window.ui.showToast('Bookmark action failed', 'error');
  }
  if (window.app.updateBookmarkState) await window.app.updateBookmarkState(docId);
};

window.shareArticle = function() {
  if (!window.app || !window.app.currentDocument) return;
  
  const doc = window.app.currentDocument;
  const url = `${window.location.origin}${window.location.pathname}?view=${doc.id}`;
  
  if (navigator.share) {
    navigator.share({
      title: doc.title,
      text: doc.summary,
      url: url
    }).catch(console.error);
  } else {
    // Fallback: copy to clipboard
    navigator.clipboard.writeText(url).then(() => {
      window.ui.showToast('Link copied to clipboard', 'success');
    }).catch(() => {
      window.ui.showToast('Failed to copy link', 'error');
    });
  }
};

window.rateArticle = async function(rating) {
  if (!window.app || !window.app.currentDocument) return;
  const docId = window.app.currentDocument.id;
  try {
    await window.api.rateDocument(docId, rating);
  } catch (e) {
    console.error('Rating failed:', e);
    window.ui.showToast('Failed to submit rating', 'error');
    return;
  }
  const message = rating >= 4 ? 'Thanks for the positive feedback!' : 'Thank you for your feedback. We\'ll work to improve this article.';
  window.ui.showToast(message, rating >= 4 ? 'success' : 'info');
  const feedbackEl = document.getElementById('view-feedback');
  if (feedbackEl) {
    feedbackEl.innerHTML = `<span class="text-emerald-400">Feedback recorded</span>`;
  }
};

window.reportIssue = function() {
  if (!window.app || !window.app.currentDocument) return;
  
  const doc = window.app.currentDocument;
  const content = `
    <div class="space-y-4">
      <p class="text-gray-300">Report an issue with: <strong>${window.ui.escapeHtml(doc.title)}</strong></p>
      <div>
        <label class="block text-sm font-medium text-gray-300 mb-2">Issue Type</label>
        <select id="issue-type" class="w-full bg-slate-800 text-white rounded-lg px-3 py-2">
          <option value="content">Content Error</option>
          <option value="outdated">Outdated Information</option>
          <option value="unclear">Unclear Instructions</option>
          <option value="broken">Broken Links/Examples</option>
          <option value="other">Other</option>
        </select>
      </div>
      <div>
        <label class="block text-sm font-medium text-gray-300 mb-2">Description</label>
        <textarea id="issue-description" rows="4" class="w-full bg-slate-800 text-white rounded-lg px-3 py-2" placeholder="Please describe the issue..."></textarea>
      </div>
      <div class="flex justify-end space-x-3">
        <button onclick="closeModal()" class="bg-slate-700 hover:bg-slate-600 px-4 py-2 rounded">Cancel</button>
        <button onclick="submitIssueReport()" class="bg-red-600 hover:bg-red-700 px-4 py-2 rounded">Submit Report</button>
      </div>
    </div>
  `;
  
  window.ui.openModal('document-modal', 'Report Issue', content);
};

window.submitIssueReport = async function() {
  const type = document.getElementById('issue-type')?.value;
  const description = document.getElementById('issue-description')?.value;
  
  if (!description?.trim()) {
    window.ui.showToast('Please provide a description', 'warning');
    return;
  }

  try {
    const docId = window.app.currentDocument?.id;
    await window.api.reportIssue(docId, type, description);
    window.ui.closeModal();
    window.ui.showToast('Issue reported successfully. Thank you!', 'success');
  } catch (e) {
    console.error('Issue report failed', e);
    window.ui.showToast('Failed to report issue', 'error');
  }
};

window.scrollToHeading = function(headingId) {
  const element = document.getElementById(headingId);
  if (element) {
    element.scrollIntoView({ behavior: 'smooth', block: 'start' });
    
    // Highlight the heading briefly
    element.style.backgroundColor = 'rgba(147, 51, 234, 0.2)';
    setTimeout(() => {
      element.style.backgroundColor = '';
    }, 2000);
  }
};

window.searchByTag = function(tag) {
  if (!window.app) return;
  
  // Set search input to tag
  const heroSearch = document.getElementById('hero-search');
  if (heroSearch) {
    heroSearch.value = `#${tag}`;
  }
  
  // Switch to browse mode and perform search
  window.app.setMode('browse');
  window.app.performSearch(`#${tag}`);
  
  window.ui.showToast(`Searching for articles tagged with "${tag}"`, 'info');
};
