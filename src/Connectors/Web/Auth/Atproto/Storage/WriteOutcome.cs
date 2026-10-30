namespace Koan.Web.Auth.Connector.Atproto.Storage;

/// <summary>Whether a write under a claim landed, was refused because its guard no longer held, or is unknown.</summary>
internal readonly record struct WriteOutcome(bool HasLanded, bool IsRefused, Exception? Failure)
{
    public static readonly WriteOutcome Landed = new(true, false, null);
    public static readonly WriteOutcome Refused = new(false, true, null);
    public static WriteOutcome Unknown(Exception? failure) => new(false, false, failure);
}
