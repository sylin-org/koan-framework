namespace Koan.Data.Core.Options;

public sealed class SourceIntegrationOptions
{
    public int MaxRecords { get; set; } = Infrastructure.Constants.Defaults.SourceMaxRecords;
    public long MaxBytes { get; set; } = Infrastructure.Constants.Defaults.SourceMaxBytes;
    public long MaxValueBytes { get; set; } = Infrastructure.Constants.Defaults.SourceMaxValueBytes;
    public TimeSpan MaxDuration { get; set; } = TimeSpan.FromSeconds(Infrastructure.Constants.Defaults.SourceMaxDurationSeconds);
    public int ParameterPlanCacheEntries { get; set; } = Infrastructure.Constants.Defaults.SourceParameterPlanCacheEntries;
    public TimeSpan DoctorTimeout { get; set; } = TimeSpan.FromSeconds(Infrastructure.Constants.Defaults.DoctorTimeoutSeconds);
}
