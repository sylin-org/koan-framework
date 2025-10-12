#nullable enable
using System.Runtime.CompilerServices;

namespace Koan.Core.Utilities.Guard;

/// <summary>
/// Entry point for fluent guard clause validation.
/// Provides natural language syntax: value.Must.NotBe.Null(), priority.Must.Be.Positive()
/// </summary>
/// <remarks>
/// <para>
/// Guards are for <strong>fail-fast parameter validation</strong>, not user-facing input validation.
/// Always throws immediately on failure. Use InputValidator for batch validation scenarios.
/// </para>
/// <para>
/// Usage:
/// <code>
/// public Todo(string title, int priority)
/// {
///     _title = title.Must.NotBe.Blank();
///     _priority = priority.Must.Be.Positive();
/// }
/// </code>
/// </para>
/// </remarks>
public static class MustExtensions
{
    /// <summary>
    /// Initiates a fluent guard clause chain.
    /// </summary>
    /// <typeparam name="T">The type of the value being validated</typeparam>
    /// <param name="value">The value to validate</param>
    /// <param name="param">Parameter name (automatically captured)</param>
    /// <returns>A fluent carrier for chaining guard validations</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Must<T> Must<T>(this T value,
        [CallerArgumentExpression(nameof(value))] string? param = null)
        => new(value, param);
}
