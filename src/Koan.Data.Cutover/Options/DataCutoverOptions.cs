namespace Koan.Data.Cutover.Options;

public sealed class DataCutoverOptions
{
    /// <summary>The operator's assertion that no writer exists outside this host's Data operation horizon.</summary>
    public CutoverWriterOwnership WriterOwnership { get; set; }

    /// <summary>Maximum records held in one raw copy or verification page.</summary>
    public int PageSize { get; set; } = Infrastructure.Constants.DefaultPageSize;

    /// <summary>Maximum physical containers requested from an inspector at once.</summary>
    public int ContainerPageSize { get; set; } = Infrastructure.Constants.DefaultContainerPageSize;

    /// <summary>Maximum containers admitted to one complete source inventory.</summary>
    public int MaximumContainers { get; set; } = Infrastructure.Constants.DefaultMaximumContainers;
}
