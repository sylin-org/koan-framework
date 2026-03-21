using S18.Prism.Models;

namespace S18.Prism.Services.SourcePulling;

public interface ISourcePullAdapter
{
    SourceType SupportedType { get; }
    Task<List<Note>> PullAsync(Source source, CancellationToken ct);
}
