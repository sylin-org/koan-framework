namespace Koan.Canon.Runtime;

public interface ICanonRuntime
{
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
    Task ReplayAsync(DateTimeOffset? from = null, DateTimeOffset? until = null, CancellationToken ct = default);
    Task ReprojectAsync(string referenceId, string? viewName = null, CancellationToken ct = default);
}

public interface ICanonBuilder
{
    ICanonBuilder Standardize();
    ICanonBuilder Key();
    ICanonBuilder Associate();
    ICanonBuilder Project();
    ICanonBuilder Distribute();
}


