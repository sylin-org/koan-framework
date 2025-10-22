#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Koan.Core.Utilities.Guard;

/// <summary>
/// Negative guard facet providing "must not be" validations.
/// Stack-only ref struct to ensure zero allocations.
/// </summary>
/// <typeparam name="T">The type of value being validated</typeparam>
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

/// <summary>
/// Type-specific extension methods for NotBe&lt;T&gt; to provide type-safe guards.
/// These only appear in IntelliSense for appropriate types.
/// </summary>
public static class NotBeExtensions
{
    /// <summary>
    /// Throws if reference type is null; returns value otherwise.
    /// </summary>
    /// <typeparam name="T">The reference type</typeparam>
    /// <param name="notBe">The NotBe carrier</param>
    /// <returns>The validated non-null value</returns>
    /// <exception cref="ArgumentNullException">When value is null</exception>
    /// <example>
    /// <code>
    /// var name = userName.Must.NotBe.Null();
    /// // name is guaranteed non-null after this point
    /// </code>
    /// </example>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Null<T>(this NotBe<T> notBe) where T : class
    {
        ArgumentNullException.ThrowIfNull(notBe.Value, notBe.ParamName);
        return notBe.Value;
    }

    /// <summary>
    /// Throws if value type equals default(T); returns value otherwise.
    /// </summary>
    /// <typeparam name="T">The value type</typeparam>
    /// <param name="notBe">The NotBe carrier</param>
    /// <returns>The validated non-default value</returns>
    /// <exception cref="ArgumentException">When value equals default(T)</exception>
    /// <example>
    /// <code>
    /// var timestamp = dateTime.Must.NotBe.Default();
    /// // timestamp is not default(DateTime)
    /// </code>
    /// </example>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Default<T>(this NotBe<T> notBe) where T : struct
    {
        if (EqualityComparer<T>.Default.Equals(notBe.Value, default!))
            throw new ArgumentException("Value cannot be default.", notBe.ParamName);
        return notBe.Value;
    }

    /// <summary>
    /// Throws if string is null, empty, or whitespace; returns value otherwise.
    /// </summary>
    /// <param name="notBe">The NotBe carrier</param>
    /// <returns>The validated non-blank string</returns>
    /// <exception cref="ArgumentException">When string is null, empty, or whitespace</exception>
    /// <example>
    /// <code>
    /// var title = todoTitle.Must.NotBe.Blank();
    /// // title is guaranteed non-blank (not null/empty/whitespace)
    /// </code>
    /// </example>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string Blank(this NotBe<string> notBe)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(notBe.Value, notBe.ParamName);
        return notBe.Value;
    }

    /// <summary>
    /// Throws if Guid is empty (all zeros); returns value otherwise.
    /// </summary>
    /// <param name="notBe">The NotBe carrier</param>
    /// <returns>The validated non-empty Guid</returns>
    /// <exception cref="ArgumentException">When Guid is empty</exception>
    /// <example>
    /// <code>
    /// var id = entityId.Must.NotBe.Empty();
    /// // id is guaranteed not to be Guid.Empty
    /// </code>
    /// </example>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Guid Empty(this NotBe<Guid> notBe)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(notBe.Value, Guid.Empty, notBe.ParamName);
        return notBe.Value;
    }

    /// <summary>
    /// Throws if predicate returns true (i.e., disallowed condition met); returns value otherwise.
    /// </summary>
    /// <typeparam name="T">The type of value being validated</typeparam>
    /// <param name="notBe">The NotBe carrier</param>
    /// <param name="disallowed">Predicate that returns true for invalid values</param>
    /// <param name="message">Custom error message</param>
    /// <returns>The validated value</returns>
    /// <exception cref="ArgumentException">When predicate returns true</exception>
    /// <example>
    /// <code>
    /// var age = userAge.Must.NotBe.Where(a => a > 150, "Age cannot exceed 150");
    /// </code>
    /// </example>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Where<T>(this NotBe<T> notBe, Func<T, bool> disallowed, string? message = null)
    {
        if (disallowed(notBe.Value))
            throw new ArgumentException(message ?? "Value violates rule.", notBe.ParamName);
        return notBe.Value;
    }

    #region Collection Guards

    /// <summary>
    /// Throws if collection is null or empty; returns value otherwise.
    /// </summary>
    /// <typeparam name="T">The element type</typeparam>
    /// <param name="notBe">The NotBe carrier</param>
    /// <returns>The validated non-empty collection</returns>
    /// <exception cref="ArgumentException">When collection is null or empty</exception>
    /// <example>
    /// <code>
    /// var items = userItems.Must.NotBe.Empty();
    /// // items is guaranteed to have at least one element
    /// </code>
    /// </example>
    public static IEnumerable<T> Empty<T>(this NotBe<IEnumerable<T>> notBe)
    {
        if (notBe.Value == null || !notBe.Value.Any())
            throw new ArgumentException("Collection cannot be null or empty.", notBe.ParamName);
        return notBe.Value;
    }

    /// <summary>
    /// Throws if list is null or empty; returns value otherwise.
    /// </summary>
    /// <typeparam name="T">The element type</typeparam>
    /// <param name="notBe">The NotBe carrier</param>
    /// <returns>The validated non-empty list</returns>
    /// <exception cref="ArgumentException">When list is null or empty</exception>
    public static IList<T> Empty<T>(this NotBe<IList<T>> notBe)
    {
        if (notBe.Value == null || notBe.Value.Count == 0)
            throw new ArgumentException("List cannot be null or empty.", notBe.ParamName);
        return notBe.Value;
    }

    /// <summary>
    /// Throws if array is null or empty; returns value otherwise.
    /// </summary>
    /// <typeparam name="T">The element type</typeparam>
    /// <param name="notBe">The NotBe carrier</param>
    /// <returns>The validated non-empty array</returns>
    /// <exception cref="ArgumentException">When array is null or empty</exception>
    public static T[] Empty<T>(this NotBe<T[]> notBe)
    {
        if (notBe.Value == null || notBe.Value.Length == 0)
            throw new ArgumentException("Array cannot be null or empty.", notBe.ParamName);
        return notBe.Value;
    }

    #endregion
}
