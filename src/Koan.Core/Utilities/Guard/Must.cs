#nullable enable
using System.Runtime.CompilerServices;

namespace Koan.Core.Utilities.Guard;

/// <summary>
/// Fluent guard carrier that provides access to NotBe and Be validation facets.
/// This is a stack-only ref struct to ensure zero heap allocations.
/// </summary>
/// <typeparam name="T">The type of value being validated</typeparam>
/// <remarks>
/// This struct is an intermediate in the fluent chain: value.Must.NotBe.Null()
/// The compiler and JIT optimizer inline this completely, resulting in zero runtime overhead.
/// </remarks>
public readonly ref struct Must<T>
{
    internal readonly T Value;
    internal readonly string? ParamName;

    /// <summary>
    /// Internal constructor - only created via MustExtensions.Must()
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Must(T value, string? paramName)
    {
        Value = value;
        ParamName = paramName;
    }

    /// <summary>
    /// Provides access to negative guards: NotBe.Null(), NotBe.Blank(), etc.
    /// </summary>
    public NotBe<T> NotBe
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(Value, ParamName);
    }

    /// <summary>
    /// Provides access to positive guards: Be.Positive(), Be.InRange(), etc.
    /// </summary>
    public Be<T> Be
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(Value, ParamName);
    }
}
