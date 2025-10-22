# Fluent Guard Pattern

Natural language parameter validation for Koan Framework.

**📘 Full Documentation**: [docs/reference/core/guard-utilities.md](../../../../../docs/reference/core/guard-utilities.md)

## Overview

The fluent guard pattern provides readable, type-safe parameter validation with zero runtime overhead:

```csharp
public Todo(string title, int priority)
{
    _title = title.Must().NotBe.Blank();
    _priority = priority.Must().Be.Positive();
}
```

**Key Benefits:**
- **Natural language**: Reads as English sentences
- **Zero ceremony**: No `nameof()` required (uses `CallerArgumentExpression`)
- **Type-safe**: IntelliSense shows only valid guards for each type
- **Zero overhead**: `ref struct` design prevents allocations
- **Fail-fast**: Always throws immediately on validation failure

## Quick Start

### Reference Types

```csharp
// Null check
var name = userName.Must().NotBe.Null();
// Throws: ArgumentNullException if null
```

### Strings

```csharp
// Blank check (null, empty, or whitespace)
var title = todoTitle.Must().NotBe.Blank();
// Throws: ArgumentException if blank

// Chainable with string operations
var processed = input.Must().NotBe.Blank().Trim().ToUpper();
```

### Guids

```csharp
// Empty GUID check
var id = entityId.Must().NotBe.Empty();
// Throws: ArgumentOutOfRangeException if Guid.Empty
```

### Value Types

```csharp
// Default check
var timestamp = dateTime.Must().NotBe.Default();
// Throws: ArgumentException if default(DateTime)
```

### Numeric Guards

```csharp
// Positive (> 0)
var priority = todoPriority.Must().Be.Positive();
// Throws: ArgumentOutOfRangeException if <= 0

// Non-negative (>= 0)
var count = itemCount.Must().Be.NonNegative();
// Throws: ArgumentOutOfRangeException if < 0

// Range (inclusive)
var rating = userRating.Must().Be.InRange(1, 5);
// Throws: ArgumentOutOfRangeException if outside [1, 5]

// Minimum
var age = userAge.Must().Be.AtLeast(13);
// Throws: ArgumentOutOfRangeException if < 13

// Maximum
var percentage = score.Must().Be.AtMost(100);
// Throws: ArgumentOutOfRangeException if > 100
```

### Advanced Range Control

```csharp
// Between with configurable inclusivity
using Koan.Core.Utilities.Guard;

// Inclusive: [13, 120] - both bounds included
var age = userAge.Must().Be.Between(13, 120, RangeType.Inclusive);

// Exclusive: (0, 100) - both bounds excluded
var percentage = score.Must().Be.Between(0.0, 100.0, RangeType.Exclusive);

// InclusiveExclusive: [0, Length) - for array indexing
var index = userIndex.Must().Be.Between(0, array.Length, RangeType.InclusiveExclusive);

// ExclusiveInclusive: (0, MaxPrice]
var price = itemPrice.Must().Be.Between(0.0m, 999.99m, RangeType.ExclusiveInclusive);
```

### String Format Validation

```csharp
// Email validation (basic RFC check)
var email = userEmail.Must().Be.ValidEmail();
// Validates: user@example.com, test.user+tag@example.co.uk

// URL validation (HTTP/HTTPS)
var website = userWebsite.Must().Be.ValidUrl();
// Validates: https://example.com, http://example.com/path?query=value

// Custom regex pattern
var zipCode = userZip.Must().Be.MatchingPattern(@"^\d{5}(-\d{4})?$", "Invalid ZIP code");
var ssn = userSSN.Must().Be.MatchingPattern(@"^\d{3}-\d{2}-\d{4}$");
```

### Enum Validation

```csharp
public enum OrderStatus { Pending = 1, Confirmed = 2, Shipped = 3 }

// Validates enum value is defined (not arbitrary integer)
var status = userStatus.Must().Be.Defined<OrderStatus>();
// Throws ArgumentException if userStatus is (OrderStatus)999
```

