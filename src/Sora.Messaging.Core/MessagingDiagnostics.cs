namespace Sora.Messaging;

internal sealed class MessagingDiagnostics : IMessagingDiagnostics
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, EffectiveMessagingPlan> _plans = new(StringComparer.OrdinalIgnoreCase);
    public void SetEffectivePlan(string busCode, EffectiveMessagingPlan plan) => _plans[busCode] = plan;
    public EffectiveMessagingPlan? GetEffectivePlan(string busCode) => _plans.TryGetValue(busCode, out var p) ? p : null;
}