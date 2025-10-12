# Proposal: Fluent Guard Pattern for Parameter Validation

**Status**: Accepted
**Date**: 2025-10-12
**Author**: Enterprise Architect
**Related**: Koan.Core utilities, Entity<T> constructor patterns

---

## Executive Summary

Koan Framework lacks a consistent, ergonomic pattern for parameter validation (guard clauses). Developers currently use verbose .NET guard methods that require `nameof()` and reduce code readability. This proposal introduces a **fluent guard pattern** using modern C# `CallerArgumentExpression` to provide natural language validation with zero runtime overhead.

**Key Benefits**:
- **Natural language**: `title.Must.NotBe.Blank()` reads as English
- **Zero ceremony**: No `nameof()` required
- **Type-safe**: IntelliSense guides to appropriate validations
- **Zero overhead**: `ref struct` design prevents allocations
- **Framework-aligned**: Consistent validation style across Koan modules

---

## Problem Statement

### Current State

```csharp
// Verbose .NET guards
public Todo(string title, int priority)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(title, nameof(title));
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(priority, nameof(priority));

    _title = title;
    _priority = priority;
}
```

**Issues**:
1. **Ceremony overhead**: `nameof()` on every parameter
2. **Poor readability**: Guard intent buried in method names
3. **IntelliSense pollution**: No clear separation between domain logic and validation
4. **Inconsistent patterns**: Developers might use different guard libraries or roll their own

### Real-World Impact

**Developer friction**:
- Entity constructors become cluttered with validation boilerplate
- Guards hard to distinguish from business logic
- No framework-wide validation style guidance

**Code clarity**:
```csharp
// Hard to scan: which is validation vs logic?
public Todo(string title, string assignee, int priority)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(title, nameof(title));
    ArgumentNullException.ThrowIfNull(assignee, nameof(assignee));
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(priority, nameof(priority));

    _title = title.Trim().Truncate(200);  // Business logic
    _assignee = assignee;
    _priority = priority;
}
```

---

## Proposed Solution

### Fluent Guard Pattern

```csharp
/// <summary>
/// Fluent guard extensions for parameter validation.
/// Provides natural language validation: value.Must.NotBe.Null()
/// Always throws immediately on failure - use InputValidator for batch validation.
/// </summary>
public static class MustExtensions
{
    public static Must<T> Must<T>(this T value,
        [CallerArgumentExpression("value")] string? param = null)
        => new(value, param);
}

public readonly ref struct Must<T>
{
    public NotBe<T> NotBe { get; }
    public Be<T> Be { get; }
}
```

### Usage Examples

```csharp
// Entity constructors
public class Todo : Entity<Todo>
{
    private string _title;
    private string _assignee;
    private int _priority;

    public Todo(string title, string assignee, int priority)
    {
        // Clear validation section
        _title = title.Must.NotBe.Blank();
        _assignee = assignee.Must.NotBe.Null();
        _priority = priority.Must.Be.Positive();
    }
}

// Chainable with business logic
_title = title.Must.NotBe.Blank().Trim().Truncate(200);
//              ^^^^^^^^^^^^^^^^^^^ validation
//                                  ^^^^^^^^^^^^^^^^^^^^ transformation

// Type-safe guards
var id = entityId.Must.NotBe.Empty();  // Only available for Guid
var age = userAge.Must.Be.InRange(1, 120);  // Only available for int
```

---

## Design Principles

### 1. Natural Language Readability

**Goal**: Guards should read as English sentences

```csharp
// Reads: "title must not be blank"
title.Must.NotBe.Blank()

// Reads: "priority must be positive"
priority.Must.Be.Positive()
```

### 2. Zero Runtime Overhead

**Implementation**: `ref struct` carriers prevent heap allocations

```csharp
public readonly ref struct Must<T>
{
    private readonly T _value;
    private readonly string? _param;

    // Stack-only, inlined by JIT
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Must(T value, string? param) { /* ... */ }
}
```

**Result**: IL equivalent to direct `ArgumentException.ThrowIfXxx()` calls

### 3. Type Safety via Extensions

