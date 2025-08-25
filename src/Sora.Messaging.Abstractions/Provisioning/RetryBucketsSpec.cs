namespace Sora.Messaging.Provisioning;

public sealed record RetryBucketsSpec(IReadOnlyList<int> DelaysSeconds);