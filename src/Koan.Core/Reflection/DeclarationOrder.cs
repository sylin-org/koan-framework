using System.Reflection;

namespace Koan.Core.Reflection;

/// <summary>
/// The sort key that puts reflected members back into declaration order.
/// </summary>
/// <remarks>
/// <see cref="MemberInfo.MetadataToken"/> is the exact answer wherever the runtime keeps metadata
/// tokens, and it is what every caller here used directly. NativeAOT keeps none: ILC discards them and
/// the property throws <see cref="InvalidOperationException"/> ("There is no metadata token available
/// for the given member"), which turned an ordering detail into a boot failure for any AOT-published
/// application that maps an entity.
///
/// Where tokens are unavailable the key is constant, so LINQ's stable ordering leaves the sequence in
/// the order reflection produced it — the declaration order the token was standing in for. On CoreCLR
/// the answer is the token, so the ordering is byte-for-byte what it was before.
///
/// The availability question is answered by asking the very member the caller passed, rather than by
/// probing a member of this class: a private probe member is exactly the kind of thing trimming
/// removes, and nothing guarantees that a <see cref="Type"/> and a <see cref="PropertyInfo"/> answer
/// alike on a runtime that supports tokens partially.
/// </remarks>
public static class DeclarationOrder
{
    private const int Unknown = 0;
    private const int TokensAvailable = 1;
    private const int TokensUnavailable = 2;

    private static int _state = Unknown;

    /// <summary>The declaration-order sort key for <paramref name="member"/>.</summary>
    public static int Of(MemberInfo member)
    {
        ArgumentNullException.ThrowIfNull(member);

        if (Volatile.Read(ref _state) == TokensUnavailable) return 0;

        try
        {
            var token = member.MetadataToken;
            Volatile.Write(ref _state, TokensAvailable);
            return token;
        }
        catch (InvalidOperationException)
        {
            // Every member on this runtime answers the same way, so stop asking.
            Volatile.Write(ref _state, TokensUnavailable);
            return 0;
        }
    }
}
