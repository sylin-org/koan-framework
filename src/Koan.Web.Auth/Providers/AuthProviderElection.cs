using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Web.Auth.Infrastructure;
using Koan.Web.Auth.Options;

namespace Koan.Web.Auth.Providers;

internal sealed class AuthProviderElection : IAuthProviderElection
{
    private readonly AuthOptions _options;
    private readonly IProviderRegistry _registry;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<AuthProviderElection> _logger;
    private readonly Lazy<AuthProviderSelection> _selection;

    public AuthProviderElection(
        IProviderRegistry registry,
        IOptionsSnapshot<AuthOptions> options,
        IHostEnvironment environment,
        ILogger<AuthProviderElection> logger)
    {
        _registry = registry;
        _options = options.Value;
        _environment = environment;
        _logger = logger;
        _selection = new Lazy<AuthProviderSelection>(Evaluate, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public AuthProviderSelection Current => _selection.Value;

    private AuthProviderSelection Evaluate()
    {
        var effective = _registry.EffectiveProviders;
        if (effective.Count == 0)
        {
            _logger.LogWarning("No authentication providers discovered.");
            return AuthProviderSelection.None;
        }

        var explicitIds = new HashSet<string>(_options.Providers.Keys, StringComparer.OrdinalIgnoreCase);
        var candidates = effective.Select(entry => CreateCandidate(entry.Key, entry.Value, explicitIds.Contains(entry.Key))).ToList();

        if (!candidates.Any())
        {
            _logger.LogWarning("Authentication provider discovery returned no candidates after filtering.");
            return AuthProviderSelection.None;
        }

        var preferred = FindPreferred(candidates, _options.PreferredProviderId);
        if (preferred is not null)
        {
            LogSelection(preferred);
            return preferred;
        }

        var enabledInteractive = candidates
            .Where(c => c.Enabled && c.SupportsInteractive)
            .OrderByDescending(c => c.IsExplicit)
            .ThenByDescending(c => c.Priority)
            .ThenBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (enabledInteractive.Count > 0)
        {
            var first = enabledInteractive[0];
            var reason = first.IsExplicit
                ? "Explicit configuration"
                : $"Discovered provider priority={first.Priority}";
            var selection = BuildSelection(first, false, reason);
            LogSelection(selection);
            return selection;
        }

        var fallback = TrySelectFallback(candidates);
        if (fallback is not null)
        {
            LogSelection(fallback);
            return fallback;
        }

        _logger.LogWarning("No enabled interactive authentication providers could be elected.");
        return new AuthProviderSelection(null, "none", "No enabled interactive providers available.", 0, false, null);
    }

    private AuthProviderCandidate CreateCandidate(string id, ProviderOptions options, bool isExplicit)
    {
        var protocol = (options.Type ?? AuthConstants.Protocols.Oidc).ToLowerInvariant();
        var priority = options.Priority ?? 0;
        return new AuthProviderCandidate(
            id,
            protocol,
            priority,
            options.Enabled,
            isExplicit);
    }

    private AuthProviderSelection? FindPreferred(IEnumerable<AuthProviderCandidate> candidates, string? preferredId)
    {
        if (string.IsNullOrWhiteSpace(preferredId)) return null;
        var preferred = candidates.FirstOrDefault(c => string.Equals(c.Id, preferredId, StringComparison.OrdinalIgnoreCase));
        if (preferred is null)
        {
            _logger.LogWarning("Preferred authentication provider '{PreferredProvider}' was not discovered.", preferredId);
            return null;
        }

        if (!preferred.Enabled)
        {
            _logger.LogWarning("Preferred authentication provider '{PreferredProvider}' is disabled.", preferredId);
            return null;
        }

        if (!preferred.SupportsInteractive)
        {
            _logger.LogWarning("Preferred authentication provider '{PreferredProvider}' does not support interactive challenges.", preferredId);
            return null;
        }

        return BuildSelection(preferred, false, "Preferred provider configured");
    }

    private AuthProviderSelection? TrySelectFallback(IEnumerable<AuthProviderCandidate> candidates)
    {
        if (!_environment.IsDevelopment())
        {
            return null;
        }

        var test = candidates.FirstOrDefault(c => string.Equals(c.Id, "test", StringComparison.OrdinalIgnoreCase) && c.SupportsInteractive);
        if (test is null)
        {
            return null;
        }

        if (test.IsExplicit && !test.Enabled)
        {
            return null;
        }

        var reason = test.Enabled
            ? "Development default"
            : "Development fallback: enabling TestProvider for interactive logins";
        return BuildSelection(test with { Enabled = true }, true, reason);
    }

    private AuthProviderSelection BuildSelection(AuthProviderCandidate candidate, bool isFallback, string reason)
    {
        var challenge = candidate.SupportsInteractive ? $"/auth/{candidate.Id}/challenge" : null;
        return new AuthProviderSelection(candidate.Id, candidate.Protocol, reason, candidate.Priority, isFallback, challenge);
    }

    private void LogSelection(AuthProviderSelection selection)
    {
        if (selection.HasProvider)
        {
            _logger.LogInformation("Auth provider elected: id={ProviderId} protocol={Protocol} priority={Priority} fallback={Fallback} reason={Reason}", selection.ProviderId, selection.Protocol, selection.Priority, selection.IsFallback, selection.Reason);
        }
        else
        {
            _logger.LogWarning("Auth provider election resulted in no selection: {Reason}", selection.Reason);
        }
    }

    private sealed record AuthProviderCandidate(string Id, string Protocol, int Priority, bool Enabled, bool IsExplicit)
    {
        public bool SupportsInteractive => Protocol == AuthConstants.Protocols.Oidc || Protocol == AuthConstants.Protocols.OAuth2;
    }
}
