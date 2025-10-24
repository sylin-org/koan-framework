import { PageHeader } from './PageHeader.js';
import { SearchFilter } from './SearchFilter.js';
import { EmptyState } from './EmptyState.js';
import { LoadingState } from './LoadingState.js';

const DEFAULT_FORM = {
  name: '',
  active: false,
  fields: []
};

export class OrganizationProfilesManager {
  constructor(api, eventBus, toast, router) {
    this.api = api;
    this.eventBus = eventBus;
    this.toast = toast;
    this.router = router;

    this.pageHeader = new PageHeader(router, eventBus);
    this.searchFilter = new SearchFilter(eventBus, {
      searchPlaceholder: 'Search organization profiles by name…',
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
      subtitle: 'Define fields that should be extracted from ALL documents, regardless of pipeline type.',
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
          <p>Define organizational fields that will be extracted from every document, regardless of pipeline type.</p>
        </header>
        <form data-profile-form>
          <div class="form-grid">
            <label class="form-field">
              <span class="form-label">Profile Name</span>
              <input type="text" name="name" value="${this.escapeHtml(this.formValues.name)}" required maxlength="128" placeholder="e.g., Geisinger Healthcare" />
              <span class="form-help">User-friendly label for this profile</span>
            </label>
          </div>

          <div class="form-section">
            <h3>Field Definitions</h3>
            <p>Define fields that should be extracted from all documents when this profile is active.</p>

            <div class="fields-editor" data-fields-editor>
              ${this.renderFieldsEditor()}
            </div>

            <button type="button" class="btn btn-secondary" data-action="add-field">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="12" y1="5" x2="12" y2="19"></line>
                <line x1="5" y1="12" x2="19" y2="12"></line>
              </svg>
              Add Field
            </button>
          </div>

          <div class="form-actions">
            <button type="submit" class="btn btn-primary">${isEdit ? 'Save Changes' : 'Create Profile'}</button>
            <button type="button" class="btn btn-secondary" data-action="cancel-profile">Cancel</button>
          </div>
        </form>
      </section>
    `;
  }

  renderFieldsEditor() {
    if (this.formValues.fields.length === 0) {
      return '<div class="empty-fields">No fields defined. Click "Add Field" to create your first field.</div>';
    }

    return this.formValues.fields.map((field, index) => `
      <div class="field-editor-row" data-field-index="${index}">
        <div class="field-editor-inputs">
          <input
            type="text"
            name="field-name-${index}"
            value="${this.escapeHtml(field.fieldName || '')}"
            placeholder="Field Name (e.g., RegulatoryRegime)"
            required
          />
          <input
            type="text"
            name="field-description-${index}"
            value="${this.escapeHtml(field.description || '')}"
            placeholder="Description (optional)"
          />
          <input
            type="text"
            name="field-examples-${index}"
            value="${this.escapeHtml((field.examples || []).join(', '))}"
            placeholder="Examples: HIPAA, SOC 2, ..."
          />
        </div>
        <button type="button" class="btn btn-danger btn-icon" data-action="remove-field" data-field-index="${index}" title="Remove field">
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <line x1="18" y1="6" x2="6" y2="18"></line>
            <line x1="6" y1="6" x2="18" y2="18"></line>
          </svg>
        </button>
      </div>
    `).join('');
  }

  renderProfilesList() {
    if (this.isLoading) {
      return LoadingState.render('card', { count: 4 });
    }

    if (this.profiles.length === 0) {
      return EmptyState.render({
        variant: 'onboarding',
        title: 'No organization profiles yet',
        description: 'Create a profile to define fields that should be extracted from ALL documents across all pipelines.',
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
    const isActive = profile.active || false;
    const fieldCount = (profile.fields || []).length;

    return `
      <article class="profile-card card-lift ${isActive ? 'profile-card-active' : ''}" data-profile-id="${this.escapeHtml(profile.id)}">
        <header class="profile-card-header">
          <div class="profile-card-title-row">
            <h3>${this.escapeHtml(profile.name)}</h3>
            ${isActive ? '<span class="badge badge-success">Active</span>' : ''}
          </div>
          <div class="profile-card-actions">
            ${!isActive ? `<button class="btn btn-primary btn-sm" data-action="activate-profile" data-profile-id="${this.escapeHtml(profile.id)}" title="Activate profile">
              Activate
            </button>` : ''}
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
        <div class="profile-card-body">
          <div class="profile-field-count">
            <strong>${fieldCount}</strong> ${fieldCount === 1 ? 'field' : 'fields'} will be extracted from all documents
          </div>
          ${fieldCount > 0 ? `
            <ul class="profile-fields-list">
              ${(profile.fields || []).slice(0, 5).map(field => `
                <li>
                  <strong>${this.escapeHtml(field.fieldName)}</strong>
                  ${field.examples && field.examples.length > 0 ? `<span class="field-examples">(e.g., ${this.escapeHtml(field.examples.slice(0, 2).join(', '))})</span>` : ''}
                </li>
              `).join('')}
              ${fieldCount > 5 ? `<li class="more-fields">+${fieldCount - 5} more...</li>` : ''}
            </ul>
          ` : '<div class="no-fields">No fields defined</div>'}
        </div>
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

      // Add field button
      const addFieldBtn = form.querySelector('[data-action="add-field"]');
      if (addFieldBtn) {
        addFieldBtn.addEventListener('click', () => {
          this.formValues.fields.push({ fieldName: '', description: '', examples: [] });
          this.updateView(container);
        });
      }

      // Remove field buttons
      form.querySelectorAll('[data-action="remove-field"]').forEach(btn => {
        btn.addEventListener('click', () => {
          const index = parseInt(btn.getAttribute('data-field-index'), 10);
          this.formValues.fields.splice(index, 1);
          this.updateView(container);
        });
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

    // Create profile buttons
    container.querySelectorAll('[data-action="create-profile"]').forEach(button => {
      button.addEventListener('click', (event) => {
        event.preventDefault();
        this.startCreate();
        this.updateView(container);
      });
    });

    // Cancel buttons
    container.querySelectorAll('[data-action="cancel-profile"]').forEach(button => {
      button.addEventListener('click', (event) => {
        event.preventDefault();
        this.resetForm();
        this.updateView(container);
      });
    });

    // Edit buttons
    container.querySelectorAll('[data-action="edit-profile"]').forEach(button => {
      button.addEventListener('click', (event) => {
        event.preventDefault();
        const id = button.getAttribute('data-profile-id');
        this.startEdit(id);
        this.updateView(container);
      });
    });

    // Activate buttons
    container.querySelectorAll('[data-action="activate-profile"]').forEach(button => {
      button.addEventListener('click', async (event) => {
        event.preventDefault();
        const id = button.getAttribute('data-profile-id');
        await this.activateProfile(id);
        this.updateView(container);
      });
    });

    // Delete buttons
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
    this.formValues = { ...DEFAULT_FORM, fields: [] };
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
      active: profile.active || false,
      fields: (profile.fields || []).map(f => ({
        fieldName: f.fieldName || '',
        description: f.description || '',
        examples: f.examples || []
      }))
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
    this.formValues = { ...DEFAULT_FORM, fields: [] };
  }

  async handleFormSubmit(formData) {
    const name = formData.get('name')?.trim() || '';

    if (!name) {
      this.toast.error('Name is required.');
      return;
    }

    // Collect field definitions from form
    const fields = [];
    for (let i = 0; i < this.formValues.fields.length; i++) {
      const fieldName = formData.get(`field-name-${i}`)?.trim();
      const description = formData.get(`field-description-${i}`)?.trim() || '';
      const examplesStr = formData.get(`field-examples-${i}`)?.trim() || '';
      const examples = examplesStr ? examplesStr.split(',').map(e => e.trim()).filter(Boolean) : [];

      if (fieldName) {
        fields.push({
          fieldName,
          description,
          examples,
          displayOrder: i
        });
      }
    }

    const payload = {
      name,
      active: this.formValues.active,
      fields
    };

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

  async activateProfile(id) {
    const profile = this.profiles.find(p => p.id === id);
    if (!profile) {
      this.toast.error('Profile not found');
      return;
    }

    try {
      await this.api.request(`/api/organizationprofiles/${id}/activate`, {
        method: 'POST'
      });
      this.toast.success(`Activated profile: ${profile.name}`);
      await this.loadProfiles();
    } catch (error) {
      console.error('Failed to activate profile', error);
      this.toast.error('Failed to activate profile');
    }
  }

  async deleteProfile(id) {
    const profile = this.profiles.find(p => p.id === id);
    if (!profile) {
      this.toast.error('Profile not found');
      return;
    }

    if (profile.active) {
      this.toast.error('Cannot delete the active profile. Activate another profile first.');
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
