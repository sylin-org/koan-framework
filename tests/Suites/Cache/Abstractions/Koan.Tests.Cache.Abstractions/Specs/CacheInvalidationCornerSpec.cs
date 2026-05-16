using Koan.Cache.Abstractions.Coherence;
using Koan.Cache.Abstractions.Primitives;

namespace Koan.Tests.Cache.Abstractions.Specs;

/// <summary>
/// Regression coverage for the C# ternary type-inference issue: when constructing a
/// <c>CacheKey?</c> via a conditional with one branch being <c>null</c>, the compiler may
/// pick the implicit <c>string → CacheKey</c> operator path and invoke it with null,
/// throwing at runtime. Envelope round-trips for EvictByTag / EvictAll messages exercise
/// this code path (they carry no Key) so the regression below pins the contract.
/// </summary>
public sealed class CacheInvalidationCornerSpec
{
    [Fact]
    public void CacheKey_nullable_assigned_from_null_via_ternary_uses_lifting_not_implicit_operator()
    {
        string? key = null;

        // The pattern that previously threw: ternary inferred CacheKey on the true branch
        // and tried to coerce null → CacheKey on the false branch via the implicit operator.
        // Explicit cast on the true branch forces lifting on both branches.
        CacheKey? result = key is not null ? (CacheKey?)new CacheKey(key) : null;

        result.Should().BeNull();
    }

    [Fact]
    public void EvictByTag_factory_does_not_invoke_implicit_string_to_CacheKey()
    {
        // Should NOT throw — null Key parameter must lift directly to CacheKey?, not
        // go through op_Implicit(string) which would reject null.
        var act = () => CacheInvalidation.EvictByTag(
            new HashSet<string> { "Todo" },
            Guid.NewGuid());

        act.Should().NotThrow();
    }

    [Fact]
    public void EvictAll_factory_does_not_invoke_implicit_string_to_CacheKey()
    {
        var act = () => CacheInvalidation.EvictAll(Guid.NewGuid());
        act.Should().NotThrow();
    }
}
