using Koan.Testing.Diagnostics;

namespace Koan.Testing.Contracts;

public sealed class TestContext
{
    private readonly ConcurrentDictionary<string, object?> _items = new(StringComparer.OrdinalIgnoreCase);

    public TestContext(string suite, string scenario, ITestDiagnostics diagnostics, CancellationToken cancellation)
    {
        Suite = suite ?? throw new ArgumentNullException(nameof(suite));
        Scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
        Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        Cancellation = cancellation;
        StartedAt = DateTimeOffset.UtcNow;
        ExecutionId = Guid.CreateVersion7();
        Random = new Random(HashCode.Combine(ExecutionId.GetHashCode(), Scenario.GetHashCode(StringComparison.Ordinal)));
    }

    public string Suite { get; }

    public string Scenario { get; }

    public Guid ExecutionId { get; }

    public DateTimeOffset StartedAt { get; }

    public CancellationToken Cancellation { get; }

    public ITestDiagnostics Diagnostics { get; }

    public Random Random { get; }

    public void SetItem<T>(string key, T value) => _items[key] = value;

    public bool TryGetItem<T>(string key, out T value)
    {
        if (_items.TryGetValue(key, out var boxed) && boxed is T typed)
        {
            value = typed;
            return true;
        }

        value = default!;
        return false;
    }

    public T GetRequiredItem<T>(string key)
    {
        if (TryGetItem<T>(key, out var value))
        {
            return value;
        }

        throw new KeyNotFoundException($"Spec item '{key}' was not found in context '{Scenario}'.");
    }

    public IReadOnlyDictionary<string, object?> Items => _items;
}
