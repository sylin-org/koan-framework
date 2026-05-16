```markdown
# koan-framework Development Patterns

> Auto-generated skill from repository analysis

## Overview

The `koan-framework` repository is a C#-based codebase focused on building extensible, service-oriented backend systems, with integrated TypeScript/React UI. It emphasizes clean separation between models, controllers, services, and infrastructure, and supports rapid iteration on REST APIs, backend features, and UI synchronization. The project follows conventional commit patterns, clear coding conventions, and provides robust workflows for adding entities, features, infrastructure refactors, pagination, and more.

## Coding Conventions

- **File Naming:**  
  Use PascalCase for all file names.  
  _Example:_  
  ```
  ProjectController.cs
  SearchCategory.cs
  KoanAutoRegistrar.cs
  ```

- **Import Style:**  
  Mixed usage of explicit and wildcard imports.  
  _Example:_  
  ```csharp
  using System;
  using Koan.Service.KoanContext.Models;
  ```

- **Export Style:**  
  Default (single class per file, public by default).  
  _Example:_  
  ```csharp
  public class Project { ... }
  ```

- **Commit Messages:**  
  Use [Conventional Commits](https://www.conventionalcommits.org/), e.g.:  
  ```
  feat: add semantic search endpoint to SearchController
  fix: correct continuation token logic in Search.cs
  docs: update ADR for vector storage registry
  ```

## Workflows

### Add or Evolve Entity with REST API

**Trigger:** When introducing a new domain concept (e.g., Project, Tag) to manage via API/UI  
**Command:** `/new-entity-api`

1. **Create or update entity class** in `Models/`  
   _Example:_  
   ```csharp
   // Models/Project.cs
   public class Project { public int Id { get; set; } public string Name { get; set; } }
   ```
2. **Add or update controller** in `Controllers/`  
   _Example:_  
   ```csharp
   // Controllers/ProjectsController.cs
   [ApiController]
   [Route("api/projects")]
   public class ProjectsController : EntityController<Project> { ... }
   ```
3. **Register controller/service** in `KoanAutoRegistrar.cs` or `Startup/Program.cs`
4. **Optionally add seeder/initializer** for default data  
   _Example:_  
   ```csharp
   // Bootstrap/ProjectSeeder.cs
   public static void SeedProjects() { ... }
   ```
5. **Update API client/types** in UI  
   _Example:_  
   ```typescript
   // ui/src/api/types.ts
   export interface Project { id: number; name: string; }
   ```
6. **Expose new endpoints** (e.g., `/api/projects`)

---

### Feature Development: Service, Controller, UI

**Trigger:** When adding a new backend feature (e.g., semantic search, metrics)  
**Command:** `/new-feature`

1. **Implement service logic** in `Services/`  
   _Example:_  
   ```csharp
   // Services/Search.cs
   public class SearchService { ... }
   ```
2. **Create or update API controller** in `Controllers/`
3. **Update/add UI page/component**  
   _Example:_  
   ```tsx
   // ui/src/pages/SearchPage.tsx
   export function SearchPage() { ... }
   ```
4. **Update API client/types** in UI
5. **Wire up service/controller** in `Program.cs` or `KoanAutoRegistrar.cs`

---

### Infrastructure or Connector Refactor

**Trigger:** When refactoring/upgrading core infrastructure (adapters, registries, etc.)  
**Command:** `/refactor-adapter-infra`

1. **Define or update interface** in `Abstractions/`  
   _Example:_  
   ```csharp
   // INamingProvider.cs
   public interface INamingProvider { string GetName(...); }
   ```
2. **Update AdapterFactory implementations**  
   _Example:_  
   ```
   // SqliteAdapterFactory.cs, MongoAdapterFactory.cs, etc.
   ```
3. **Update registry logic** (e.g., `StorageNameRegistry.cs`)
4. **Update repositories** to use new registry/interface
5. **Update/add tests** to verify new behavior

---

### Backend-UI Iteration with Types

**Trigger:** When backend models change, requiring frontend type and UI updates  
**Command:** `/sync-backend-ui-types`

1. **Update backend model**  
   _Example:_  
   ```csharp
   // Models/SearchResultChunk.cs
   public class SearchResultChunk { ... }
   ```
2. **Update API controller** to return new structure
3. **Update TypeScript interfaces** in `ui/src/api/types.ts`
4. **Update UI components/pages**  
   _Example:_  
   ```tsx
   // ui/src/pages/SearchPage.tsx
   ```
5. **Rebuild UI assets** (`wwwroot/assets/`)

---

### Pagination or Continuation Token Implementation

**Trigger:** When adding/fixing paginated search results  
**Command:** `/add-pagination`

1. **Implement continuation token logic** in backend service  
   _Example:_  
   ```csharp
   // Services/Pagination.cs
   public string GenerateContinuationToken(...) { ... }
   ```
2. **Update API controller** to include `continuationToken` in response
3. **Update UI** to consume `continuationToken` and trigger additional fetches
4. **Update TypeScript types** if needed

---

### Test Suite Expansion or Update

**Trigger:** When adding new features or after refactoring core logic  
**Command:** `/add-tests`

1. **Create/update Spec files** in `tests/Suites/Context/Unit` or `tests/Suites/Data/Core`
2. **Align test entity definitions**  
   _Example:_  
   ```csharp
   // TransactionTestEntity.cs
   ```
3. **Update test pipeline usage** (e.g., TestPipeline API)
4. **Verify all tests pass**

---

### Documentation and ADR Update

**Trigger:** When introducing/updating significant architecture, features, or workflows  
**Command:** `/add-adr`

1. **Create/update ADRs or proposals** in `docs/decisions/*.md`
2. **Update guides/howto docs** for new patterns
3. **Cross-link related guides** and update frontmatter
4. **Document implementation status, rationale, and next steps**

---

## Testing Patterns

- **Test Framework:** Unknown (likely xUnit/NUnit for C#; some TypeScript tests detected)
- **Test File Patterns:**  
  - C#: `tests/Suites/Context/Unit/Koan.Tests.Context.Unit/Specs/**/*.cs`
  - TypeScript: `*.test.ts`
- **Spec Style:**  
  - Place specs under `Specs/` directories
  - Use clear, behavior-driven test names
  - Align test entities with production models

_Example C# test file:_
```csharp
// tests/Suites/Data/Core/Koan.Tests.Data.Core/Specs/Naming/DataVectorSeparation.Spec.cs
[Fact]
public void ShouldSeparateVectorsByName() { ... }
```

## Commands

| Command                | Purpose                                                      |
|------------------------|--------------------------------------------------------------|
| /new-entity-api        | Add or evolve a domain entity with REST API endpoints        |
| /new-feature           | Implement a new backend feature with service/controller/UI   |
| /refactor-adapter-infra| Refactor or upgrade core infrastructure/connector logic      |
| /sync-backend-ui-types | Align backend models with frontend TypeScript types and UI   |
| /add-pagination        | Implement or extend continuation token-based pagination      |
| /add-tests             | Add or update comprehensive test suites                      |
| /add-adr               | Add or update architectural decision records and documentation|
```
