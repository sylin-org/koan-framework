using Koan.Data.Abstractions;
using Koan.Data.Core.Options;
using Koan.Data.Core.Routing;
using Microsoft.Extensions.Options;

namespace Koan.Data.Core;

/// <summary>Discovers installed record providers and coordinates redaction-safe candidate probes.</summary>
public sealed class DataProviderSetup
{
    private readonly IServiceProvider _services;
    private readonly DataProviderCatalog _providers;
    private readonly TimeSpan _timeout;
    private readonly IReadOnlyList<DataProviderCandidate> _candidates;

    internal DataProviderSetup(
        IServiceProvider services,
        DataProviderCatalog providers,
        IOptions<SourceIntegrationOptions> options)
    {
        _services = services;
        _providers = providers;
        _timeout = options.Value.DoctorTimeout;
        if (_timeout <= TimeSpan.Zero)
            throw new InvalidOperationException("Koan:Data:SourceIntegration:DoctorTimeout must be positive.");

        _candidates = providers.Candidates.Select(candidate =>
        {
            var setup = candidate.Value as IDataAdapterSetup;
            var descriptor = setup?.DescribeSetup();
            if (setup is not null && descriptor is null)
                throw new InvalidOperationException(
                    $"Data provider '{candidate.Id}' returned no setup descriptor.");
            var fields = descriptor?.Fields?.ToArray() ?? [];
            Validate(candidate.Id, descriptor?.DisplayName, fields);
            return new DataProviderCandidate(
                candidate.Id,
                descriptor?.DisplayName?.Trim() ?? candidate.Id,
                candidate.Aliases.ToArray(),
                fields,
                setup is not null,
                DataClaimSet.Describe(candidate.Value).Capabilities);
        }).ToArray();
    }

    public IReadOnlyList<DataProviderCandidate> Candidates => _candidates;

    public async Task<DataProviderProbeResult> Probe(
        string provider,
        IReadOnlyDictionary<string, string?> settings,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentNullException.ThrowIfNull(settings);

        var factory = _providers.Find(provider);
        if (factory is null)
            return new DataProviderProbeResult(
                DataProviderProbeStatus.InvalidConfiguration,
                "The requested provider is not installed in this host.",
                $"Choose one of: {string.Join(", ", _candidates.Select(static candidate => candidate.Id))}.");

        var canonical = _providers.Describe(factory).Id;
        var candidate = _candidates.Single(item =>
            string.Equals(item.Id, canonical, StringComparison.OrdinalIgnoreCase));
        if (factory is not IDataAdapterSetup setup)
            return new DataProviderProbeResult(
                DataProviderProbeStatus.Unsupported,
                $"Provider '{canonical}' does not implement candidate probing.",
                "Configure a named source and use Data.Source(name).Doctor(), or update the adapter.");

        DataProviderProbeContext context;
        try
        {
            var submitted = new DataProviderProbeContext(settings);
            var known = candidate.Fields.Select(static field => field.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var unknown = submitted.Settings.Keys.FirstOrDefault(key => !known.Contains(key));
            if (unknown is not null)
                throw new ArgumentException($"Candidate setting '{unknown}' is not declared by provider '{canonical}'.");

            var merged = candidate.Fields.ToDictionary(
                static field => field.Key,
                field => submitted.Settings.TryGetValue(field.Key, out var value) ? value : field.DefaultValue,
                StringComparer.OrdinalIgnoreCase);
            var missing = candidate.Fields.FirstOrDefault(field =>
                field.Required && string.IsNullOrWhiteSpace(merged[field.Key]));
            if (missing is not null)
                throw new ArgumentException($"Candidate setting '{missing.Key}' is required.");
            context = new DataProviderProbeContext(merged);
        }
        catch (ArgumentException error)
        {
            return Invalid(error.Message);
        }

        using var timeout = new CancellationTokenSource(_timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try
        {
            return await setup.Probe(_services, context, linked.Token).ConfigureAwait(false)
                ?? new DataProviderProbeResult(
                    DataProviderProbeStatus.Unavailable,
                    $"Provider '{canonical}' returned no candidate probe result.",
                    "Update the adapter or configure and diagnose a named source.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return new DataProviderProbeResult(
                DataProviderProbeStatus.TimedOut,
                $"Provider '{canonical}' did not complete its candidate probe in time.",
                "Check reachability or increase Koan:Data:SourceIntegration:DoctorTimeout deliberately.");
        }
        catch (Exception error) when (error is ArgumentException or FormatException)
        {
            return Invalid("The submitted settings are not valid for this provider.");
        }
        catch
        {
            return new DataProviderProbeResult(
                DataProviderProbeStatus.Unavailable,
                $"Provider '{canonical}' could not open the candidate target.",
                "Check the endpoint, credentials, target name, network path, and provider availability.");
        }

        DataProviderProbeResult Invalid(string message) => new(
            DataProviderProbeStatus.InvalidConfiguration,
            message,
            $"Supply the fields declared by provider '{canonical}'.");
    }

    private static void Validate(
        string provider,
        string? displayName,
        IReadOnlyList<DataProviderSetupField> fields)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new InvalidOperationException($"Data provider '{provider}' declares an empty display name.");
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field.Key) || string.IsNullOrWhiteSpace(field.Label))
                throw new InvalidOperationException(
                    $"Data provider '{provider}' declares a setup field with an empty key or label.");
            if (!keys.Add(field.Key.Trim()))
                throw new InvalidOperationException(
                    $"Data provider '{provider}' declares setup field '{field.Key}' more than once.");
        }
    }
}
