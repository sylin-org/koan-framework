/**
 * Router - Hash-based routing for URL preservation
 * Enables bookmarking, browser back/forward, and shareable URLs
 */
export class Router {
  constructor() {
    this.routes = new Map();
    this.currentRoute = null;
    this.currentParams = {};
  }

  /**
   * Define a route pattern
   * @param {string} pattern - Route pattern (e.g., 'analysis-types/:id/edit')
   * @param {Function} handler - Handler function receiving params object
   */
  route(pattern, handler) {
    this.routes.set(pattern, {
      pattern,
      handler,
      regex: this.patternToRegex(pattern),
      paramNames: this.extractParamNames(pattern)
    });
  }

  /**
   * Convert route pattern to regex
   * @param {string} pattern - Route pattern
   * @returns {RegExp} Regular expression for matching
   */
  patternToRegex(pattern) {
    // Convert :param to capturing group
    const regexPattern = pattern
      .replace(/\//g, '\\/')
      .replace(/:([^/]+)/g, '([^/]+)');
    return new RegExp(`^${regexPattern}$`);
  }

  /**
   * Extract parameter names from pattern
   * @param {string} pattern - Route pattern
   * @returns {string[]} Parameter names
   */
  extractParamNames(pattern) {
    const matches = pattern.match(/:([^/]+)/g);
    return matches ? matches.map(m => m.substring(1)) : [];
  }

  /**
   * Match a path against all routes
   * @param {string} path - Path to match
   * @returns {Object|null} Match result with handler and params
   */
  match(path) {
    for (const [pattern, route] of this.routes) {
      const match = path.match(route.regex);
      if (match) {
        // Extract parameters
        const params = {};
        route.paramNames.forEach((name, index) => {
          params[name] = match[index + 1];
        });

        return {
          handler: route.handler,
          params,
          pattern
        };
      }
    }
    return null;
  }

  /**
   * Navigate to a route
   * @param {string} path - Route path
   * @param {Object} params - Additional parameters (query string)
   * @param {boolean} replace - Replace history instead of push
   */
  navigate(path, params = {}, replace = false) {
    // Build hash URL
    let hash = `#/${path}`;

    // Add query parameters if provided
    const queryParams = Object.entries(params)
      .filter(([key, value]) => value != null && !path.includes(`:${key}`))
      .map(([key, value]) => `${encodeURIComponent(key)}=${encodeURIComponent(value)}`)
      .join('&');

    if (queryParams) {
      hash += `?${queryParams}`;
    }

    // Update browser URL
    if (replace) {
      window.location.replace(hash);
    } else {
      window.location.hash = hash;
    }
  }

  /**
   * Get current hash path (without # and query string)
   * @returns {string} Current path
   */
  getCurrentPath() {
    const hash = window.location.hash.substring(1); // Remove #
    const queryIndex = hash.indexOf('?');
    return queryIndex !== -1 ? hash.substring(0, queryIndex) : hash;
  }

  /**
   * Parse query string parameters
   * @returns {Object} Query parameters
   */
  getQueryParams() {
    const hash = window.location.hash;
    const queryIndex = hash.indexOf('?');

    if (queryIndex === -1) return {};

    const queryString = hash.substring(queryIndex + 1);
    const params = {};

    queryString.split('&').forEach(pair => {
      const [key, value] = pair.split('=');
      if (key) {
        params[decodeURIComponent(key)] = decodeURIComponent(value || '');
      }
    });

    return params;
  }

  /**
   * Start listening for hash changes
   * @param {Function} defaultHandler - Handler for unmatched routes
   */
  start(defaultHandler) {
    this.defaultHandler = defaultHandler;

    // Handle initial route
    this.handleRoute();

    // Listen for hash changes (back/forward, manual navigation)
    window.addEventListener('hashchange', () => {
      this.handleRoute();
    });
  }

  /**
   * Handle current route
   */
  handleRoute() {
    const path = this.getCurrentPath();
    const queryParams = this.getQueryParams();

    // Remove leading slash if present
    const cleanPath = path.startsWith('/') ? path.substring(1) : path;

    // Default to empty path for root
    const routePath = cleanPath || '';

    // Try to match route
    const matchResult = this.match(routePath);

    if (matchResult) {
      this.currentRoute = matchResult.pattern;
      this.currentParams = { ...matchResult.params, ...queryParams };
      matchResult.handler(this.currentParams);
    } else if (this.defaultHandler) {
      this.currentRoute = null;
      this.currentParams = queryParams;
      this.defaultHandler(routePath, queryParams);
    } else {
      console.warn(`No route matched for: ${routePath}`);
    }
  }

  /**
   * Get current route and params
   * @returns {Object} Current route info
   */
  getCurrentRoute() {
    return {
      route: this.currentRoute,
      params: this.currentParams,
      path: this.getCurrentPath()
    };
  }

  /**
   * Check if a route is currently active
   * @param {string} pattern - Route pattern to check
   * @returns {boolean} True if active
   */
  isActive(pattern) {
    return this.currentRoute === pattern;
  }

  /**
   * Build a URL for a route
   * @param {string} pattern - Route pattern
   * @param {Object} params - Route parameters
   * @returns {string} Full hash URL
   */
  buildUrl(pattern, params = {}) {
    let path = pattern;

    // Replace :param with actual values
    Object.entries(params).forEach(([key, value]) => {
      path = path.replace(`:${key}`, value);
    });

    return `#/${path}`;
  }
}
