using Koan.Orchestration;

namespace Koan.Core.Hosting.Bootstrap
{
    public enum BootstrapState
    {
        Success,
        Failed,
        Skipped
    }
}

namespace Koan.Orchestration
{
    /// <summary>
    /// Unified service metadata for orchestration-runtime bridge
    /// </summary>
    public class UnifiedServiceMetadata
    {
        public ServiceKind ServiceKind { get; init; }
        public bool IsOrchestrationAware { get; init; }
        public List<string> Capabilities { get; init; } = new();

        public bool HasCapability(string capability)
            => Capabilities.Contains(capability, StringComparer.OrdinalIgnoreCase);
    }
}