import { PageHeader } from './PageHeader.js';
import { SearchFilter } from './SearchFilter.js';
import { EmptyState } from './EmptyState.js';
import { LoadingState } from './LoadingState.js';

const DEFAULT_FORM = {
  name: '',
  scopeClassification: '',
  regulatoryRegime: '',
  lineOfBusiness: '',
  department: '',
  stakeholdersText: ''
};

export class OrganizationProfilesManager {
  constructor(api, eventBus, toast, router) {
    this.api = api;
    this.eventBus = eventBus;
    this.toast = toast;
    this.router = router;

    this.pageHeader = new PageHeader(router, eventBus);
    this.searchFilter = new SearchFilter(eventBus, {
      searchPlaceholder: 'Search organization profiles by name or department…',
      sortOptions: [
        { value: 'name', label: 'Name' },
        { value: 'updated', label: 'Recently Updated' },
        { value: 'created', label: 'Recently Created' }
      ],
      defaultSort: 'name',
      defaultSortDirection: 'asc'
    });

    this.profiles = [];
    this.filteredProfiles = [];
    this.isLoading = false;
    this.searchQuery = '';
    this.sortBy = 'name';
    this.sortDirection = 'asc';
    this.mode = 'list';
    this.editingProfile = null;
    this.formValues = { ...DEFAULT_FORM };
    this.eventHandlersRegistered = false;
  }

  async render(context = {}) {
    if (context.mode) {
      this.setMode(context.mode, context.profileId || null);
    }

    await this.loadProfiles();

    return `
      <div class="profiles-manager">
        ${this.renderHeader()}
        ${this.renderToolbar()}
        ${this.renderBody()}
      </div>
    `;
  }

  renderHeader() {
    return this.pageHeader.render({
      title: 'Organization Profiles',
      subtitle: 'Manage organizational context injected into extraction prompts and deliverables.',
      breadcrumbs: [
        {
          label: 'Home',
          path: '#/',
          icon: '<rect x="3" y="3" width="7" height="7"></rect><rect x="14" y="3" width="7" height="7"></rect><rect x="14" y="14" width="7" height="7"></rect><rect x="3" y="14" width="7" height="7"></rect>'
        },
        {
          label: 'Organization Profiles',
          path: '#/organization-profiles'
        }
      ],
      actions: [
        {
          label: this.mode === 'create' ? 'Cancel' : 'New Profile',
          action: this.mode === 'create' ? 'cancel-profile' : 'create-profile',
          variant: this.mode === 'create' ? 'secondary' : 'primary',
          icon: this.mode === 'create'
            ? '<line x1="18" y1="6" x2="6" y2="18"></line><line x1="6" y1="6" x2="18" y2="18"></line>'
            : '<line x1="12" y1="5" x2="12" y2="19"></line><line x1="5" y1="12" x2="19" y2="12"></line>'
        }
      ]
    });
  }

  renderToolbar() {
    return `
      <div class="profiles-manager-toolbar">
        ${this.searchFilter.render()}
        <div class="profiles-manager-stats">
          <span class="stat-badge">${this.filteredProfiles.length} ${this.filteredProfiles.length === 1 ? 'profile' : 'profiles'}</span>
        </div>
      </div>
    `;
  }

  renderBody() {
    const formSection = this.mode === 'create' || this.mode === 'edit'
      ? this.renderForm()
      : '';

    const listSection = this.renderProfilesList();

    return `
      <div class="profiles-manager-body">
        ${formSection}
        ${listSection}
      </div>
    `;
  }

  renderForm() {
    const isEdit = this.mode === 'edit';
    return `
      <section class="profiles-form" data-profiles-form>
        <header class="form-header">
          <h2>${isEdit ? 'Edit Organization Profile' : 'Create Organization Profile'}</h2>
          <p>${isEdit
            ? 'Update organizational attributes that shape prompt context across pipelines.'
            : 'Define organizational context available to all pipelines and prompt builders.'}</p>
        </header>
        <form data-profile-form>
          <div class="form-grid">
            <label class="form-field">
              <span class="form-label">Name</span>
              <input type="text" name="name" value="${this.escapeHtml(this.formValues.name)}" required maxlength="128" />
            </label>
            <label class="form-field">
              <span class="form-label">Scope Classification</span>
              <input type="text" name="scopeClassification" value="${this.escapeHtml(this.formValues.scopeClassification)}" maxlength="128" />
            </label>
            <label class="form-field">
              <span class="form-label">Regulatory Regime</span>
              <input type="text" name="regulatoryRegime" value="${this.escapeHtml(this.formValues.regulatoryRegime)}" maxlength="128" />
            </label>
            <label class="form-field">
              <span class="form-label">Line of Business</span>
              <input type="text" name="lineOfBusiness" value="${this.escapeHtml(this.formValues.lineOfBusiness)}" maxlength="128" />
            </label>
            <label class="form-field">
              <span class="form-label">Department</span>
              <input type="text" name="department" value="${this.escapeHtml(this.formValues.department)}" maxlength="128" />
            </label>
          </div>

          <label class="form-field">
            <span class="form-label">Primary Stakeholders</span>
            <textarea name="stakeholdersText" rows="5" placeholder="Role: contact one, contact two">${this.escapeHtml(this.formValues.stakeholdersText)}</textarea>
            <span class="form-help">One role per line. Example: <code>Legal: Alex Carter, Priya Shah</code></span>
          </label>

          <div class="form-actions">
            <button type="submit" class="btn btn-primary">${isEdit ? 'Save Changes' : 'Create Profile'}</button>
            <button type="button" class="btn btn-secondary" data-action="cancel-profile">Cancel</button>
          </div>
        </form>
      </section>
    `;
  }

