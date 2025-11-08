// Auto-generated TypeScript definitions for Koan MCP Code Mode
// Generated: 2025-11-08T22:08:39.2844728Z
// Use these type definitions to guide code generation.
// Code execution is JavaScript-only (no TypeScript transpilation).

declare namespace Koan {
  // ──────────────────────────────────────────────────
  // Entity Domain - Auto-discovered entity operations
  // ──────────────────────────────────────────────────
  namespace Entities {
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

// integrity-sha256: fa0cfcb2652957921bdc50fb393059d855bf35e8d27dbff43208e09953b3ab6c
