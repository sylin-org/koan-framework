# Defensive Publication: Zero-Allocation Fluent Guard Clauses via Ref Struct Carriers with CallerArgumentExpression

**Publication Type:** Defensive Patent Publication (prior art establishment)
**Inventor:** Leo Botinelly (Leonardo Milson Botinelly Soares)
**Date of Publication:** 2026-03-24
**Framework:** Koan Framework v0.6.3 (.NET 10)
**Status:** Implemented, tested, shipped in production

---

## 1. Title and Abstract

### Title

Zero-Allocation Fluent Guard Clauses via Readonly Ref Struct Carriers with Compiler-Inferred Parameter Names Using CallerArgumentExpression

### Abstract

A method and system for performing fail-fast parameter validation in managed-language applications using a fluent, natural-language API that produces zero heap allocations at runtime. The invention combines three independently known language features -- `readonly ref struct` value types, aggressive method inlining via `[MethodImpl(MethodImplOptions.AggressiveInlining)]`, and compiler-captured parameter names via `[CallerArgumentExpression]` -- into a novel composite architecture. A generic extension method `.Must()` initiates a validation chain that flows through stack-only intermediate "carrier" structs (`Must<T>`, `Be<T>`, `NotBe<T>`), each of which the JIT compiler eliminates entirely during compilation. The result is validation code that reads as natural language (`title.Must.NotBe.Blank()`, `priority.Must.Be.Positive()`) while compiling down to the same machine code as hand-written `if`/`throw` statements. Type-specific guard methods are provided through constrained extension methods on the carrier structs, ensuring that only applicable validations appear in IntelliSense for a given type.

---

## 2. Technical Description

### 2.1 Problem Statement

Parameter validation (guard clauses) is a universal concern in application development. Developers face a tradeoff between three desirable properties:

1. **Readability**: Validation should express intent clearly, ideally reading as natural language.
2. **Performance**: Validation should add zero overhead beyond the conditional check itself -- no heap allocations, no virtual dispatch, no boxing of value types.
3. **Diagnostics**: When validation fails, error messages should automatically include the name of the offending parameter without requiring the developer to redundantly pass a string literal.

Prior art achieves at most two of these three properties simultaneously. This invention achieves all three.

### 2.2 Architecture Overview

The system consists of five interrelated components:

```
  value.Must.NotBe.Blank()
    |     |    |      |
    |     |    |      +-- Terminal guard method (throws or returns validated value)
    |     |    +--------- Negative-facet carrier (readonly ref struct NotBe<T>)
    |     +-------------- Routing carrier (readonly ref struct Must<T>)
    |                     with facet-selector properties (.Be, .NotBe)
    +-------------------- Extension method entry point with
                          [CallerArgumentExpression] parameter capture
```

**Component 1: Entry Point Extension Method (`MustExtensions.Must<T>`)**

```csharp
public static class MustExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Must<T> Must<T>(this T value,
        [CallerArgumentExpression(nameof(value))] string? param = null)
        => new(value, param);
}
```

The C# compiler resolves `[CallerArgumentExpression(nameof(value))]` at the call site, substituting the source-code text of the expression passed as `value`. When the developer writes `title.Must.NotBe.Blank()`, the compiler emits `title.Must("title").NotBe.Blank()`, embedding the string `"title"` as a compile-time constant. No reflection or runtime inspection occurs.

**Component 2: Routing Carrier (`Must<T>`)**

```csharp
public readonly ref struct Must<T>
{
    internal readonly T Value;
    internal readonly string? ParamName;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Must(T value, string? paramName)
    {
        Value = value;
        ParamName = paramName;
    }

    public NotBe<T> NotBe
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(Value, ParamName);
    }

    public Be<T> Be
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(Value, ParamName);
    }
}
```

`Must<T>` is declared as `readonly ref struct`, which means:
- It can only exist on the stack (the CLR enforces this; it cannot be boxed, stored in fields of reference types, or captured by lambdas/async methods).
- It has no heap footprint whatsoever.
- The JIT compiler can (and does) inline the entire struct away, leaving only the raw value and the parameter-name string pointer on the stack or in registers.

The `.Be` and `.NotBe` properties serve as facet selectors, routing the chain to the appropriate set of terminal guard methods.