### Collection Validation

```csharp
// Validates collection is not null or empty
IEnumerable<string> tags = userTags.Must().NotBe.Empty();
IList<Order> orders = orderList.Must().NotBe.Empty();
string[] roles = userRoles.Must().NotBe.Empty();
```

### Custom Predicates

```csharp
// Must NOT match predicate
var value = input.Must().NotBe.Where(
    x => x > 100,
    "Value cannot exceed 100"
);

// Must match predicate
var email = userEmail.Must().Be.Where(
    e => e.Contains("@"),
    "Must be valid email format"
);
```

## Complete Example

```csharp
public class Todo : Entity<Todo>
{
    private string _title;
    private string _description;
    private string _assignee;
    private int _priority;
    private decimal _estimatedHours;

    public Todo(
        string title,
        string description,
        string assignee,
        int priority,
        decimal estimatedHours)
    {
        // Guards clearly separated from business logic
        _title = title.Must().NotBe.Blank();
        _description = description.Must().NotBe.Null(); // Can be empty string
        _assignee = assignee.Must().NotBe.Blank();
        _priority = priority.Must().Be.InRange(1, 5);
        _estimatedHours = estimatedHours.Must().Be.Positive();
    }

    public void UpdatePriority(int newPriority)
    {
        _priority = newPriority.Must().Be.InRange(1, 5);
    }
}
```

## When to Use Guards

✅ **Use guards for:**
- Entity constructor parameter validation
- Service method parameter validation
- Property setter validation
- Defensive programming (preventing invalid state)

❌ **Don't use guards for:**
- User-facing form validation (use `ModelState` or `InputValidator`)
- API input validation (use `DataAnnotations` or `FluentValidation`)
- Business rule validation (use explicit domain logic)

## Guards vs. Input Validators

### Guards (Fail-Fast)

```csharp
// Throws on FIRST error, stops execution
public Todo(string title, int priority)
{
    _title = title.Must().NotBe.Blank(); // Throws here if invalid
    _priority = priority.Must().Be.Positive(); // Never reached if title fails
}
```

**Use for:** Preventing invalid state from entering the system

### Input Validators (Batch Errors)

```csharp
// Collects ALL errors, returns structured feedback
[HttpPost]
public IActionResult CreateTodo([FromBody] CreateTodoRequest request)
{
    if (!ModelState.IsValid)
        return BadRequest(ModelState); // Returns all validation errors

    // Guards only used after input validation passes
    var todo = new Todo(request.Title.Must().NotBe.Blank(), ...);
    return Ok(todo);
}
```

**Use for:** Providing good UX by showing all validation errors at once

## Available Guards

### Negative Guards (`Must().NotBe`)

| Guard | Types | Description |
|-------|-------|-------------|
| `Null()` | Reference types | Throws if null |
| `Blank()` | `string` | Throws if null/empty/whitespace |
| `Empty()` | `Guid` | Throws if `Guid.Empty` |
| `Empty()` | `IEnumerable<T>`, `IList<T>`, `T[]` | Throws if null or empty collection |
| `Default()` | Value types | Throws if `default(T)` |
| `Where(predicate, message)` | Any | Throws if predicate returns true |

### Positive Guards (`Must().Be`)

| Guard | Types | Description |
|-------|-------|-------------|
| `Positive()` | `int`, `long`, `decimal`, `double` | Throws if <= 0 |
| `NonNegative()` | `int`, `long` | Throws if < 0 |
| `InRange(min, max)` | `int`, `long`, `decimal` | Throws if outside [min, max] |
| `Between(min, max, RangeType)` | `int`, `long`, `decimal`, `double` | Configurable range inclusivity |
| `AtLeast(min)` | `int`, `long` | Throws if < min |
| `AtMost(max)` | `int`, `long` | Throws if > max |
| `ValidEmail()` | `string` | Basic email format validation |
| `ValidUrl()` | `string` | HTTP/HTTPS URL validation |
| `MatchingPattern(pattern, message)` | `string` | Custom regex validation |
| `Defined<TEnum>()` | `enum` | Validates enum is defined |
| `Where(predicate, message)` | Any | Throws if predicate returns false |