  renderProfilesList() {
    if (this.isLoading) {
      return LoadingState.render('card', { count: 4 });
    }

    if (this.profiles.length === 0) {
      return EmptyState.render({
        variant: 'onboarding',
        title: 'No organization profiles yet',
        description: 'Create a profile to inject organization-wide context into analysis prompts and deliverables.',
        icon: '<circle cx="12" cy="12" r="3"></circle><path d="M12 1v6m0 6v6m8.66-13.66l-4.24 4.24m-4.84 4.84l-4.24 4.24M23 12h-6m-6 0H1"></path>',
        action: {
          label: 'Create Profile',
          action: 'create-profile',
          variant: 'primary',
          icon: '<line x1="12" y1="5" x2="12" y2="19"></line><line x1="5" y1="12" x2="19" y2="12"></line>'
        }
      });
    }

    if (this.filteredProfiles.length === 0) {
      return EmptyState.forSearchResults();
    }

    return `
      <section class="profiles-list">
        <div class="profiles-grid">
          ${this.filteredProfiles.map(profile => this.renderProfileCard(profile)).join('')}
        </div>
      </section>
    `;
  }

  renderProfileCard(profile) {
    const stakeholders = this.formatStakeholders(profile.primaryStakeholders);
    return `
      <article class="profile-card card-lift" data-profile-id="${this.escapeHtml(profile.id)}">
        <header class="profile-card-header">
          <h3>${this.escapeHtml(profile.name)}</h3>
          <div class="profile-card-actions">
            <button class="btn btn-secondary btn-icon" data-action="edit-profile" data-profile-id="${this.escapeHtml(profile.id)}" title="Edit profile">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M17 3a2.828 2.828 0 1 1 4 4L7.5 20.5 2 22l1.5-5.5z"></path>
              </svg>
            </button>
            <button class="btn btn-danger btn-icon" data-action="delete-profile" data-profile-id="${this.escapeHtml(profile.id)}" title="Delete profile">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <polyline points="3 6 5 6 21 6"></polyline>
                <path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6"></path>
                <path d="M10 11v6"></path>
                <path d="M14 11v6"></path>
                <path d="M9 6V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2"></path>
              </svg>
            </button>
          </div>
        </header>
        <dl class="profile-meta">
          ${profile.scopeClassification ? `<div><dt>Scope</dt><dd>${this.escapeHtml(profile.scopeClassification)}</dd></div>` : ''}
          ${profile.regulatoryRegime ? `<div><dt>Regulation</dt><dd>${this.escapeHtml(profile.regulatoryRegime)}</dd></div>` : ''}
          ${profile.lineOfBusiness ? `<div><dt>Line of Business</dt><dd>${this.escapeHtml(profile.lineOfBusiness)}</dd></div>` : ''}
          ${profile.department ? `<div><dt>Department</dt><dd>${this.escapeHtml(profile.department)}</dd></div>` : ''}
        </dl>
        ${stakeholders ? `
          <section class="profile-stakeholders">
            <h4>Primary Stakeholders</h4>
            <ul>
              ${stakeholders.map(item => `<li><strong>${this.escapeHtml(item.role)}</strong>: ${this.escapeHtml(item.contacts)}</li>`).join('')}
            </ul>
          </section>
        ` : ''}
        <footer class="profile-card-footer">
          <span>Updated ${this.formatRelativeTime(profile.updatedAt || profile.createdAt)}</span>
        </footer>
      </article>
    `;
  }