**Component 3: Positive-Facet Carrier (`Be<T>`)**

```csharp
public readonly ref struct Be<T>
{
    internal readonly T Value;
    internal readonly string? ParamName;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Be(T value, string? paramName)
    {
        Value = value;
        ParamName = paramName;
    }
}
```

Terminal guard methods are provided as constrained extension methods on `Be<T>`. By constraining to specific closed generic types (e.g., `Be<int>`, `Be<string>`, `Be<long>`), only applicable guards appear in IntelliSense:

| Extension method | Constrained to | Behavior |
|------------------|---------------|----------|
| `Positive()` | `Be<int>`, `Be<long>`, `Be<decimal>`, `Be<double>` | Throws `ArgumentOutOfRangeException` if value <= 0 |
| `NonNegative()` | `Be<int>`, `Be<long>` | Throws `ArgumentOutOfRangeException` if value < 0 |
| `InRange(min, max)` | `Be<int>`, `Be<long>`, `Be<decimal>` | Throws `ArgumentOutOfRangeException` if value outside [min, max] |
| `AtLeast(min)` | `Be<int>`, `Be<long>` | Throws `ArgumentOutOfRangeException` if value < min |
| `AtMost(max)` | `Be<int>`, `Be<long>` | Throws `ArgumentOutOfRangeException` if value > max |
| `Between(min, max, RangeType)` | `Be<int>`, `Be<long>`, `Be<decimal>`, `Be<double>` | Throws `ArgumentOutOfRangeException` with configurable bound inclusivity |
| `Where(predicate, message?)` | `Be<T>` (open generic) | Throws `ArgumentException` if predicate returns false |
| `ValidEmail()` | `Be<string>` | Throws `ArgumentException` if string does not match email pattern |
| `ValidUrl()` | `Be<string>` | Throws `ArgumentException` if string is not a valid HTTP/HTTPS URL |
| `MatchingPattern(pattern, message?)` | `Be<string>` | Throws `ArgumentException` if string does not match regex |
| `Defined<TEnum>()` | `Be<TEnum> where TEnum : struct, Enum` | Throws `ArgumentException` if enum value is not defined |

**Component 4: Negative-Facet Carrier (`NotBe<T>`)**

```csharp
public readonly ref struct NotBe<T>
{
    internal readonly T Value;
    internal readonly string? ParamName;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal NotBe(T value, string? paramName)
    {
        Value = value;
        ParamName = paramName;
    }
}
```

Terminal guard methods on `NotBe<T>`:

| Extension method | Constrained to | Behavior |
|------------------|---------------|----------|
| `Null()` | `NotBe<T> where T : class` | Throws `ArgumentNullException` if value is null; returns non-null `T` |
| `Default()` | `NotBe<T> where T : struct` | Throws `ArgumentException` if value equals `default(T)` |
| `Blank()` | `NotBe<string>` | Throws `ArgumentException` if string is null, empty, or whitespace |
| `Empty()` | `NotBe<Guid>` | Throws `ArgumentOutOfRangeException` if Guid equals `Guid.Empty` |
| `Empty()` | `NotBe<IEnumerable<T>>`, `NotBe<IList<T>>`, `NotBe<T[]>` | Throws `ArgumentException` if collection is null or empty |
| `Where(predicate, message?)` | `NotBe<T>` (open generic) | Throws `ArgumentException` if predicate returns true |

**Component 5: Range Type Enumeration**

```csharp
public enum RangeType
{
    Inclusive,          // [min, max]
    Exclusive,          // (min, max)
    InclusiveExclusive, // [min, max)
    ExclusiveInclusive  // (min, max]
}
```

The `Between()` method accepts a `RangeType` parameter (defaulting to `Inclusive`) and uses a `switch` expression to select the appropriate comparison logic. Error messages include mathematical interval notation (e.g., `[10, 20)`) to precisely communicate the violated constraint.

### 2.3 JIT Elimination Mechanism

The critical performance property is that the entire carrier chain is eliminated by the JIT compiler. This occurs through a specific sequence of optimizations:

1. **Inlining**: Every method and property accessor in the chain is annotated with `[MethodImpl(MethodImplOptions.AggressiveInlining)]`. The JIT compiler inlines each call, replacing the method call with its body at the call site.

