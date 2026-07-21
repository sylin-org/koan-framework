using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Core.Orchestration.Composition;

/// <summary>Binds a host's immutable structural plan to one retained instance of each dynamic source.</summary>
internal sealed class ServiceDiscoveryRuntime
{
    private readonly ImmutableArray<BoundSource> _sources;
    private readonly IReadOnlyDictionary<string, BoundSource> _sourcesByScheme;

    internal ServiceDiscoveryRuntime(IServiceProvider services, ServiceDiscoveryPlan plan)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(plan);

        _sources = plan.Sources
            .Select(source => new BoundSource(
                source,
                (IDiscoveryCandidateSource)services.GetRequiredService(source.SourceType)))
            .ToImmutableArray();
        _sourcesByScheme = _sources
            .SelectMany(static source => source.Registration.IntentSchemes.Select(scheme => (scheme, source)))
            .ToDictionary(static item => item.scheme, static item => item.source, StringComparer.OrdinalIgnoreCase);
    }

    internal async Task<AutomaticSourceQuery> QueryAutomatic(
        DiscoveryCandidateRequest request,
        CancellationToken cancellationToken)
    {
        var candidates = ImmutableArray.CreateBuilder<DiscoveryCandidate>();
        var failures = ImmutableArray.CreateBuilder<DiscoverySourceFailure>();

        foreach (var source in _sources)
        {
            var result = await Query(source, request, cancellationToken).ConfigureAwait(false);
            candidates.AddRange(result.Candidates);
            if (result.Failure is not null) failures.Add(result.Failure);
        }

        return new AutomaticSourceQuery(candidates.ToImmutable(), failures.ToImmutable());
    }

    internal async Task<RequiredSourceQuery> QueryRequired(
        string intentScheme,
        DiscoveryCandidateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(intentScheme);
        if (!_sourcesByScheme.TryGetValue(intentScheme, out var source))
        {
            return RequiredSourceQuery.NotMatched;
        }

        var result = await Query(source, request, cancellationToken).ConfigureAwait(false);
        return new RequiredSourceQuery(true, result.Candidates, result.Failure);
    }

    private static async Task<SourceQuery> Query(
        BoundSource source,
        DiscoveryCandidateRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var supplied = await source.Instance
                .GetCandidates(request, cancellationToken)
                .ConfigureAwait(false);
            var normalized = (supplied ?? [])
                .Where(static candidate => candidate is not null && !string.IsNullOrWhiteSpace(candidate.Url))
                .Select(static candidate => candidate with { Priority = DiscoveryCandidatePriority.Automatic })
                .ToImmutableArray();
            return new SourceQuery(normalized, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new SourceQuery(
                [],
                new DiscoverySourceFailure(
                    source.Registration.Owner,
                    source.Registration.Id,
                    exception.GetType().Name));
        }
    }

    private sealed record BoundSource(
        DiscoverySourceRegistration Registration,
        IDiscoveryCandidateSource Instance);

    private sealed record SourceQuery(
        ImmutableArray<DiscoveryCandidate> Candidates,
        DiscoverySourceFailure? Failure);
}

internal sealed record AutomaticSourceQuery(
    ImmutableArray<DiscoveryCandidate> Candidates,
    ImmutableArray<DiscoverySourceFailure> Failures);

internal sealed record RequiredSourceQuery(
    bool IsMatched,
    ImmutableArray<DiscoveryCandidate> Candidates,
    DiscoverySourceFailure? Failure)
{
    internal static RequiredSourceQuery NotMatched { get; } = new(false, [], null);
}

internal sealed record DiscoverySourceFailure(string Owner, string Id, string ErrorType);