  attachEventHandlers(container) {
    if (!container) {
      return;
    }

    this.pageHeader.attachEventHandlers(container);
    this.searchFilter.attachEventHandlers(container);

    const form = container.querySelector('[data-profile-form]');
    if (form) {
      form.addEventListener('submit', (event) => {
        event.preventDefault();
        this.handleFormSubmit(new FormData(form));
      });

      const cancelBtn = form.querySelector('[data-action="cancel-profile"]');
      if (cancelBtn) {
        cancelBtn.addEventListener('click', (event) => {
          event.preventDefault();
          this.resetForm();
          this.updateView(container);
        });
      }
    }

    container.querySelectorAll('[data-action="create-profile"]').forEach(button => {
      button.addEventListener('click', (event) => {
        event.preventDefault();
        this.startCreate();
        this.updateView(container);
      });
    });

    container.querySelectorAll('[data-action="cancel-profile"]').forEach(button => {
      button.addEventListener('click', (event) => {
        event.preventDefault();
        this.resetForm();
        this.updateView(container);
      });
    });

    container.querySelectorAll('[data-action="edit-profile"]').forEach(button => {
      button.addEventListener('click', (event) => {
        event.preventDefault();
        const id = button.getAttribute('data-profile-id');
        this.startEdit(id);
        this.updateView(container);
      });
    });

    container.querySelectorAll('[data-action="delete-profile"]').forEach(button => {
      button.addEventListener('click', async (event) => {
        event.preventDefault();
        const id = button.getAttribute('data-profile-id');
        await this.deleteProfile(id);
        this.updateView(container);
      });
    });

    EmptyState.attachEventHandlers(container, this.eventBus);

    if (!this.eventHandlersRegistered) {
      this.eventBus.on('page-header-action', (action) => {
        if (action === 'create-profile') {
          this.startCreate();
          this.updateView(document.querySelector('#app'));
        }
        if (action === 'cancel-profile') {
          this.resetForm();
          this.updateView(document.querySelector('#app'));
        }
      });

      this.eventBus.on('search-filter-changed', (state) => {
        this.searchQuery = state.search || '';
        this.sortBy = state.sortBy || 'name';
        this.sortDirection = state.sortDirection || 'asc';
        this.applyFilters();
        this.updateView(document.querySelector('#app'));
      });

      this.eventBus.on('empty-state-action', (action) => {
        if (action === 'create-profile') {
          this.startCreate();
          this.updateView(document.querySelector('#app'));
        }
      });

      this.eventHandlersRegistered = true;
    }
  }

  async loadProfiles() {
    this.isLoading = true;
    try {
      this.profiles = await this.api.getOrganizationProfiles();
      this.applyFilters();
    } catch (error) {
      console.error('Failed to load organization profiles:', error);
      this.toast.error('Failed to load organization profiles');
      this.profiles = [];
      this.filteredProfiles = [];
    } finally {
      this.isLoading = false;
    }
  }

  applyFilters() {
    const query = this.searchQuery.toLowerCase().trim();

    let filtered = [...this.profiles];
    if (query) {
      filtered = filtered.filter(profile => {
        if (profile.name?.toLowerCase().includes(query)) return true;
        if (profile.department?.toLowerCase().includes(query)) return true;
        if (profile.scopeClassification?.toLowerCase().includes(query)) return true;
        if (profile.regulatoryRegime?.toLowerCase().includes(query)) return true;
        if (profile.lineOfBusiness?.toLowerCase().includes(query)) return true;
        return false;
      });
    }

    filtered.sort((a, b) => {
      let comparison = 0;

      switch (this.sortBy) {
        case 'name':
          comparison = (a.name || '').localeCompare(b.name || '');
          break;
        case 'created': {
          const aDate = new Date(a.createdAt || 0);
          const bDate = new Date(b.createdAt || 0);
          comparison = bDate - aDate;
          break;
        }
        case 'updated':
        default: {
          const aDate = new Date(a.updatedAt || a.createdAt || 0);
          const bDate = new Date(b.updatedAt || b.createdAt || 0);
          comparison = bDate - aDate;
          break;
        }
      }

      return this.sortDirection === 'asc' ? comparison : -comparison;
    });

    this.filteredProfiles = filtered;
  }

  startCreate() {
    this.mode = 'create';
    this.editingProfile = null;
    this.formValues = { ...DEFAULT_FORM };
  }

  startEdit(id) {
    const profile = this.profiles.find(p => p.id === id);
    if (!profile) {
      this.toast.error('Profile not found');
      return;
    }

    this.mode = 'edit';
    this.editingProfile = profile;
    this.formValues = {
      name: profile.name || '',
      scopeClassification: profile.scopeClassification || '',
      regulatoryRegime: profile.regulatoryRegime || '',
      lineOfBusiness: profile.lineOfBusiness || '',
      department: profile.department || '',
      stakeholdersText: this.stringifyStakeholders(profile.primaryStakeholders)
    };
  }

