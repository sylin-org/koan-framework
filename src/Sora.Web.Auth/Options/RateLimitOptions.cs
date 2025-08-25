namespace Sora.Web.Auth.Options;

public sealed class RateLimitOptions
{
    public int ChallengesPerMinutePerIp { get; init; } = 10;
    public int CallbackFailuresPer10MinPerIp { get; init; } = 5;
}