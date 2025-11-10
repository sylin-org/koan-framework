// Auto-generated TypeScript definitions for Koan MCP Code Mode
// Generated: 2025-11-10T01:05:14.2831208Z
// Use these type definitions to guide code generation.
// Code execution is JavaScript-only (no TypeScript transpilation).

declare namespace Koan {
  // ──────────────────────────────────────────────────
  // Entity Domain - Auto-discovered entity operations
  // ──────────────────────────────────────────────────
  namespace Entities {

    // SearchAudience - Manage search audience profiles with category filtering and search tuning
    interface SearchAudience {
      /** Full-text query term. */
      q: string;
      /** JSON filter expression compiled into repository predicates. */
      filter: string;
      /** When true string comparisons ignore case sensitivity. */
      ignoreCase: boolean;
      /** Page number to request. */
      page: number;
      /** Number of items per page (default 50). */
      pageSize: number;
      /** Sort expression using field[:direction] format. */
      sort: string;
      /** Optional Accept header override (view negotiation). */
      accept: string;
      /** When true forces pagination even when repository returns a full set. */
      forcePagination: boolean;
      /** Additional query parameters forwarded to hooks. */
      extras: Record<string, any>;
      /** Dataset key when using multitenant routing. */
      set: string;
      /** Response shaping hint. */
      shape: "map" | "dict";
      /** Relationship expansion hints (e.g. with=all). */
      with: string;
    }

    interface ISearchAudienceOperations {
      /** List Manage search audience profiles with category filtering and search tuning records with paging, filtering, and shaping. */
      collection(params?: { filter?: any; pageSize?: number; page?: number; sort?: string; set?: string; with?: string }): { items: SearchAudience[]; page: number; pageSize: number; totalCount: number };

      /** Retrieve a Manage search audience profiles with category filtering and search tuning by identifier. */
      getById(id: string, options?: { set?: string; with?: string }): SearchAudience;

      /** Insert or update a Manage search audience profiles with category filtering and search tuning record. */
      upsert(model: SearchAudience, options?: { set?: string }): SearchAudience;

      /** Delete a Manage search audience profiles with category filtering and search tuning by identifier. */
      delete(id: string, options?: { set?: string }): number;

      /** Delete multiple Manage search audience profiles with category filtering and search tuning records by identifier. */
      deleteMany(ids: string[], options?: { set?: string }): number;

    }

    const SearchAudience: ISearchAudienceOperations;

    // SearchCategory - Manage search content categories with path-based auto-classification
    interface SearchCategory {
      /** Full-text query term. */
      q: string;
      /** JSON filter expression compiled into repository predicates. */
      filter: string;
      /** When true string comparisons ignore case sensitivity. */
      ignoreCase: boolean;
      /** Page number to request. */
      page: number;
      /** Number of items per page (default 50). */
      pageSize: number;
      /** Sort expression using field[:direction] format. */
      sort: string;
      /** Optional Accept header override (view negotiation). */
      accept: string;
      /** When true forces pagination even when repository returns a full set. */
      forcePagination: boolean;
      /** Additional query parameters forwarded to hooks. */
      extras: Record<string, any>;
      /** Dataset key when using multitenant routing. */
      set: string;
      /** Response shaping hint. */
      shape: "map" | "dict";
      /** Relationship expansion hints (e.g. with=all). */
      with: string;
    }

    interface ISearchCategoryOperations {
      /** List Manage search content categories with path-based auto-classification records with paging, filtering, and shaping. */
      collection(params?: { filter?: any; pageSize?: number; page?: number; sort?: string; set?: string; with?: string }): { items: SearchCategory[]; page: number; pageSize: number; totalCount: number };

      /** Retrieve a Manage search content categories with path-based auto-classification by identifier. */
      getById(id: string, options?: { set?: string; with?: string }): SearchCategory;

      /** Insert or update a Manage search content categories with path-based auto-classification record. */
      upsert(model: SearchCategory, options?: { set?: string }): SearchCategory;

      /** Delete a Manage search content categories with path-based auto-classification by identifier. */
      delete(id: string, options?: { set?: string }): number;

      /** Delete multiple Manage search content categories with path-based auto-classification records by identifier. */
      deleteMany(ids: string[], options?: { set?: string }): number;

    }

    const SearchCategory: ISearchCategoryOperations;
  }

  // ──────────────────────────────────────────────────
  // Output Domain - Communication with user
  // ──────────────────────────────────────────────────
  namespace Out {
    /** Send final answer to the user */
    function answer(text: string): void;

    /** Log informational message */
    function info(message: string): void;

    /** Log warning message */
    function warn(message: string): void;
  }
}

// ──────────────────────────────────────────────────
// Runtime Context - Available to JavaScript code
// ──────────────────────────────────────────────────
export interface CodeModeContext {
  SDK: typeof Koan;
}

// integrity-sha256: 20be835e09d243d08c47b42a1ff4388066b9672b5581d1005bd711a477cab587
