// S5.Recs UI constants (global)
// Centralizes magic numbers used across the browse UI so behavior is easy to tweak.
// Keep environment/config-like knobs in config.js; use constants here for UI logic.
window.S5Const = Object.freeze({
  // Well-known page paths and links
  PATHS: Object.freeze({
    HOME: 'index.html',
    DETAILS: 'details.html',
    DASHBOARD: 'dashboard.html'
  }),
  LINKS: Object.freeze({
    GITHUB_REPO: 'https://github.com/sylin-labs/Koan-framework'
  }),
  BRAND: Object.freeze({
    NAME: 'MediaHub'
  }),
  MEDIA_TYPES: Object.freeze({
    ANIME: 'Anime',
    MANGA: 'Manga',
    MOVIE: 'Movie',
    BOOK: 'Book'
  }),
  // Common UI text strings
  TEXT: Object.freeze({
    LOADING: 'Loading…',
    NO_RESULTS: 'No results',
    RESULTS_PREFIX: 'Showing ',
    RESULTS_SUFFIX: ' results',
    EXPAND: 'Expand',
    COLLAPSE: 'Collapse'
  }),
  RECS: Object.freeze({
    DEFAULT_PREFER_WEIGHT: 0.2,
    DEFAULT_MAX_PREFERRED_TAGS: 3,
    DEFAULT_DIVERSITY_WEIGHT: 0.1,
    PREFER_WEIGHT_MIN: 0.0,
    PREFER_WEIGHT_MAX: 1.0,
    PREFER_WEIGHT_STEP: 0.05,
    TAG_WEIGHT_LOW_MAX: 0.2,
    TAG_WEIGHT_HIGH_MIN: 0.6,
    TAG_BOOST_DEBOUNCE_MS: 50
  }),
  EPISODES: Object.freeze({
    SHORT_MAX: 12,
    MEDIUM_MAX: 25,
    LONG_MAX: 9999 // used as a practical upper bound
  }),
  CHAPTERS: Object.freeze({
    SHORT_MAX: 50,
    MEDIUM_MAX: 200,
    LONG_MAX: 9999 // used as a practical upper bound
  }),
  LIBRARY: Object.freeze({
    PAGE_SIZE: 500,
    WATCHLIST_PAGE_SIZE: 100
  }),
  UI: Object.freeze({
    REMOVE_CARD_TRANSITION_MS: 250,
  REMOVE_CARD_TIMEOUT_MS: 260,
  PREVIEW_SECTION_COUNT: 24
  }),
  INIT: Object.freeze({
    RETRY_BASE_MS: 1000,
    RETRY_MAX_MS: 8000
  }),
  TAGS: Object.freeze({
    PREFER_CATALOG_SIZE: 16,
    CHIPS_IN_CARD: 2,
    CHIPS_IN_LIST: 4,
    CHIPS_IN_DETAILS: 12
  }),
  DETAILS: Object.freeze({
    SIMILAR_TOPK: 12
  }),
  ADMIN: Object.freeze({
    IMPORT_DEFAULT_LIMIT: 200,
    // VECTOR_UPSERT_LIMIT removed - vector rebuild now processes ALL items by default
    QUICK_ACTIONS_REFRESH_DELAY_MS: 1500,
    MAX_PREFERRED_TAGS_MIN: 1,
    MAX_PREFERRED_TAGS_MAX: 5,
    DIVERSITY_WEIGHT_MIN: 0.0,
    DIVERSITY_WEIGHT_MAX: 0.2,
    DIVERSITY_WEIGHT_STEP: 0.05
  }),
  RATING: Object.freeze({
    STARS: 5,
    MIN: 1,
    MAX: 5,
    DEFAULT_POPULARITY_SCORE: 0.7,
    SCALE_MULTIPLIER: 5,
    ROUND_TO: 10, // round to 1 decimal place via *10 then /10
    STEP: 1
  }),
  YEAR: Object.freeze({
    WINDOW_YEARS: 30
  }),
  FILTERS: Object.freeze({
    DEBOUNCE_MS: 120
  }),
  // Backend endpoints used by the lightweight API client
  ENDPOINTS: Object.freeze({
    USERS: '/api/users',
    RECS_SETTINGS: '/admin/recs-settings',
    LIBRARY_BASE: '/api/library',
    RATE: '/api/recs/rate',
    RECS_QUERY: '/api/recs/query',
    MEDIA_BASE: '/api/media',
    MEDIA_BY_IDS: '/api/media/by-ids',
    MEDIA_TYPES: '/api/media-types',
    MEDIA_FORMATS: '/api/media-formats',
  TAGS: '/api/tags',
  GENRES: '/api/genres',
  // Admin endpoints
  ADMIN_STATS: '/admin/stats',
  ADMIN_SEED_START: '/admin/seed/start',
  ADMIN_SEED_VECTORS: '/admin/seed/vectors',
  ADMIN_TAGS_REBUILD: '/admin/tags/rebuild',
  ADMIN_GENRES_REBUILD: '/admin/genres/rebuild',
  ADMIN_TAGS_CENSOR: '/admin/tags/censor',
  ADMIN_TAGS_CENSOR_ADD: '/admin/tags/censor/add',
  ADMIN_TAGS_CENSOR_REMOVE: '/admin/tags/censor/remove',
  ADMIN_TAGS_CENSOR_CLEAR: '/admin/tags/censor/clear',
  // Well-known
  WK_HEALTH: '/.well-known/Koan/health',
  WK_OBSERVABILITY: '/.well-known/Koan/observability',
  WK_AGGREGATES: '/.well-known/Koan/aggregates'
  })
});