2. **Struct promotion**: After inlining, the JIT recognizes that each `readonly ref struct` instance is (a) stack-allocated, (b) never escapes the method, and (c) has only field accesses remaining. It "promotes" the struct fields to local variables or registers.

3. **Copy propagation**: The value and parameter-name string flow through the chain as simple register-to-register moves or are propagated as constants, eliminating intermediate copies.

4. **Dead code elimination**: Any struct construction that was inlined and whose fields are individually tracked is fully eliminated. No memory is reserved for the struct itself.

The net result: `title.Must.NotBe.Blank()` compiles to machine code equivalent to:

```csharp
ArgumentException.ThrowIfNullOrWhiteSpace(title, "title");
```

No `Must<string>` instance is allocated. No `NotBe<string>` instance is allocated. The only runtime cost is the null/whitespace check itself plus, on the failure path, the exception object allocation.

### 2.4 Value-Returning Design

Every terminal guard method returns the validated value. This enables assignment-site validation:

```csharp
public Todo(string title, int priority, Guid id)
{
    _title    = title.Must.NotBe.Blank();       // returns string
    _priority = priority.Must.Be.Positive();     // returns int
    _id       = id.Must.NotBe.Empty();           // returns Guid
}
```

The return value has the same type as the input. For `NotBe.Null()`, the return type is the non-nullable `T` (constrained via `where T : class`), which enables the C# compiler's nullable reference type analysis to recognize the value as non-null after the guard.

### 2.5 Extension Method Overloading for Type Safety

Guard methods that are only meaningful for specific types (e.g., `Positive()` for numeric types, `Blank()` for strings) are implemented as extension methods on the closed generic carrier type (e.g., `this Be<int> be`). The C# compiler's overload resolution ensures that:

- `intValue.Must.Be.Positive()` compiles (extension method exists for `Be<int>`)
- `stringValue.Must.Be.Positive()` does not compile (no extension method for `Be<string>`)
- `stringValue.Must.NotBe.Blank()` compiles (extension method exists for `NotBe<string>`)
- `intValue.Must.NotBe.Blank()` does not compile (no extension method for `NotBe<int>`)

This is enforced at compile time with zero runtime cost. IntelliSense only shows applicable guards for the value's type.

### 2.6 Regex Safety for String Format Guards

The `ValidEmail()` and `ValidUrl()` guards use pre-compiled `Regex` instances with `RegexOptions.Compiled` and a 100ms timeout to prevent ReDoS attacks:

```csharp
private static readonly Regex EmailRegex = new(
    @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
    RegexOptions.Compiled | RegexOptions.IgnoreCase,
    TimeSpan.FromMilliseconds(100));
```

The `MatchingPattern()` guard creates a one-shot `Regex` with the same 100ms timeout, and catches `RegexMatchTimeoutException` to throw a clear `ArgumentException`.

---

## 3. Novel Combination of Elements

No single element described here is independently novel. The novelty lies in their specific combination and the emergent properties that combination produces.

### 3.1 Known Elements

