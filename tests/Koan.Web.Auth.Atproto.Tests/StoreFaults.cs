using Koan.Web.Auth.Connector.Atproto.Storage;

namespace Koan.Web.Auth.Atproto.Tests;

/// <summary>
/// Store failures a spec arms on a refresh's result, a write that clears a claim and stores another grant, declared on
/// <see cref="AtprotoSession"/>'s lifecycle so they act where a store would: before the write lands, or after it landed.
/// </summary>
internal static class StoreFaults
{
    /// <summary>Result writes that fail before they land, counted down.</summary>
    public static int FailResults;

    /// <summary>Result writes that land and then fail, counted down.</summary>
    public static int FailAfterResults;

    public static void Reset()
    {
        FailResults = 0;
        FailAfterResults = 0;
    }

    public static void Declare()
    {
        AtprotoSession.Lifecycle.BeforeUpsert(context =>
        {
            if (Result(context.Prior, context.Current) && Interlocked.Decrement(ref FailResults) >= 0)
                throw new InvalidOperationException("fixture: the store failed this write");
            return context.Proceed();
        });
        AtprotoSession.Lifecycle.AfterUpsert(context =>
        {
            if (Result(context.Prior, context.Current) && Interlocked.Decrement(ref FailAfterResults) >= 0)
                throw new InvalidOperationException("fixture: the store failed after this write landed");
        });
    }

    private static bool Result(AtprotoSession? prior, AtprotoSession current)
        => current.ClaimedUntil is null && current.Grant is not null && prior?.ClaimedUntil is not null && current.Grant != prior.Grant;
}
