#nullable enable
using System;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Koan.Core.Utilities.Guard;

/// <summary>
/// Positive guard facet providing "must be" validations.
/// Stack-only ref struct to ensure zero allocations.
/// </summary>
/// <typeparam name="T">The type of value being validated</typeparam>
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

/// <summary>
/// Type-specific extension methods for Be&lt;T&gt; to provide type-safe guards.
/// These only appear in IntelliSense for appropriate types.
/// </summary>
public static class BeExtensions
{
    /// <summary>
    /// Throws if int is less than or equal to zero; returns value otherwise.
    /// </summary>
    /// <param name="be">The Be carrier</param>
    /// <returns>The validated positive integer</returns>
    /// <exception cref="ArgumentOutOfRangeException">When value is &lt;= 0</exception>
    /// <example>
    /// <code>
    /// var priority = todoPriority.Must.Be.Positive();
    /// // priority is guaranteed > 0
    /// </code>
    /// </example>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Positive(this Be<int> be)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(be.Value, be.ParamName);
        return be.Value;
    }

    /// <summary>
    /// Throws if long is less than or equal to zero; returns value otherwise.
    /// </summary>
    /// <param name="be">The Be carrier</param>
    /// <returns>The validated positive long</returns>
    /// <exception cref="ArgumentOutOfRangeException">When value is &lt;= 0</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Positive(this Be<long> be)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(be.Value, be.ParamName);
        return be.Value;
    }

    /// <summary>
    /// Throws if decimal is less than or equal to zero; returns value otherwise.
    /// </summary>
    /// <param name="be">The Be carrier</param>
    /// <returns>The validated positive decimal</returns>
    /// <exception cref="ArgumentOutOfRangeException">When value is &lt;= 0</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static decimal Positive(this Be<decimal> be)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(be.Value, be.ParamName);
        return be.Value;
    }

    /// <summary>
    /// Throws if double is less than or equal to zero; returns value otherwise.
    /// </summary>
    /// <param name="be">The Be carrier</param>
    /// <returns>The validated positive double</returns>
    /// <exception cref="ArgumentOutOfRangeException">When value is &lt;= 0</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Positive(this Be<double> be)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(be.Value, be.ParamName);
        return be.Value;
    }

    /// <summary>
    /// Throws if int is less than zero; returns value otherwise.
    /// </summary>
    /// <param name="be">The Be carrier</param>
    /// <returns>The validated non-negative integer</returns>
    /// <exception cref="ArgumentOutOfRangeException">When value is &lt; 0</exception>
    /// <example>
    /// <code>
    /// var count = itemCount.Must.Be.NonNegative();
    /// // count is guaranteed >= 0
    /// </code>
    /// </example>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int NonNegative(this Be<int> be)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(be.Value, be.ParamName);
        return be.Value;
    }

    /// <summary>
    /// Throws if long is less than zero; returns value otherwise.
    /// </summary>
    /// <param name="be">The Be carrier</param>
    /// <returns>The validated non-negative long</returns>
    /// <exception cref="ArgumentOutOfRangeException">When value is &lt; 0</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long NonNegative(this Be<long> be)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(be.Value, be.ParamName);
        return be.Value;
    }

    /// <summary>
    /// Throws if int is not within the specified range (inclusive); returns value otherwise.
    /// </summary>
    /// <param name="be">The Be carrier</param>
    /// <param name="min">Minimum allowed value (inclusive)</param>
    /// <param name="max">Maximum allowed value (inclusive)</param>
    /// <returns>The validated value within range</returns>
    /// <exception cref="ArgumentOutOfRangeException">When value is outside [min, max]</exception>
    /// <example>
    /// <code>
    /// var rating = userRating.Must.Be.InRange(1, 5);
    /// // rating is guaranteed to be between 1 and 5 (inclusive)
    /// </code>
    /// </example>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int InRange(this Be<int> be, int min, int max)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(be.Value, min, be.ParamName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(be.Value, max, be.ParamName);
        return be.Value;
    }

    /// <summary>
    /// Throws if long is not within the specified range (inclusive); returns value otherwise.
    /// </summary>
    /// <param name="be">The Be carrier</param>
    /// <param name="min">Minimum allowed value (inclusive)</param>
    /// <param name="max">Maximum allowed value (inclusive)</param>
    /// <returns>The validated value within range</returns>
    /// <exception cref="ArgumentOutOfRangeException">When value is outside [min, max]</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long InRange(this Be<long> be, long min, long max)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(be.Value, min, be.ParamName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(be.Value, max, be.ParamName);
        return be.Value;
    }

    /// <summary>
    /// Throws if decimal is not within the specified range (inclusive); returns value otherwise.
    /// </summary>
    /// <param name="be">The Be carrier</param>
    /// <param name="min">Minimum allowed value (inclusive)</param>
    /// <param name="max">Maximum allowed value (inclusive)</param>
    /// <returns>The validated value within range</returns>
    /// <exception cref="ArgumentOutOfRangeException">When value is outside [min, max]</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static decimal InRange(this Be<decimal> be, decimal min, decimal max)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(be.Value, min, be.ParamName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(be.Value, max, be.ParamName);
        return be.Value;
    }

    /// <summary>
    /// Throws if int is less than the specified minimum; returns value otherwise.
    /// </summary>
    /// <param name="be">The Be carrier</param>
    /// <param name="min">Minimum allowed value (inclusive)</param>
    /// <returns>The validated value &gt;= min</returns>
    /// <exception cref="ArgumentOutOfRangeException">When value is &lt; min</exception>
    /// <example>
    /// <code>
    /// var age = userAge.Must.Be.AtLeast(13);
    /// // age is guaranteed >= 13
    /// </code>
    /// </example>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int AtLeast(this Be<int> be, int min)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(be.Value, min, be.ParamName);
        return be.Value;
    }

    /// <summary>
    /// Throws if long is less than the specified minimum; returns value otherwise.
    /// </summary>
    /// <param name="be">The Be carrier</param>
    /// <param name="min">Minimum allowed value (inclusive)</param>
    /// <returns>The validated value &gt;= min</returns>
    /// <exception cref="ArgumentOutOfRangeException">When value is &lt; min</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long AtLeast(this Be<long> be, long min)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(be.Value, min, be.ParamName);
        return be.Value;
    }

    /// <summary>
    /// Throws if int is greater than the specified maximum; returns value otherwise.
    /// </summary>
    /// <param name="be">The Be carrier</param>
    /// <param name="max">Maximum allowed value (inclusive)</param>
    /// <returns>The validated value &lt;= max</returns>
    /// <exception cref="ArgumentOutOfRangeException">When value is &gt; max</exception>
    /// <example>
    /// <code>
    /// var percentage = score.Must.Be.AtMost(100);
    /// // percentage is guaranteed <= 100
    /// </code>
    /// </example>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int AtMost(this Be<int> be, int max)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(be.Value, max, be.ParamName);
        return be.Value;
    }

    /// <summary>
    /// Throws if long is greater than the specified maximum; returns value otherwise.
    /// </summary>
    /// <param name="be">The Be carrier</param>
    /// <param name="max">Maximum allowed value (inclusive)</param>
    /// <returns>The validated value &lt;= max</returns>
    /// <exception cref="ArgumentOutOfRangeException">When value is &gt; max</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long AtMost(this Be<long> be, long max)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(be.Value, max, be.ParamName);
        return be.Value;
    }

    /// <summary>
    /// Throws if predicate returns false (i.e., requirement not met); returns value otherwise.
    /// </summary>
    /// <typeparam name="T">The type of value being validated</typeparam>
    /// <param name="be">The Be carrier</param>
    /// <param name="required">Predicate that returns true for valid values</param>
    /// <param name="message">Custom error message</param>
    /// <returns>The validated value</returns>
    /// <exception cref="ArgumentException">When predicate returns false</exception>
    /// <example>
    /// <code>
    /// var email = userEmail.Must.Be.Where(e => e.Contains("@"), "Must be valid email");
    /// </code>
    /// </example>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Where<T>(this Be<T> be, Func<T, bool> required, string? message = null)
    {
        if (!required(be.Value))
            throw new ArgumentException(message ?? "Does not satisfy requirement.", be.ParamName);
        return be.Value;
    }

    #region Between Guards with Range Types

    /// <summary>
    /// Throws if int is not within the specified range with configurable inclusivity.
    /// </summary>
    /// <param name="be">The Be carrier</param>
    /// <param name="min">Lower bound</param>
    /// <param name="max">Upper bound</param>
    /// <param name="rangeType">Inclusivity behavior (default: Inclusive)</param>
    /// <returns>The validated value within range</returns>
    /// <exception cref="ArgumentOutOfRangeException">When value is outside the specified range</exception>
    /// <example>
    /// <code>
    /// var age = userAge.Must.Be.Between(13, 120, RangeType.Inclusive);  // [13, 120]
    /// var percent = score.Must.Be.Between(0, 100, RangeType.InclusiveExclusive); // [0, 100)
    /// </code>
    /// </example>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Between(this Be<int> be, int min, int max, RangeType rangeType = RangeType.Inclusive)
    {
        var value = be.Value;
        var valid = rangeType switch
        {
            RangeType.Inclusive => value >= min && value <= max,
            RangeType.Exclusive => value > min && value < max,
            RangeType.InclusiveExclusive => value >= min && value < max,
            RangeType.ExclusiveInclusive => value > min && value <= max,
            _ => throw new ArgumentException("Invalid range type", nameof(rangeType))
        };

        if (!valid)
        {
            var bounds = rangeType switch
            {
                RangeType.Inclusive => $"[{min}, {max}]",
                RangeType.Exclusive => $"({min}, {max})",
                RangeType.InclusiveExclusive => $"[{min}, {max})",
                RangeType.ExclusiveInclusive => $"({min}, {max}]",
                _ => $"{min} to {max}"
            };
            throw new ArgumentOutOfRangeException(be.ParamName, value, $"Value must be in range {bounds}.");
        }

        return value;
    }

    /// <summary>
    /// Throws if long is not within the specified range with configurable inclusivity.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Between(this Be<long> be, long min, long max, RangeType rangeType = RangeType.Inclusive)
    {
        var value = be.Value;
        var valid = rangeType switch
        {
            RangeType.Inclusive => value >= min && value <= max,
            RangeType.Exclusive => value > min && value < max,
            RangeType.InclusiveExclusive => value >= min && value < max,
            RangeType.ExclusiveInclusive => value > min && value <= max,
            _ => throw new ArgumentException("Invalid range type", nameof(rangeType))
        };

        if (!valid)
        {
            var bounds = rangeType switch
            {
                RangeType.Inclusive => $"[{min}, {max}]",
                RangeType.Exclusive => $"({min}, {max})",
                RangeType.InclusiveExclusive => $"[{min}, {max})",
                RangeType.ExclusiveInclusive => $"({min}, {max}]",
                _ => $"{min} to {max}"
            };
            throw new ArgumentOutOfRangeException(be.ParamName, value, $"Value must be in range {bounds}.");
        }

        return value;
    }

    /// <summary>
    /// Throws if decimal is not within the specified range with configurable inclusivity.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static decimal Between(this Be<decimal> be, decimal min, decimal max, RangeType rangeType = RangeType.Inclusive)
    {
        var value = be.Value;
        var valid = rangeType switch
        {
            RangeType.Inclusive => value >= min && value <= max,
            RangeType.Exclusive => value > min && value < max,
            RangeType.InclusiveExclusive => value >= min && value < max,
            RangeType.ExclusiveInclusive => value > min && value <= max,
            _ => throw new ArgumentException("Invalid range type", nameof(rangeType))
        };

        if (!valid)
        {
            var bounds = rangeType switch
            {
                RangeType.Inclusive => $"[{min}, {max}]",
                RangeType.Exclusive => $"({min}, {max})",
                RangeType.InclusiveExclusive => $"[{min}, {max})",
                RangeType.ExclusiveInclusive => $"({min}, {max}]",
                _ => $"{min} to {max}"
            };
            throw new ArgumentOutOfRangeException(be.ParamName, value, $"Value must be in range {bounds}.");
        }

        return value;
    }

    /// <summary>
    /// Throws if double is not within the specified range with configurable inclusivity.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Between(this Be<double> be, double min, double max, RangeType rangeType = RangeType.Inclusive)
    {
        var value = be.Value;
        var valid = rangeType switch
        {
            RangeType.Inclusive => value >= min && value <= max,
            RangeType.Exclusive => value > min && value < max,
            RangeType.InclusiveExclusive => value >= min && value < max,
            RangeType.ExclusiveInclusive => value > min && value <= max,
            _ => throw new ArgumentException("Invalid range type", nameof(rangeType))
        };

        if (!valid)
        {
            var bounds = rangeType switch
            {
                RangeType.Inclusive => $"[{min}, {max}]",
                RangeType.Exclusive => $"({min}, {max})",
                RangeType.InclusiveExclusive => $"[{min}, {max})",
                RangeType.ExclusiveInclusive => $"({min}, {max}]",
                _ => $"{min} to {max}"
            };
            throw new ArgumentOutOfRangeException(be.ParamName, value, $"Value must be in range {bounds}.");
        }

        return value;
    }

    #endregion

    #region String Format Validation

    // Simple email regex - not RFC 5322 compliant but good for basic validation
    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100));

    // Basic URL regex
    private static readonly Regex UrlRegex = new(
        @"^https?://[^\s]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// Throws if string is not a valid email format; returns value otherwise.
    /// Uses basic email validation (not RFC 5322 compliant).
    /// </summary>
    /// <param name="be">The Be carrier</param>
    /// <returns>The validated email string</returns>
    /// <exception cref="ArgumentException">When string is not a valid email format</exception>
    /// <example>
    /// <code>
    /// var email = userEmail.Must.Be.ValidEmail();
    /// // email is guaranteed to have basic email format (xxx@yyy.zzz)
    /// </code>
    /// </example>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string ValidEmail(this Be<string> be)
    {
        if (string.IsNullOrWhiteSpace(be.Value) || !EmailRegex.IsMatch(be.Value))
            throw new ArgumentException("Value must be a valid email address.", be.ParamName);
        return be.Value;
    }

    /// <summary>
    /// Throws if string is not a valid HTTP/HTTPS URL; returns value otherwise.
    /// </summary>
    /// <param name="be">The Be carrier</param>
    /// <returns>The validated URL string</returns>
    /// <exception cref="ArgumentException">When string is not a valid URL</exception>
    /// <example>
    /// <code>
    /// var url = websiteUrl.Must.Be.ValidUrl();
    /// // url is guaranteed to be a valid HTTP/HTTPS URL
    /// </code>
    /// </example>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string ValidUrl(this Be<string> be)
    {
        if (string.IsNullOrWhiteSpace(be.Value) || !UrlRegex.IsMatch(be.Value))
            throw new ArgumentException("Value must be a valid HTTP/HTTPS URL.", be.ParamName);
        return be.Value;
    }

    /// <summary>
    /// Throws if string does not match the specified regex pattern; returns value otherwise.
    /// </summary>
    /// <param name="be">The Be carrier</param>
    /// <param name="pattern">Regular expression pattern to match</param>
    /// <param name="message">Custom error message</param>
    /// <returns>The validated string matching the pattern</returns>
    /// <exception cref="ArgumentException">When string does not match pattern</exception>
    /// <example>
    /// <code>
    /// var zipCode = userZip.Must.Be.MatchingPattern(@"^\d{5}(-\d{4})?$", "Invalid ZIP code");
    /// var phone = userPhone.Must.Be.MatchingPattern(@"^\d{3}-\d{3}-\d{4}$", "Phone must be XXX-XXX-XXXX");
    /// </code>
    /// </example>
    public static string MatchingPattern(this Be<string> be, string pattern, string? message = null)
    {
        if (string.IsNullOrWhiteSpace(be.Value))
            throw new ArgumentException(message ?? $"Value must match pattern: {pattern}", be.ParamName);

        try
        {
            var regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromMilliseconds(100));
            if (!regex.IsMatch(be.Value))
                throw new ArgumentException(message ?? $"Value must match pattern: {pattern}", be.ParamName);
        }
        catch (RegexMatchTimeoutException)
        {
            throw new ArgumentException("Pattern matching timed out.", be.ParamName);
        }

        return be.Value;
    }

    #endregion

    #region Enum Validation

    /// <summary>
    /// Throws if enum value is not defined in the enum type; returns value otherwise.
    /// </summary>
    /// <typeparam name="TEnum">The enum type</typeparam>
    /// <param name="be">The Be carrier</param>
    /// <returns>The validated enum value</returns>
    /// <exception cref="ArgumentException">When enum value is not defined</exception>
    /// <example>
    /// <code>
    /// public enum Status { Active = 1, Inactive = 2 }
    ///
    /// var status = userStatus.Must.Be.Defined&lt;Status&gt;();
    /// // Throws if userStatus is not Status.Active or Status.Inactive
    /// </code>
    /// </example>
    public static TEnum Defined<TEnum>(this Be<TEnum> be) where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(typeof(TEnum), be.Value))
            throw new ArgumentException($"Invalid {typeof(TEnum).Name} value: {be.Value}", be.ParamName);
        return be.Value;
    }

    #endregion
}
