// UI Utilities and Helper Functions
class UI {
  constructor() {
    this.modals = new Map();
  }

  // Toast notifications
  showToast(message, type = 'info', duration = 3000) {
    const toast = document.createElement('div');
    toast.className = `fixed top-4 right-4 z-50 px-6 py-3 rounded-lg shadow-lg transition-all duration-300 transform translate-x-full`;
    
    const typeClasses = {
      'success': 'bg-emerald-600 text-white',
      'error': 'bg-red-600 text-white',
      'warning': 'bg-amber-600 text-white',
      'info': 'bg-blue-600 text-white'
    };
    
    toast.classList.add(...(typeClasses[type] || typeClasses['info']).split(' '));
    toast.textContent = message;
    
    document.body.appendChild(toast);
    
    // Animate in
    setTimeout(() => {
      toast.classList.remove('translate-x-full');
    }, 100);
    
    // Animate out and remove
    setTimeout(() => {
      toast.classList.add('translate-x-full');
      setTimeout(() => {
        if (document.body.contains(toast)) {
          document.body.removeChild(toast);
        }
      }, 300);
    }, duration);
  }

  // Loading states
  showLoading(element, text = 'Loading...') {
    if (typeof element === 'string') {
      element = document.getElementById(element);
    }
    if (!element) return;

    element.innerHTML = `
      <div class="flex items-center justify-center py-8">
        <div class="animate-spin rounded-full h-8 w-8 border-b-2 border-purple-500 mr-3"></div>
        <span class="text-gray-400">${text}</span>
      </div>
    `;
  }

  hideLoading(element) {
    if (typeof element === 'string') {
      element = document.getElementById(element);
    }
    if (!element) return;

    element.innerHTML = '';
  }

  // Status badges
  createStatusBadge(status) {
    const badge = document.createElement('span');
    badge.className = 'px-2 py-1 rounded-full text-xs font-medium border';
    
    const statusClasses = {
      'Draft': 'bg-amber-500/20 text-amber-400 border-amber-500/30',
      'Review': 'bg-orange-500/20 text-orange-400 border-orange-500/30',
      'Published': 'bg-emerald-500/20 text-emerald-400 border-emerald-500/30',
      'Archived': 'bg-gray-500/20 text-gray-400 border-gray-500/30'
    };
    
    badge.className += ' ' + (statusClasses[status] || statusClasses['Draft']);
    badge.textContent = status;
    
    return badge;
  }

  // Rating stars
  createRatingStars(rating, maxRating = 5) {
    const container = document.createElement('div');
    container.className = 'flex items-center text-yellow-400';
    
    for (let i = 1; i <= maxRating; i++) {
      const star = document.createElement('i');
      star.className = `fas fa-star text-sm ${i <= rating ? 'text-yellow-400' : 'text-gray-600'}`;
      container.appendChild(star);
    }
    
    return container;
  }

  // Time formatting
  formatTimeAgo(date) {
    const now = new Date();
    const diff = now - new Date(date);
    const seconds = Math.floor(diff / 1000);
    const minutes = Math.floor(seconds / 60);
    const hours = Math.floor(minutes / 60);
    const days = Math.floor(hours / 24);
    
    if (days > 0) return `${days}d ago`;
    if (hours > 0) return `${hours}h ago`;
    if (minutes > 0) return `${minutes}m ago`;
    return 'Just now';
  }

  // Collection icons
  getCollectionIcon(collectionId) {
    const icons = {
      'getting-started': '🚀',
      'guides': '📚',
      'api-reference': '🔧',
      'faq': '❓',
      'troubleshooting': '🔍'
    };
    return icons[collectionId] || '📄';
  }

  getCollectionClass(collectionId) {
    const classes = {
      'getting-started': 'collection-getting-started',
      'guides': 'collection-guides',
      'api-reference': 'collection-api-reference',
      'faq': 'collection-faq',
      'troubleshooting': 'collection-troubleshooting'
    };
    return classes[collectionId] || 'bg-slate-600';
  }

  // Role tags
  createRoleTag(role) {
    const tag = document.createElement('span');
    tag.className = 'inline-block px-2 py-1 rounded text-xs font-medium border';
    
    const roleClasses = {
      'Reader': 'bg-blue-500/20 text-blue-400 border-blue-500/30',
      'Author': 'bg-purple-500/20 text-purple-400 border-purple-500/30',
      'Moderator': 'bg-orange-500/20 text-orange-400 border-orange-500/30',
      'Admin': 'bg-red-500/20 text-red-400 border-red-500/30'
    };
    
    tag.className += ' ' + (roleClasses[role] || roleClasses['Reader']);
    tag.textContent = role;
    
    return tag;
  }

  // Modal management
  openModal(modalId, title, content) {
    const modal = document.getElementById(modalId);
    if (!modal) return;

    const titleEl = modal.querySelector('#modal-title');
    const contentEl = modal.querySelector('#modal-content');
    
    if (titleEl) titleEl.textContent = title;
    if (contentEl) contentEl.innerHTML = content;
    
    modal.classList.remove('hidden');
    document.body.style.overflow = 'hidden';
  }

  closeModal(modalId = 'document-modal') {
    const modal = document.getElementById(modalId);
    if (!modal) return;

    modal.classList.add('hidden');
    document.body.style.overflow = 'auto';
  }