**Pattern**: Type-specific guards only available where appropriate

```csharp
// Blank() only available for NotBe<string>
public static string Blank(this NotBe<string> notBe)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(notBe._value, notBe._param);
    return notBe._value;
}

// Positive() only available for Be<int>
public static int Positive(this Be<int> be)
{
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(be._value, be._param);
    return be._value;
}
```

**Benefit**: IntelliSense shows only valid validations for each type

### 4. Fail-Fast Semantics

**Design**: Always throw immediately, never accumulate

```csharp
// ALWAYS throws ArgumentException if blank
var title = input.Must.NotBe.Blank();

// Safe to use - guaranteed non-blank after guard
var length = title.Length;
```

**Rationale**: Guard clauses are for **preventing invalid state**, not UX validation (use `InputValidator` for batch validation scenarios)

---

## Implementation Structure

```
Koan.Core/
  Utilities/
    Guard/
      MustExtensions.cs   // Entry point: .Must property
      Must.cs             // Must<T> carrier struct
      NotBe.cs            // NotBe<T> facet + type-specific extensions
      Be.cs               // Be<T> facet + type-specific extensions
```

### Core Guards (Initial Release)

**Negative Guards** (`NotBe` facet):
- `Null()` - Reference types
- `Blank()` - Strings (null/empty/whitespace)
- `Empty()` - Guid
- `Default()` - Value types

**Positive Guards** (`Be` facet):
- `Positive()` - int (> 0)
- `InRange(min, max)` - int
- `Between(min, max)` - int (exclusive)

**Extensibility**: Teams can add custom guards via extension methods

```csharp
// Custom guard example
public static Guid IsGuidV7(this Be<Guid> be)
{
    if (be._value.Version() != 7)
        throw new ArgumentException("Expected GUID v7", be._param);
    return be._value;
}

// Usage
var id = entityId.Must.Be.IsGuidV7();
```

---

## Comparison with Alternatives

### Option 1: Direct Extensions (Rejected)

```csharp
// Pollutes root IntelliSense
title.NotBlank();
priority.Positive();
```

**Issues**:
- Every type gets guard methods in IntelliSense
- Harder to distinguish validation from domain logic
- No clear namespace/pattern for validation

### Option 2: Static Guard Class (Rejected)

```csharp
Guard.NotBlank(title, nameof(title));
Guard.Positive(priority, nameof(priority));
```

**Issues**:
- Still requires `nameof()`
- Not fluent/chainable
- Same pattern as existing .NET guards (no improvement)

### Option 3: Third-Party Library (Rejected)

**Existing libraries** (Ardalis.GuardClauses, Dawn.Guard):
- Predate `CallerArgumentExpression` (require `nameof()`)
- External dependency for trivial functionality
- Not aligned with Koan's fluent patterns

**Decision**: Implement our own - surface area is ~100 lines total

### Selected: Fluent Must Pattern (Accepted)

**Advantages**:
- Natural language readability
- Zero `nameof()` ceremony
- Type-safe IntelliSense
- Framework-aligned fluent style
- Zero external dependencies
- Trivial implementation (~150 LOC)

---

## Usage Patterns

### Entity Constructors

```csharp
public class User : Entity<User>
{
    private string _email;
    private string _name;
    private int _age;

    public User(string email, string name, int age)
    {
        _email = email.Must.NotBe.Blank();
        _name = name.Must.NotBe.Blank();
        _age = age.Must.Be.InRange(13, 150);
    }
}
```

### Service Methods

```csharp
public async Task<Todo> AssignTodo(Guid todoId, string assignee)
{
    var id = todoId.Must.NotBe.Empty();
    var user = assignee.Must.NotBe.Blank();

    var todo = await Todo.Get(id);
    todo.Assignee = user;
    await todo.Save();

    return todo;
}
```

### Property Setters

```csharp
public class Todo : Entity<Todo>
{
    private int _priority;

    public int Priority
    {
        get => _priority;
        set => _priority = value.Must.Be.InRange(1, 5);
    }
}
```

---

## NOT a Replacement For

### 1. Input Validation (Forms/APIs)

