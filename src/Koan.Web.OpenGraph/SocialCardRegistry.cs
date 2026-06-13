using System.Collections.Generic;

namespace Koan.Web.OpenGraph;

/// <summary>
/// Static, type-keyed card registry. Clones the <c>EntityEventRegistry</c> mechanic: written once at
/// boot, read at request time, with a <c>Reset()</c> for test isolation. Registration is boot-time and
/// consumption is request-time, so there is no deferred-attach problem to solve.
/// </summary>
internal static class SocialCardRegistry
{
    private static readonly object Gate = new();
    private static readonly Dictionary<Type, CardRegistration> ByType = new();
    private static List<CardRegistration> Ordered = new();

    /// <summary>Registrations in registration order; first match wins at request time.</summary>
    public static IReadOnlyList<CardRegistration> Registrations
    {
        get { lock (Gate) { return Ordered; } }
    }

    public static bool Has(Type type)
    {
        lock (Gate) { return ByType.ContainsKey(type); }
    }

    public static bool TryGet(Type type, out CardRegistration registration)
    {
        lock (Gate) { return ByType.TryGetValue(type, out registration!); }
    }

    public static void Register(Type type, CardRegistration registration)
    {
        lock (Gate)
        {
            if (ByType.ContainsKey(type))
            {
                throw new InvalidOperationException(
                    $"A social card is already registered for '{type.Name}'. Register one card per type; call SocialCards.Reset() first in tests.");
            }

            ByType[type] = registration;
            // Copy-on-write so a concurrent reader iterating Registrations is never disturbed.
            Ordered = new List<CardRegistration>(Ordered) { registration };
        }
    }

    public static void Reset()
    {
        lock (Gate)
        {
            ByType.Clear();
            Ordered = new List<CardRegistration>();
        }
    }
}
