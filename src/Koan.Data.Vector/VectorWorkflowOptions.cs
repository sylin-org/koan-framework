using System.Collections.Generic;

namespace Koan.Data.Vector;

public sealed class VectorWorkflowOptions
{
    public bool EnableWorkflows { get; set; } = true;

    public IDictionary<string, VectorProfileOption> Profiles { get; } =
        new Dictionary<string, VectorProfileOption>(System.StringComparer.OrdinalIgnoreCase);

    public sealed class VectorProfileOption
    {
        public int? TopK { get; set; }
        public double? Alpha { get; set; }
        public string? VectorName { get; set; }
        public Dictionary<string, object?>? Metadata { get; set; }
        public bool EmitMetrics { get; set; }
    }
}
