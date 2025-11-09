// Auto-generated TypeScript definitions for Koan MCP Code Mode
// Generated: 2025-11-09T21:32:01.2671952Z
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

// integrity-sha256: 993e600887d62a5f42e63cecff812912ab88515644b18670a3c0f2092f49a4cc
