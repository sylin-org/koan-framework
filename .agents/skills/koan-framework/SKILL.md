```markdown
# koan-framework Development Patterns

> Auto-generated skill from repository analysis

## Overview

The `koan-framework` repository is a C# codebase focused on providing a modular, adapter-driven data and web integration framework. It features robust contract abstractions, a comprehensive adapter surface matrix for testing, and a strong emphasis on maintainability through clear workflows and documentation. The repository is organized for extensibility, with repeatable patterns for adding adapters, evolving contracts, and ensuring high test coverage.

## Coding Conventions

- **File Naming:**  
  Use PascalCase for all file names.  
  _Example:_  
  ```
  AdapterSurfaceSpecsBase.cs
  VectorAdapterFactory.cs
  ```

- **Import Style:**  
  Use relative imports within the project.  
  _Example:_  
  ```csharp
  using Koan.Data.Abstractions;
  using Koan.Cache.Decorators;
  ```

- **Export Style:**  
  Use named exports (public classes, interfaces, etc.).  
  _Example:_  
  ```csharp
  public interface IDataRepository { ... }
  public class CachedRepository : IDataRepository { ... }
  ```

- **Commit Messages:**  
  Use [Conventional Commits](https://www.conventionalcommits.org/):  
  - Prefixes: `feat`, `fix`, `test`, `docs`, `chore`, `refactor`
  - Example:  
    ```
    feat(adapter): add support for partitioned queries
    fix(vector): correct cache invalidation on transfer
    ```

## Workflows

### Adapter Surface Matrix Expansion
**Trigger:** When a new data or vector adapter is added, or when new CRUD/partition/transfer specs are needed across all adapters.  
**Command:** `/expand-adapter-matrix`

1. Add or update test projects under `tests/Suites/Web/AdapterSurface/` or `tests/Suites/Data/VectorAdapterSurface/` for each adapter.
2. Implement or update `IAdapterTestFactory` or `IVectorAdapterTestFactory` in the new test project.
3. Wire up spec base classes (e.g., `AdapterSurfaceSpecsBase`, `AdapterPartitionSpecsBase`, `AdapterTransferSpecsBase`, `VectorAdapterSurfaceSpecsBase`) in the test project.
4. Update shared test kit (`TestKit`) for new capabilities or spec bases as needed.
5. Update `README.md` in the relevant test kit to document adapter status and instructions.
6. Run the full matrix and update capability flags/skips as needed.

_Example:_
```csharp
public class MyAdapterTestFactory : IAdapterTestFactory { ... }
public class MyAdapterSurfaceSpecs : AdapterSurfaceSpecsBase { ... }
```

---

### Data Layer Contract Migration
**Trigger:** When a breaking change or simplification is needed in the data-layer contracts.  
**Command:** `/migrate-data-contracts`

1. Update or remove interfaces in `src/Koan.Data.Abstractions/` (e.g., `IDataRepositoryWithOptions`, `INamingProvider`).
2. Refactor all adapter implementations in `src/Connectors/Data/*` to match the new contract.
3. Update `RepositoryFacade`, `CachedRepository`, and any decorators to match the new signatures.
4. Update or remove related static caches or helpers (e.g., `AggregateConfigs`, `EntitySchemaGuard`).
5. Update all test projects and fakes/specs that referenced the old contracts.
6. Validate with the adapter surface matrix to ensure zero regression.

_Example:_
```csharp
// Before
public interface IDataRepositoryWithOptions { ... }

// After
public interface IDataRepository { ... }
```

---

### Adapter Bugfix Driven by Matrix
**Trigger:** When the adapter matrix exposes a bug or contract violation in a specific adapter.  
**Command:** `/fix-adapter-bug`

1. Identify failing or skipped specs in the matrix and diagnose root cause.
2. Update the affected adapter implementation (e.g., `src/Connectors/Data/*/*Repository.cs`).
3. If needed, update shared abstractions or test kit to support the fix.
4. Validate the fix by rerunning the full matrix and confirming all relevant specs pass.
5. Document the fix in the adapter surface `README.md` or relevant ADR if architectural.

---

### Documenting and Adding Workbooks
**Trigger:** When a new operational scenario, connector, or workflow needs step-by-step documentation.  
**Command:** `/add-workbook`

1. Create a new workbook in `docs/workbooks/` (e.g., `adding-a-connector.md`).
2. Update `docs/workbooks/README.md` to index the new workbook.
3. If needed, update or create a `_template.md` for contributors.
4. Update any cross-references in guides, ADRs, or scripts to point to the new workbook.
5. Update header comments in relevant scripts or workflows to reference the workbook.
6. If replacing an old guide, stub the old file to redirect to the new location.

---

### Feature Development with ADRs and Tests
**Trigger:** When a significant new feature or contract is added.  
**Command:** `/new-feature`

1. Draft an ADR in `docs/decisions/` describing the feature, rationale, and migration path.
2. Implement the feature in `src/` (may span multiple submodules).
3. Write or update tests in `tests/Suites/*` to cover the new feature end-to-end.
4. Update or create documentation as needed.
5. Validate via the relevant test suites or matrices.

---

### Routine Version Bump Release
**Trigger:** When preparing a new release (patch or minor).  
**Command:** `/bump-version`

1. Update `build/versions.props` with the new version numbers for all packages.
2. Commit with a `chore(release): bump` message.
3. No code changes; purely mechanical.

---

## Testing Patterns

- **Test File Naming:**  
  Test files use the `*Tests.cs` pattern.  
  _Example:_  
  ```
  AdapterSurfaceSpecsTests.cs
  VectorAdapterSurfaceSpecsTests.cs
  ```

- **Test Organization:**  
  - Tests are grouped under `tests/Suites/Web/AdapterSurface/` and `tests/Suites/Data/VectorAdapterSurface/`.
  - Shared test kits (e.g., `Koan.Web.AdapterSurface.TestKit`) provide base classes and utilities.
  - Spec base classes are used for consistency across adapters.

- **Test Framework:**  
  The specific test framework is not detected, but standard C# testing patterns apply.

_Example:_
```csharp
public class MyAdapterSurfaceSpecs : AdapterSurfaceSpecsBase
{
    [Fact]
    public void Should_Respect_Partitioning()
    {
        // Arrange
        // Act
        // Assert
    }
}
```

## Commands

| Command                | Purpose                                                                 |
|------------------------|-------------------------------------------------------------------------|
| /expand-adapter-matrix | Expand the adapter surface matrix for new adapters or test scenarios     |
| /migrate-data-contracts| Migrate or refactor data-layer contracts across all adapters             |
| /fix-adapter-bug       | Fix adapter-specific bugs surfaced by the adapter matrix                 |
| /add-workbook          | Add a new operational workbook or update documentation                   |
| /new-feature           | Start a new feature or architectural change with ADR and tests           |
| /bump-version          | Perform a routine version bump for a release                             |
```
