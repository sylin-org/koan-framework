using Sora.Messaging.Provisioning;

namespace Sora.Messaging;

internal sealed class MessagingDiagnostics : IMessagingDiagnostics
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, EffectiveMessagingPlan> _plans = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ProvisioningDiagnostics> _prov = new(StringComparer.OrdinalIgnoreCase);
    public void SetEffectivePlan(string busCode, EffectiveMessagingPlan plan) => _plans[busCode] = plan;
    public EffectiveMessagingPlan? GetEffectivePlan(string busCode) => _plans.TryGetValue(busCode, out var p) ? p : null;
    public void SetProvisioning(string busCode, ProvisioningDiagnostics diag) => _prov[busCode] = diag;
    public ProvisioningDiagnostics? GetProvisioning(string busCode) => _prov.TryGetValue(busCode, out var d) ? d : null;
}