**Wrong**:
```csharp
// Don't use guards for user-facing validation
[HttpPost]
public IActionResult CreateTodo([FromBody] CreateTodoRequest request)
{
    request.Title.Must.NotBe.Blank();  // ❌ Throws on first error
    request.Email.Must.NotBe.Blank();  // Never reached if title fails
    // ...
}
```

**Right**:
```csharp
[HttpPost]
public IActionResult CreateTodo([FromBody] CreateTodoRequest request)
{
    // Use ModelState or InputValidator to collect ALL errors
    if (!ModelState.IsValid)
        return BadRequest(ModelState);  // Returns all validation errors

    // Guards only for defensive programming
    var todo = new Todo(request.Title.Must.NotBe.Blank());
    return Ok(todo);
}
```

### 2. Business Rule Validation

**Guards are for parameter validation**, not business logic:

```csharp
// Wrong: Business rule in guard
var amount = transfer.Amount.Must.Be.Where(
    a => a <= account.Balance,
    "Insufficient funds");  // ❌ Domain logic in guard

// Right: Explicit business validation
if (transfer.Amount > account.Balance)
    throw new InsufficientFundsException(account.Id, transfer.Amount);
```

---

## Migration & Adoption

### Opt-In Pattern

**Existing code**: No changes required - standard .NET guards continue to work

**New code**: Adopt fluent guards for improved readability

```csharp
// Old style (still valid)
ArgumentNullException.ThrowIfNull(value, nameof(value));

// New style (recommended for new code)
value.Must.NotBe.Null();
```

### Framework Guidance

**Documentation**: Update entity modeling guides to show fluent guard pattern

**Examples**: All framework samples use `.Must` pattern consistently

**Tooling**: Consider analyzer to suggest `.Must` over `ArgumentException.ThrowIfXxx`

---

## Performance Considerations

### Zero-Allocation Design

**`ref struct` eliminates heap allocations**:

```csharp
// No allocations - stack-only intermediates
var title = input.Must.NotBe.Blank();

// IL equivalent to:
ArgumentException.ThrowIfNullOrWhiteSpace(input, "input");
var title = input;
```

**Aggressive inlining** ensures JIT optimizes to direct guard calls

### Benchmarks (Expected)

| Pattern | Allocations | Time | IL Size |
|---------|-------------|------|---------|
| Direct `ThrowIfXxx` | 0 bytes | ~5ns | Baseline |
| Fluent `.Must` | 0 bytes | ~5ns | +0-2 bytes |
| Third-party guard libs | 0-24 bytes | ~8-15ns | +10-50 bytes |

**Result**: Fluent pattern has identical performance to manual guards

---

## Extensibility

### Custom Guards via Extensions

```csharp
// Koan-specific guards
public static class KoanGuardExtensions
{
    public static Guid IsGuidV7(this Be<Guid> be)
    {
        var guid = be.Value();
        if (guid.Version() != 7)
            throw new ArgumentException("Expected GUID v7", be.ParamName());
        return guid;
    }

    public static string IsValidEmail(this Be<string> be)
    {
        var email = be.Value();
        if (!EmailValidator.IsValid(email))
            throw new ArgumentException("Invalid email format", be.ParamName());
        return email;
    }
}

// Usage
var id = entityId.Must.Be.IsGuidV7();
var email = userEmail.Must.Be.IsValidEmail();
```

### Domain-Specific Guards

Teams can add business-specific validations:

```csharp
// Finance module guards
public static decimal Positive(this Be<decimal> be)
{
    var value = be.Value();
    if (value <= 0)
        throw new ArgumentOutOfRangeException(be.ParamName(), "Amount must be positive");
    return value;
}

// Usage
var amount = transaction.Amount.Must.Be.Positive();
```

---

## Implementation Plan

### Phase 1: Core Infrastructure
- [ ] Create `Koan.Core/Utilities/Guard/` directory
- [ ] Implement `MustExtensions.cs` (entry point)
- [ ] Implement `Must.cs` (carrier struct)
- [ ] Implement `NotBe.cs` with common guards
- [ ] Implement `Be.cs` with common guards

