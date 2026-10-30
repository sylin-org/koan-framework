namespace Koan.Jobs;

/// <summary>A translatable filter for the static facade (<c>MyModel.Jobs.Query(...)</c>) and dashboards, and the page
/// of its matches to read. Null fields are wildcards. Kept declarative (not an arbitrary predicate) so durable ledgers
/// can push it down. Matches are read newest first by submission, one bounded page at a time (JOBS-0010).</summary>
public sealed record JobQuery(
    string? WorkType = null,
    string? WorkId = null,
    string? Action = null,
    JobStatus? Status = null)
{
    /// <summary>The page size a query reads unless it names its own.</summary>
    public const int DefaultSize = 100;

    private readonly int _page = 1;
    private readonly int _size = DefaultSize;

    /// <summary>The 1-based page of matches to read, newest first.</summary>
    public int Page
    {
        get => _page;
        init => _page = value >= 1 ? value : throw new ArgumentOutOfRangeException(nameof(Page), value, "A page number starts at 1.");
    }

    /// <summary>The most matches one read returns.</summary>
    public int Size
    {
        get => _size;
        init => _size = value >= 1 ? value : throw new ArgumentOutOfRangeException(nameof(Size), value, "A page holds at least one match.");
    }

    /// <summary>Work source filter: null spans sources; empty selects the default, including legacy records.</summary>
    public string? WorkSource { get; init; }
    /// <summary>Work adapter filter: null spans adapters; empty selects the default.</summary>
    public string? WorkAdapter { get; init; }
    /// <summary>Work partition filter: null spans partitions; empty selects the default.</summary>
    public string? WorkPartition { get; init; }
}
