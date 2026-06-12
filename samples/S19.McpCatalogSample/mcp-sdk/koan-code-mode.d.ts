// Auto-generated TypeScript definitions for Koan MCP Code Mode
// Generated: 2026-06-12T12:17:05.4199218Z
// Use these type definitions to guide code generation.
// Code execution is JavaScript-only (no TypeScript transpilation).

declare namespace Koan {
  // ──────────────────────────────────────────────────
  // Entity Domain - Auto-discovered entity operations
  // ──────────────────────────────────────────────────
  namespace Entities {

    // Catalog - Flagship product catalog
    interface Catalog {
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

    interface ICatalogOperations {
      /** List Flagship product catalog records with paging, filtering, and shaping. */
      collection(params?: { filter?: any; pageSize?: number; page?: number; sort?: string; set?: string; with?: string }): { items: Catalog[]; page: number; pageSize: number; totalCount: number };

      /** Retrieve a Flagship product catalog by identifier. */
      getById(id: string, options?: { set?: string; with?: string }): Catalog;

    }

    const Catalog: ICatalogOperations;
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

// integrity-sha256: 4678bb42c9d1e0d67aeb02dd47fd17ec6fc3c05d0540af2dec17bdce23ed63bb