  setMode(mode, profileId = null) {
    if (mode === 'create') {
      this.startCreate();
      return;
    }

    if (mode === 'edit' && profileId) {
      this.startEdit(profileId);
      return;
    }

    this.resetForm();
  }

  resetForm() {
    this.mode = 'list';
    this.editingProfile = null;
    this.formValues = { ...DEFAULT_FORM };
  }

  async handleFormSubmit(formData) {
    const payload = {
      name: formData.get('name')?.trim() || '',
      scopeClassification: formData.get('scopeClassification')?.trim() || '',
      regulatoryRegime: formData.get('regulatoryRegime')?.trim() || '',
      lineOfBusiness: formData.get('lineOfBusiness')?.trim() || '',
      department: formData.get('department')?.trim() || '',
      primaryStakeholders: this.parseStakeholders(formData.get('stakeholdersText'))
    };

    if (!payload.name) {
      this.toast.error('Name is required.');
      return;
    }

    try {
      if (this.mode === 'edit' && this.editingProfile?.id) {
        await this.api.updateOrganizationProfile(this.editingProfile.id, payload);
        this.toast.success('Organization profile updated');
      } else {
        await this.api.createOrganizationProfile(payload);
        this.toast.success('Organization profile created');
      }

      this.resetForm();
      await this.loadProfiles();
    } catch (error) {
      console.error('Failed to save profile', error);
      this.toast.error('Failed to save organization profile');
    }
  }

  async deleteProfile(id) {
    const profile = this.profiles.find(p => p.id === id);
    if (!profile) {
      this.toast.error('Profile not found');
      return;
    }

    const confirmed = window.confirm(`Delete organization profile "${profile.name}"? This cannot be undone.`);
    if (!confirmed) {
      return;
    }

    try {
      await this.api.deleteOrganizationProfile(id);
      this.toast.success('Organization profile deleted');
      if (this.editingProfile?.id === id) {
        this.resetForm();
      }
      await this.loadProfiles();
    } catch (error) {
      console.error('Failed to delete profile', error);
      this.toast.error('Failed to delete organization profile');
    }
  }

  updateView(container) {
    if (!container) {
      return;
    }

    const managerHost = container.querySelector('.profiles-manager');
    if (!managerHost) {
      return;
    }

    managerHost.innerHTML = `
      ${this.renderHeader()}
      ${this.renderToolbar()}
      ${this.renderBody()}
    `;

    this.attachEventHandlers(managerHost);
  }

  parseStakeholders(text) {
    if (!text) {
      return {};
    }

    const result = {};
    const lines = String(text).split('\n');
    for (const line of lines) {
      if (!line.trim()) {
        continue;
      }
      const [role, contacts] = line.split(':');
      if (!role) {
        continue;
      }
      const roleKey = role.trim();
      const contactList = contacts
        ? contacts.split(',').map(entry => entry.trim()).filter(Boolean)
        : [];
      result[roleKey] = contactList;
    }
    return result;
  }

  stringifyStakeholders(stakeholders) {
    if (!stakeholders || Object.keys(stakeholders).length === 0) {
      return '';
    }

    return Object.entries(stakeholders)
      .map(([role, contacts]) => {
        if (Array.isArray(contacts) && contacts.length > 0) {
          return `${role}: ${contacts.join(', ')}`;
        }
        return role;
      })
      .join('\n');
  }

  formatStakeholders(stakeholders) {
    if (!stakeholders) {
      return null;
    }

    return Object.entries(stakeholders).map(([role, contacts]) => ({
      role,
      contacts: Array.isArray(contacts) ? contacts.join(', ') : ''
    }));
  }

  formatRelativeTime(dateString) {
    if (!dateString) {
      return 'recently';
    }

    const date = new Date(dateString);
    if (Number.isNaN(date.getTime())) {
      return 'recently';
    }

    const now = new Date();
    const diff = now - date;

    const minutes = Math.floor(diff / 60000);
    if (minutes < 1) return 'just now';
    if (minutes < 60) return `${minutes}m ago`;
    const hours = Math.floor(minutes / 60);
    if (hours < 24) return `${hours}h ago`;
    const days = Math.floor(hours / 24);
    if (days < 7) return `${days}d ago`;

    return date.toLocaleDateString(undefined, {
      year: 'numeric',
      month: 'short',
      day: 'numeric'
    });
  }

  escapeHtml(text) {
    if (text == null) {
      return '';
    }
    const div = document.createElement('div');
    div.textContent = String(text);
    return div.innerHTML;
  }
}
