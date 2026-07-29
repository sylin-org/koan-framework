namespace Koan.Data.Abstractions;

[Flags]
public enum SourceInspectionCapabilities
{
    None = 0,
    ListContainers = 1,
    ResolveAddress = 2,
    DescribeContainer = 4,
    SampleRecords = 8
}