| Element | Prior art |
|---------|-----------|
| `readonly ref struct` (C# 7.2+) | Microsoft language specification; used in `Span<T>`, `ReadOnlySpan<T>` |
| `[MethodImpl(AggressiveInlining)]` | .NET BCL; widely used in System.Runtime |
| `[CallerArgumentExpression]` (C# 10+) | Microsoft language specification; used in `ArgumentNullException.ThrowIfNull` |
| Fluent API pattern | Builder patterns, LINQ, FluentValidation |
| Extension methods on generic structs | Various .NET libraries |
| Guard clause libraries | Ardalis.GuardClauses, Dawn.Guard, Guard.NET, FluentValidation |

### 3.2 Novel Combination

The specific invention is the composition of these elements into a system where:

1. **A fluent chain of three or more struct types is entirely eliminated by the JIT.** Prior fluent APIs (FluentValidation, Dawn.Guard) use class-based intermediates that allocate on each invocation. Using `readonly ref struct` for multi-stage fluent carriers is novel.

2. **Facet-based routing through property accessors (`Must.Be`, `Must.NotBe`) on ref struct types creates a natural-language grammar.** Prior guard libraries using ref structs (if any exist) use static method calls, not instance property navigation.

3. **`[CallerArgumentExpression]` flows through intermediate carrier structs** rather than being consumed at the first method call. The parameter name captured at `.Must()` propagates through `.NotBe` and is consumed by `.Blank()`, spanning three struct hops. Prior uses of `[CallerArgumentExpression]` are on terminal methods (e.g., `ArgumentNullException.ThrowIfNull`), not on fluent chain entry points.

4. **Type-safe guard selection via extension method overloading on closed generic ref struct types.** The combination of `readonly ref struct Be<T>` with extension methods like `Positive(this Be<int> be)` ensures compile-time type safety with zero runtime dispatch, while also controlling IntelliSense visibility. This pattern has not been applied to guard clause libraries.

5. **The value-returning design enables assignment-site validation** within constructors and factory methods, which is not achievable with `void`-returning static guard methods (e.g., `Guard.Against.Null(value, nameof(value))`).

### 3.3 Emergent Properties

The combination produces properties not present in any individual element:

- **Zero-allocation fluent validation**: Neither "zero allocation" nor "fluent API" is novel alone; achieving both simultaneously for guard clauses is.
- **Compile-time type-filtered IntelliSense**: The developer sees only guards applicable to the actual type being validated, with no runtime cost for the filtering.
- **Automatic parameter name propagation across a multi-hop fluent chain**: The parameter name is captured once and used potentially many struct hops later.
- **Natural-language syntax without parsing overhead**: The syntax `title.Must.NotBe.Blank()` reads as an English assertion but compiles to a single conditional branch.

---

## 4. Implementation Details

### 4.1 Complete Source File Inventory

All source files reside in the `Koan.Core` assembly under `Koan.Core.Utilities.Guard` namespace:

| File | Lines | Purpose |
|------|-------|---------|
| `MustExtensions.cs` | 39 | Entry-point extension method with `[CallerArgumentExpression]` |
| `Must.cs` | 47 | Routing carrier ref struct with `.Be` and `.NotBe` facet properties |
| `Be.cs` | 526 | Positive-facet carrier ref struct + extension methods for type-specific guards |
| `NotBe.cs` | 187 | Negative-facet carrier ref struct + extension methods for type-specific guards |
| `RangeType.cs` | 30 | Enum for configurable range bound inclusivity |

**Total: 829 lines across 5 files.**

### 4.2 Guard Method Catalog

**Positive guards (`Be<T>` extensions):**

```
Positive()        - int, long, decimal, double - value > 0
NonNegative()     - int, long                  - value >= 0
InRange(min,max)  - int, long, decimal         - min <= value <= max
AtLeast(min)      - int, long                  - value >= min
AtMost(max)       - int, long                  - value <= max
Between(min,max,RangeType) - int, long, decimal, double - configurable bounds
Where(predicate)  - any T                      - predicate(value) must be true
ValidEmail()      - string                     - basic email format
ValidUrl()        - string                     - HTTP/HTTPS URL format
MatchingPattern() - string                     - arbitrary regex match
Defined<TEnum>()  - struct, Enum               - Enum.IsDefined check
```

**Negative guards (`NotBe<T>` extensions):**

```
Null()            - class T                    - value != null
Default()         - struct T                   - value != default(T)
Blank()           - string                     - not null/empty/whitespace
Empty()           - Guid                       - value != Guid.Empty
Empty()           - IEnumerable<T>, IList<T>, T[]  - not null and count > 0
Where(predicate)  - any T                      - predicate(value) must be false
```

### 4.3 Exception Strategy

The implementation delegates to BCL throw helpers wherever possible to benefit from their JIT-optimized throw-helper pattern:

| Guard | Exception type | BCL throw helper used |
|-------|---------------|----------------------|
| `NotBe.Null()` | `ArgumentNullException` | `ArgumentNullException.ThrowIfNull` |
| `NotBe.Blank()` | `ArgumentException` | `ArgumentException.ThrowIfNullOrWhiteSpace` |
| `NotBe.Empty()` (Guid) | `ArgumentOutOfRangeException` | `ArgumentOutOfRangeException.ThrowIfEqual` |
| `Be.Positive()` | `ArgumentOutOfRangeException` | `ArgumentOutOfRangeException.ThrowIfNegativeOrZero` |
| `Be.NonNegative()` | `ArgumentOutOfRangeException` | `ArgumentOutOfRangeException.ThrowIfNegative` |
| `Be.InRange()` | `ArgumentOutOfRangeException` | `ArgumentOutOfRangeException.ThrowIfLessThan` + `ThrowIfGreaterThan` |
| `Be.AtLeast()` | `ArgumentOutOfRangeException` | `ArgumentOutOfRangeException.ThrowIfLessThan` |
| `Be.AtMost()` | `ArgumentOutOfRangeException` | `ArgumentOutOfRangeException.ThrowIfGreaterThan` |

BCL throw helpers are specifically designed to keep the throw path out-of-line, allowing the JIT to keep the hot (non-throwing) path compact and branch-prediction-friendly.

### 4.4 Test Coverage

The implementation is verified by 51 test cases in `GuardTests.cs` (993 lines), covering:

- Every terminal guard method on both facets
- Boundary conditions for all numeric guards
- All four `RangeType` variants with boundary-exact values
- Null, empty, whitespace, and non-blank string variations
- Valid and invalid email/URL formats
- Regex pattern matching with custom messages and timeout behavior
- Defined and undefined enum values
- Collection guards for `IEnumerable<T>`, `IList<T>`, and `T[]` (null, empty, populated)
- `[CallerArgumentExpression]` propagation verification (parameter name appears in exception)
- Post-guard chaining with downstream methods (e.g., `.Trim().ToUpper()`)
- Multi-property entity validation integration test

---

## 5. Prior Art Comparison

### 5.1 FluentValidation (Jeremy Skinner, 2008-)

FluentValidation is a rules-engine for complex input validation. It uses class-based validators (`AbstractValidator<T>`), property-rule chains, and returns collections of validation failures. It is designed for batch validation of user input, not fail-fast parameter checks. Every validation invocation allocates validator instances, rule objects, and result collections on the heap. FluentValidation serves a fundamentally different purpose (input validation vs. guard clauses) and operates at a different performance tier.

**Distinguishing factors:** This invention is for fail-fast guard clauses (single assertion, immediate throw), not batch validation. It allocates nothing on the heap. It uses ref struct carriers, not class hierarchies.

### 5.2 Ardalis.GuardClauses (Steve Smith, 2017-)

Ardalis.GuardClauses provides `Guard.Against.Null(value, nameof(value))` style validation. The API is static-method-based. It requires the developer to manually pass `nameof(parameterName)` as a string argument. The `Against` property returns a `Guard` class instance (heap-allocated). Extension methods on the `Guard` class provide individual checks.

**Distinguishing factors:** This invention captures parameter names automatically via `[CallerArgumentExpression]`. It uses `readonly ref struct` (zero allocation) instead of a class instance. It provides a two-facet grammar (`Must.Be`/`Must.NotBe`) instead of a flat `Guard.Against` namespace. It returns the validated value, enabling assignment-site usage.

### 5.3 Dawn.Guard (Adam Langley)

Dawn.Guard provides `Guard.Argument(value, nameof(value)).NotNull().NotEmpty()` style chaining. The chain is built on a `Guard.ArgumentInfo<T>` class, which is heap-allocated. Multiple guards can be chained on the same value. Like Ardalis, it requires manual `nameof()` parameter naming.

**Distinguishing factors:** This invention uses `readonly ref struct` carriers instead of class-based `ArgumentInfo<T>`. It captures parameter names automatically. Its two-facet grammar (`Be`/`NotBe`) is structurally different from Dawn's flat method chain. It delegates to BCL throw helpers for JIT-optimized throw paths.

### 5.4 Guard.NET (George Dyrrachitis)

Guard.NET provides `Guard.That(value).IsNotNull()` static methods. It is minimalist and does not support fluent chaining. It requires manual parameter name passing. It uses standard class allocations internally.

**Distinguishing factors:** This invention provides multi-stage fluent chaining via ref struct carriers. It auto-captures parameter names. It provides type-filtered IntelliSense via extension method overloading on closed generic ref struct types.

### 5.5 .NET BCL `ArgumentNullException.ThrowIfNull` (Microsoft, .NET 6+)

.NET 6 introduced `ArgumentNullException.ThrowIfNull(value)` with `[CallerArgumentExpression]`. This is a single static method for a single check. It does not provide fluent chaining, faceted grammar, or type-specific guard selection.

**Distinguishing factors:** This invention wraps and extends the BCL throw helpers into a fluent, multi-facet, type-safe API while preserving the same JIT optimization characteristics. The `[CallerArgumentExpression]` parameter captured at the `.Must()` entry point flows through intermediate ref struct carriers to the terminal guard -- a usage pattern not present in the BCL.

### 5.6 Summary Comparison Matrix

| Property | FluentValidation | Ardalis | Dawn | Guard.NET | BCL ThrowIf | **This Invention** |
|----------|-----------------|---------|------|-----------|-------------|---------------------|
| Zero heap allocation | No | No | No | No | Yes | **Yes** |
| Fluent chaining | Yes | No | Yes | No | No | **Yes** |
| Auto parameter name | No | No | No | No | Yes | **Yes** |
| Type-filtered IntelliSense | Partial | No | No | No | N/A | **Yes** |
| Value-returning | No | No | Yes | No | No | **Yes** |
| Faceted grammar (Be/NotBe) | No | Flat | Flat | Flat | N/A | **Yes** |
| JIT-eliminated carriers | N/A | N/A | N/A | N/A | N/A | **Yes** |
| Ref struct architecture | No | No | No | No | N/A | **Yes** |

---

## 6. Potential Applications and Extensions

### 6.1 Broader Applications of the Pattern

The ref-struct fluent carrier pattern described here is applicable beyond guard clauses:

1. **Fluent logging**: `logger.At.Debug.WithContext("key", value).Write("message")` where intermediate carriers are ref structs, achieving zero-allocation structured logging on the hot path (log level filtered out).

2. **Fluent assertion libraries for testing**: Test assertion chains (`result.Should.Be.GreaterThan(0)`) using ref struct carriers would eliminate per-assertion allocations in large test suites.

3. **Fluent query building**: `query.Where.Field("name").Is.EqualTo("value")` with ref struct carriers for building filter expressions without intermediate heap objects.

4. **Fluent configuration validation**: Startup-time configuration checks (`config.Section("Database").Must.Have.Key("ConnectionString")`) using the same carrier pattern.

5. **Pipeline stage carriers**: Any multi-stage fluent pipeline where intermediate states are ephemeral and should not escape the calling method.

### 6.2 Language-Portable Variations

The core pattern is portable to other managed languages with value types and inlining:

- **Rust**: The pattern maps naturally to Rust's zero-cost abstractions, where the carriers would be `#[repr(transparent)]` newtypes with `#[inline(always)]` methods.
- **Swift**: Swift's `@inlinable` attribute and value-type structs could implement the same pattern.
- **Java (Project Valhalla)**: When value types ship in Java, this pattern would apply with `value class` carriers.
- **Kotlin/JVM**: Using `@JvmInline value class` for carriers, though JVM inlining is less predictable than CLR JIT.

### 6.3 Framework-Specific Extensions in Koan

Within the Koan Framework, the guard clause system integrates with:

- **Entity constructors**: `Entity<T>` subclasses use guards in constructors for domain invariant enforcement.
- **API controllers**: `EntityController<T>` uses guards for request parameter validation before entity operations.
- **Configuration validation**: `AddKoanOptionsWithValidation<T>()` uses guards internally for options validation at startup.

---

## 7. Antagonist Cycle

This section challenges every novelty claim made above and evaluates whether the claims survive scrutiny.

### Challenge 1: "Ref structs for fluent APIs are obvious to anyone skilled in the art."

**Counter-argument:** `readonly ref struct` has been available since C# 7.2 (2017). In the nine years since, no guard clause library has adopted ref struct carriers. FluentValidation, Ardalis.GuardClauses, Dawn.Guard, and Guard.NET all use class-based intermediates. The .NET BCL itself uses static methods rather than fluent ref struct chains for its `ThrowIf` helpers. If the combination were obvious, at least one of these widely-used libraries would have adopted it. The absence of prior implementation across a mature ecosystem with strong performance culture (the .NET community regularly optimizes for zero allocation) is evidence that the specific combination is non-obvious.

**Verdict:** Claim survives. The nine-year gap between feature availability and this specific combination is meaningful evidence of non-obviousness.

### Challenge 2: "CallerArgumentExpression through a fluent chain is trivially adding a parameter to each struct."

**Counter-argument:** The mechanical act of passing a string through structs is indeed trivial. However, the design decision to capture the expression at the chain entry point (`.Must()`) and propagate it through intermediate carriers to a terminal method is a specific architectural choice. The alternative designs (capturing at each terminal method, or requiring `nameof()`) are what every other library chose. The insight that `[CallerArgumentExpression]` on an extension method's `this` parameter captures the receiver expression, and that this captured string can serve as the parameter name for exceptions thrown several call hops later, is a non-obvious application of the feature.

**Verdict:** Claim survives, but with reduced scope. The novelty is in the architectural decision to capture once and propagate, not in the string-passing mechanism itself.

### Challenge 3: "The JIT elimination claim is just normal struct inlining -- nothing special about this use."

**Counter-argument:** This is the strongest challenge. JIT inlining of structs is indeed a well-known optimization. The contribution here is demonstrating that a three-stage fluent chain (entry -> routing -> terminal) can be reliably eliminated, and designing the struct layout specifically to enable this elimination (two fields per carrier, aggressive inlining on every accessor, internal constructors to prevent misuse). The design is intentionally shaped to stay within JIT inlining budgets. A naive implementation with larger structs, non-inlined methods, or virtual dispatch would defeat the optimization.

**Verdict:** Claim partially survives. The JIT elimination is a known mechanism; the contribution is the deliberate engineering of the struct chain to remain within the JIT's inlining and promotion thresholds.

### Challenge 4: "Type-filtered IntelliSense via extension methods on closed generic types is a standard C# pattern."

**Counter-argument:** This is a standard C# pattern. However, applying it to `readonly ref struct` carriers for guard clauses -- where the type parameter determines which validation operations are semantically meaningful -- is a specific application that has not appeared in prior art. The combination with ref structs adds a constraint (ref structs cannot implement interfaces, limiting polymorphic alternatives) that makes extension method overloading the only viable approach, which is worth documenting but is not independently novel.

**Verdict:** Claim does not survive as independent novelty. This is a standard application of a known pattern to a new context.

### Challenge 5: "The faceted grammar (Be/NotBe) is just two properties -- any developer could do this."

**Counter-argument:** The grammatical structure is simple. Its value is in the API design, not in implementation complexity. However, a defensive publication does not require implementation complexity -- it requires that the specific design be documented as prior art to prevent later patent claims. The two-facet grammar, combined with ref struct carriers and CallerArgumentExpression propagation, forms a specific composite that should be documented regardless of individual element simplicity.

**Verdict:** Claim is appropriately scoped for a defensive publication. The grammar design is documented as part of the composite invention, not as independently novel.

### Challenge 6: "The BCL throw helper delegation is just calling existing methods -- no novelty."

**Counter-argument:** Correct. Delegating to BCL throw helpers (`ArgumentNullException.ThrowIfNull`, `ArgumentOutOfRangeException.ThrowIfNegativeOrZero`, etc.) is a best-practice implementation choice, not a novel contribution. It is documented here for completeness of the implementation description, not as a novelty claim.

**Verdict:** Claim withdrawn. BCL delegation is an implementation detail, not a novel element.

### Surviving Novelty Claims After Antagonist Cycle

1. **The composite architecture**: `readonly ref struct` carriers + `[CallerArgumentExpression]` propagation + aggressive inlining + fluent faceted grammar, achieving all three of readability, zero allocation, and automatic diagnostics simultaneously.

2. **The ref struct fluent carrier pattern**: Using `readonly ref struct` types as intermediate carriers in a fluent API chain, specifically engineered for JIT elimination, applied to guard clauses. The nine-year gap between C# 7.2's introduction of ref structs and this application is evidence of non-obviousness.

3. **CallerArgumentExpression capture-once-propagate-many**: Capturing the caller's argument expression at the chain entry point and propagating it through intermediate ref struct carriers to terminal methods, rather than capturing at each terminal method.

---

*This document is published as prior art under the defensive publication doctrine. Its purpose is to prevent the patenting of the described techniques by establishing a public, dated record of the invention. The described techniques are made freely available for use by any party.*

*Framework source code: `src/Koan.Core/Utilities/Guard/` in the Koan Framework repository.*
*Test code: `tests/Suites/Core/Koan.Core.Tests/GuardTests.cs`*
