// Authentication and Role Management
class Auth {
  constructor() {
    this.currentUser = null;
    this.currentRole = 'Author'; // Default for demo
    this.init();
  }

  init() {
    // Set initial role from select
    const roleSelect = document.getElementById('role-switcher');
    if (roleSelect) {
      this.currentRole = roleSelect.value;
      this.updateUI();
    }
  }

  switchRole(role) {
    this.currentRole = role;
    
    // Update current user based on role
    this.currentUser = this.getUserForRole(role);
  // Persist role for server (Development: DevRoleClaimsMiddleware reads this)
  try { document.cookie = `_s7_roles=${encodeURIComponent(role)}; path=/`; } catch {}
  // Keep URL clean
  const url = new URL(window.location);
  url.searchParams.delete('role');
  window.history.replaceState({}, '', url);
    
    this.updateUI();
    
    // Refresh data for new role context
    if (window.app) {
      window.app.refreshData();
    }
  }

  getUserForRole(role) {
    const users = {
      'Reader': {
        id: 'read-001',
        name: 'Rob Reader',
        email: 'rob@company.com',
        roles: ['Reader']
      },
      'Author': {
        id: 'auth-001',
        name: 'Alice Author',
        email: 'alice@company.com',
        roles: ['Reader', 'Author']
      },
      'Moderator': {
        id: 'mod-001',
        name: 'Maya Moderator',
        email: 'maya@company.com',
        roles: ['Reader', 'Author', 'Moderator']
      },
      'Admin': {
        id: 'admin-001',
        name: 'Alex Admin',
        email: 'alex@company.com',
        roles: ['Reader', 'Author', 'Moderator', 'Admin']
      }
    };
    return users[role] || users['Reader'];
  }

  updateUI() {
    // Update user info display
    const currentUserEl = document.getElementById('current-user');
    const currentRoleEl = document.getElementById('current-role');
    
    if (this.currentUser) {
      if (currentUserEl) currentUserEl.textContent = this.currentUser.name;
      if (currentRoleEl) currentRoleEl.textContent = `Role: ${this.currentRole}`;
    }

    // Update body class for role-based CSS
    document.body.className = document.body.className.replace(/role-\w+/g, '');
    document.body.classList.add(`role-${this.currentRole.toLowerCase()}`);

    // Update mode navigation visibility
    this.updateModeNavigation();

    // Update View nav state (enabled if a document is selected)
    if (window.app && typeof window.app.updateViewNavState === 'function') {
      window.app.updateViewNavState();
    }
  }

  updateModeNavigation() {
    const modeButtons = {
      'browse': document.getElementById('mode-browse'),
      'edit': document.getElementById('mode-edit'),
      'moderate': document.getElementById('mode-moderate'),
      'admin': document.getElementById('mode-admin')
    };

    // Hide all modes first
    Object.values(modeButtons).forEach(btn => {
      if (btn) btn.style.display = 'none';
    });

    // Show modes based on role
    const rolePermissions = {
      'Reader': ['browse'],
      'Author': ['browse', 'edit'],
      'Moderator': ['browse', 'edit', 'moderate'],
      'Admin': ['browse', 'edit', 'moderate', 'admin']
    };

    const allowedModes = rolePermissions[this.currentRole] || ['browse'];
    allowedModes.forEach(mode => {
      const btn = modeButtons[mode];
      if (btn) btn.style.display = 'inline-flex';
    });

    // If current mode is not allowed, switch to browse
    const currentMode = document.querySelector('.mode-content.active')?.id?.replace('-mode', '');
    if (currentMode && !allowedModes.includes(currentMode)) {
      if (window.setMode) {
        window.setMode('browse');
      }
    }
  }

  hasRole(role) {
    if (!this.currentUser) return false;
    return this.currentUser.roles.includes(role);
  }

  hasPermission(permission) {
    // Simple permission mapping
    const permissions = {
      'create': this.hasRole('Author'),
      'edit': this.hasRole('Author'),
      'moderate': this.hasRole('Moderator'),
      'admin': this.hasRole('Admin')
    };
    return permissions[permission] || false;
  }
}

// Global auth instance
window.auth = new Auth();

// Global function for role switching
window.switchRole = function() {
  const roleSelect = document.getElementById('role-switcher');
  if (roleSelect && window.auth) {
    window.auth.switchRole(roleSelect.value);
  }
};
