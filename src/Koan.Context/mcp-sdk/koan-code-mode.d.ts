// Auto-generated TypeScript definitions for Koan MCP Code Mode
// Generated: 2025-11-07T16:35:55.7865552Z
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

// integrity-sha256: 4da8d06aae9fd36c6ede69ed73bb3dcc74987db9c07de50e255aef00a91417b7
