namespace Sora.Messaging;

public interface IMessagingDiagnostics
{
    void SetEffectivePlan(string busCode, EffectiveMessagingPlan plan);
    EffectiveMessagingPlan? GetEffectivePlan(string busCode);
    void SetProvisioning(string busCode, ProvisioningDiagnostics diag);
    ProvisioningDiagnostics? GetProvisioning(string busCode);
}