**See the [full documentation](../../../../../docs/reference/core/guard-utilities.md) for detailed examples and usage patterns.**

## Extensibility

Add custom guards via extension methods:

```csharp
public static class KoanGuardExtensions
{
    // Koan-specific: GUID v7 validation
    public static Guid IsGuidV7(this Be<Guid> be)
    {
        if (be.Value.Version() != 7)
            throw new ArgumentException("Expected GUID v7", be.ParamName);
        return be.Value;
    }

    // Domain-specific: Email validation
    public static string IsValidEmail(this Be<string> be)
    {
        if (!EmailValidator.IsValid(be.Value))
            throw new ArgumentException("Invalid email format", be.ParamName);
        return be.Value;
    }
}

// Usage
var id = entityId.Must().Be.IsGuidV7();
var email = userEmail.Must().Be.IsValidEmail();
```

## Implementation Details

### Zero Allocation Design

Guards use `ref struct` carriers that exist only on the stack:

```csharp
public readonly ref struct Must<T>
{
    internal readonly T Value;
    internal readonly string? ParamName;

    // Stack-only, inlined by JIT
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Must(T value, string? paramName) { ... }
}
```

**Result:** IL equivalent to direct `ArgumentException.ThrowIfXxx()` calls

### Parameter Name Capture

Uses `CallerArgumentExpression` to automatically capture parameter names:

```csharp
public static Must<T> Must<T>(this T value,
    [CallerArgumentExpression(nameof(value))] string? param = null)
    => new(value, param);
```

**Before:**
```csharp
ArgumentNullException.ThrowIfNull(userName, nameof(userName));
//                                          ^^^^^^^^^^^^^^^^^ manual
```

**After:**
```csharp
userName.Must().NotBe.Null();
// Parameter name "userName" captured automatically
```

## Performance

Benchmarks show guards have identical performance to manual validation:

| Pattern | Allocations | Time | IL Size |
|---------|-------------|------|---------|
| Direct `ThrowIfXxx` | 0 bytes | ~5ns | Baseline |
| Fluent `.Must` | 0 bytes | ~5ns | +0-2 bytes |

**Conclusion:** Use guards freely - there's no performance penalty.

## Migration Guide

### From Manual Guards

```csharp
// Before
ArgumentNullException.ThrowIfNull(value, nameof(value));
ArgumentException.ThrowIfNullOrWhiteSpace(title, nameof(title));
ArgumentOutOfRangeException.ThrowIfNegativeOrZero(priority, nameof(priority));

// After
value.Must().NotBe.Null();
title.Must().NotBe.Blank();
priority.Must().Be.Positive();
```

### From Third-Party Libraries

```csharp
// Ardalis.GuardClauses
Guard.Against.Null(value, nameof(value));
Guard.Against.NullOrWhiteSpace(title, nameof(title));

// Fluent Guards
value.Must().NotBe.Null();
title.Must().NotBe.Blank();
```

## Testing

Guards are fully tested in `tests/Suites/Core/Koan.Core.Tests/GuardTests.cs`:

- All guard methods covered
- Exception types and messages verified
- Parameter name capture validated
- Chaining behavior tested

Run tests:
```bash
dotnet test tests/Suites/Core/Koan.Core.Tests
```

## See Also

- [Proposal: Fluent Guard Pattern](../../../../docs/proposals/PROP-fluent-guard-pattern.md)
- [Koan Framework Entity Modeling Guide](../../../../docs/guides/)
- [.NET CallerArgumentExpression](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/attributes/caller-information)
