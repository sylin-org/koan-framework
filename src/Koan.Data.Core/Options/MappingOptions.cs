namespace Koan.Data.Core.Options;

public sealed class MappingOptions
{
    public int PlanEntries { get; set; } = Infrastructure.Constants.Defaults.MappingPlanEntries;
}
