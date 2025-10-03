namespace Koan.Core.Orchestration;

/// <summary>Public model for adapter discovery attempts</summary>
public sealed record DiscoveryCandidate(string Url, string Method, int Priority = 1);