### Phase 2: Testing
- [ ] Unit tests for all guard methods
- [ ] Verify zero allocations (BenchmarkDotNet)
- [ ] Test error messages and parameter names
- [ ] Verify IntelliSense behavior

### Phase 3: Documentation
- [ ] Add usage examples to entity modeling guide
- [ ] Document pattern in framework conventions
- [ ] Create "Guards vs Validators" explainer
- [ ] Update code samples across docs

### Phase 4: Adoption
- [ ] Update framework examples to use `.Must` pattern
- [ ] Add to Koan.Core bootstrap templates
- [ ] Optional: Create analyzer to suggest pattern

---

## Acceptance Criteria

- [x] Architectural decision documented
- [ ] Zero-allocation `ref struct` implementation
- [ ] Core guards implemented: `Null`, `Blank`, `Empty`, `Default`, `Positive`, `InRange`
- [ ] Type-specific extension methods provide type safety
- [ ] Unit tests cover all guards and edge cases
- [ ] Performance benchmarks verify zero overhead
- [ ] Documentation explains Guards vs Validators distinction
- [ ] Framework examples updated to use fluent pattern
- [ ] No external dependencies

---

## Risks & Mitigations

### Risk 1: Developers Misuse for Input Validation

**Risk**: Teams use guards for API validation, missing out on batch error reporting

**Mitigation**:
- Clear documentation: "Guards = fail-fast, Validators = batch errors"
- Separate `InputValidator` utility for batch scenarios
- Code samples show both patterns appropriately

### Risk 2: Extension Method Discoverability

**Risk**: Custom guards via extensions might not be discoverable

**Mitigation**:
- IntelliSense naturally shows extension methods on facets
- Documentation includes extensibility guide
- Framework ships common Koan-specific guards as examples

### Risk 3: `ref struct` Limitations

**Risk**: `ref struct` cannot be used in async methods or lambda captures

**Mitigation**:
- Guards always synchronous (by design)
- Capture return value, not intermediate struct:
  ```csharp
  // Wrong (won't compile)
  await Task.Run(() => value.Must.NotBe.Null());

  // Right
  var validated = value.Must.NotBe.Null();
  await Task.Run(() => ProcessAsync(validated));
  ```

---

## Decision Rationale

### Why Not Ambient Scoping?

Earlier exploration included ambient `ValidationScope` for batch validation (ButMustValidation library analysis). **Rejected because**:

1. **Dual-mode complexity**: Same API with different behavior (immediate vs accumulated) increases cognitive load
2. **Unsafe return values**: Accumulated mode returns invalid values, creating footguns
3. **Hidden state**: Ambient context obscures validation intent
4. **Wrong abstraction**: Guard clauses and input validation are different use cases

**Resolution**: Separate concerns
- **Guards** (this proposal): Fail-fast parameter validation
- **Validators**: Explicit batch validation for forms/APIs (use DataAnnotations/FluentValidation)

### Why Implement vs Use Library?

**Trivial surface area**: ~150 lines of code total for core functionality

**Framework alignment**: Custom implementation allows:
- Koan-specific guards (GUID v7, entity patterns)
- Consistent style across all framework code
- Zero external dependencies
- Full control over API evolution

**Modern C# features**: `CallerArgumentExpression` eliminates need for `nameof()` - existing libraries predate this feature

---

## References

- C# `CallerArgumentExpression` attribute (C# 10)
- .NET `ArgumentException.ThrowIfXxx` patterns (.NET 6+)
- Koan Framework entity modeling patterns
- ButMustValidation library analysis (rejected for ambient scoping complexity)
- Alternative libraries: Ardalis.GuardClauses, Dawn.Guard

---

## Next Steps

1. ✅ **Document decision** (this proposal)
2. **Implement core guards** in `Koan.Core/Utilities/Guard/`
3. **Create unit tests** with coverage for all guards
4. **Benchmark performance** to verify zero overhead
5. **Update documentation** with usage patterns
6. **Adopt in framework examples** for consistency
7. **Ship in next Koan.Core release**
