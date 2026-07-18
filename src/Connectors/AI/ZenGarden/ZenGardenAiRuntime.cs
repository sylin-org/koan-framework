namespace Koan.AI.Connector.ZenGarden;

/// <summary>DI-owned endpoint/client state initialized once by the layered Zen Garden provider activator.</summary>
internal sealed class ZenGardenAiRuntime : IDisposable
{
    private readonly object _gate = new();
    private HttpClient? _client;
    private IReadOnlySet<string>? _capabilities;

    internal void Configure(string endpoint, IReadOnlySet<string> capabilities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentNullException.ThrowIfNull(capabilities);

        lock (_gate)
        {
            if (_client is not null)
            {
                throw new InvalidOperationException("The Zen Garden AI runtime is already configured for this host.");
            }

            _client = new HttpClient
            {
                BaseAddress = new Uri(endpoint),
                Timeout = TimeSpan.FromMinutes(10)
            };
            _capabilities = new HashSet<string>(capabilities, StringComparer.OrdinalIgnoreCase);
        }
    }

    internal HttpClient Client => _client
        ?? throw new InvalidOperationException("The Zen Garden AI runtime has not resolved an orchestrator endpoint.");

    internal IReadOnlySet<string> Capabilities => _capabilities
        ?? throw new InvalidOperationException("The Zen Garden AI runtime has not resolved orchestrator capabilities.");

    public void Dispose()
    {
        lock (_gate)
        {
            _client?.Dispose();
            _client = null;
            _capabilities = null;
        }
    }
}
