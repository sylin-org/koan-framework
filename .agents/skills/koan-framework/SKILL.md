```markdown
# koan-framework Development Patterns

> Auto-generated skill from repository analysis

## Overview
This skill teaches the core development patterns and conventions used in the `koan-framework` TypeScript repository. It covers file naming, import/export styles, commit message conventions, and testing patterns, providing a practical reference for contributors to maintain consistency and quality in the codebase.

## Coding Conventions

### File Naming
- **Style:** kebab-case  
  **Example:**  
  ```
  user-service.ts
  data-model.ts
  ```

### Import Style
- **Relative imports** are used throughout the codebase.  
  **Example:**  
  ```typescript
  import { UserService } from './user-service';
  ```

### Export Style
- **Named exports** are preferred.  
  **Example:**  
  ```typescript
  export function createUser() { ... }
  export const USER_ROLE = 'admin';
  ```

### Commit Message Convention
- **Conventional commits** are used, with prefixes such as `docs`.  
  **Example:**  
  ```
  docs: update README with installation instructions
  ```

## Workflows

### Documentation Updates
**Trigger:** When updating or adding documentation files  
**Command:** `/update-docs`

1. Make your documentation changes.
2. Use a conventional commit message with the `docs` prefix.  
   Example: `docs: add API usage section to README`
3. Push your changes and open a pull request.

## Testing Patterns

- **Test File Pattern:** Test files are named with the `.test.` infix.  
  **Example:**  
  ```
  user-service.test.ts
  ```
- **Testing Framework:** Not explicitly detected; check existing test files for framework usage.
- **Location:** Test files are typically located alongside the files they test.

**Sample Test File:**
```typescript
import { createUser } from './user-service';

describe('createUser', () => {
  it('should create a user with default role', () => {
    // test implementation
  });
});
```

## Commands
| Command        | Purpose                                      |
|----------------|----------------------------------------------|
| /update-docs   | Start a documentation update workflow        |
```