  // Document card creation
  createDocumentCard(doc) {
    const card = document.createElement('div');
    card.className = 'document-card';
  // Click handler is assigned by app.js so modes can control behavior
    
    card.innerHTML = `
      <div class="flex items-start justify-between mb-3">
        <div class="flex items-center space-x-3">
          <div class="collection-icon ${this.getCollectionClass(doc.collectionId)}">
            ${this.getCollectionIcon(doc.collectionId)}
          </div>
          <div>
            <h3 class="font-semibold text-white line-clamp-2">${this.escapeHtml(doc.title)}</h3>
            <p class="text-sm text-gray-400">${this.escapeHtml(doc.summary)}</p>
          </div>
        </div>
        ${this.createStatusBadge(doc.status).outerHTML}
      </div>
      
      <div class="flex items-center justify-between text-sm text-gray-400 mb-3">
        <span>by ${this.escapeHtml(doc.authorName)}</span>
        <span>${this.formatTimeAgo(doc.updatedAt)}</span>
      </div>
      
      <div class="flex items-center justify-between">
        <div class="flex items-center space-x-4">
          ${doc.rating > 0 ? this.createRatingStars(doc.rating).outerHTML : ''}
          ${doc.rating > 0 ? `<span class="text-xs text-gray-400">(${doc.ratingCount})</span>` : ''}
        </div>
        <div class="flex items-center space-x-2 text-xs text-gray-400">
          <span><i class="fas fa-eye mr-1"></i>${doc.viewCount}</span>
        </div>
      </div>
      
      ${doc.tags && doc.tags.length > 0 ? `
        <div class="mt-3 flex flex-wrap gap-1">
          ${doc.tags.slice(0, 3).map(tag => 
            `<span class="bg-slate-700 text-gray-300 px-2 py-1 rounded text-xs">${this.escapeHtml(tag)}</span>`
          ).join('')}
          ${doc.tags.length > 3 ? `<span class="text-xs text-gray-400">+${doc.tags.length - 3} more</span>` : ''}
        </div>
      ` : ''}
    `;
    
    return card;
  }

  // Moderation item creation
  createModerationItem(doc) {
    const item = document.createElement('div');
    item.className = 'moderation-item';
    item.dataset.docId = doc.id;
    
    item.innerHTML = `
      <div class="flex items-center justify-between mb-2">
        <h4 class="font-medium text-white text-sm">${this.escapeHtml(doc.title)}</h4>
        <input type="checkbox" class="moderation-checkbox">
      </div>
      <div class="text-xs text-gray-400 mb-2">
        by ${this.escapeHtml(doc.authorName)} • ${this.formatTimeAgo(doc.updatedAt)}
      </div>
      ${doc.reviewNotes ? `
        <div class="text-xs text-gray-300 mb-2 p-2 bg-slate-700 rounded">
          💬 ${this.escapeHtml(doc.reviewNotes)}
        </div>
      ` : ''}
      <div class="flex items-center space-x-2">
        <button onclick="viewDocument('${doc.id}')" class="text-xs bg-slate-700 hover:bg-slate-600 px-2 py-1 rounded">
          <i class="fas fa-eye mr-1"></i>View
        </button>
        <button onclick="editDocument('${doc.id}')" class="text-xs bg-blue-600 hover:bg-blue-700 px-2 py-1 rounded">
          <i class="fas fa-edit mr-1"></i>Edit
        </button>
        <button onclick="approveDocument('${doc.id}')" class="text-xs bg-emerald-600 hover:bg-emerald-700 px-2 py-1 rounded">
          <i class="fas fa-check mr-1"></i>Approve
        </button>
      </div>
    `;
    
    return item;
  }

  // User card creation
  createUserCard(user) {
    const card = document.createElement('div');
    card.className = 'user-card';
    
    const rolesList = user.roles.map(role => this.createRoleTag(role).outerHTML).join(' ');
    
    card.innerHTML = `
      <div class="flex items-center justify-between mb-3">
        <div>
          <h4 class="font-medium text-white">${this.escapeHtml(user.name)}</h4>
          <p class="text-sm text-gray-400">${this.escapeHtml(user.email)}</p>
        </div>
        <div class="flex flex-wrap gap-1">
          ${rolesList}
        </div>
      </div>
      <div class="flex items-center justify-between text-xs text-gray-400">
        <span>Last active: ${this.formatTimeAgo(user.lastActive)}</span>
        <div class="flex items-center space-x-2">
          <button onclick="editUser('${user.id}')" class="bg-purple-600 hover:bg-purple-700 px-2 py-1 rounded text-xs">
            <i class="fas fa-edit mr-1"></i>Edit
          </button>
        </div>
      </div>
      <div class="mt-2 text-xs text-gray-400">
        Created: ${user.documentsCreated} docs • Reviewed: ${user.documentsReviewed} docs
      </div>
    `;
    
    return card;
  }

  // HTML escaping
  escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
  }

  // Markdown to HTML (simple)
  markdownToHtml(markdown) {
    return markdown
      .replace(/^# (.*$)/gim, '<h1>$1</h1>')
      .replace(/^## (.*$)/gim, '<h2>$1</h2>')
      .replace(/^### (.*$)/gim, '<h3>$1</h3>')
      .replace(/\*\*(.*)\*\*/gim, '<strong>$1</strong>')
      .replace(/\*(.*)\*/gim, '<em>$1</em>')
      .replace(/```([\s\S]*?)```/gim, '<pre><code>$1</code></pre>')
      .replace(/`([^`]*)`/gim, '<code>$1</code>')
      .replace(/\n/gim, '<br>');
  }

  // Debounce utility
  debounce(func, wait, immediate = false) {
    let timeout;
    return function executedFunction(...args) {
      const later = () => {
        timeout = null;
        if (!immediate) func(...args);
      };
      const callNow = immediate && !timeout;
      clearTimeout(timeout);
      timeout = setTimeout(later, wait);
      if (callNow) func(...args);
    };
  }
}

// Global UI instance
window.ui = new UI();

// Global modal functions
window.openModal = (modalId, title, content) => window.ui.openModal(modalId, title, content);
window.closeModal = (modalId) => window.ui.closeModal(modalId);
