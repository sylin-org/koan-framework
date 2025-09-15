namespace Koan.Flow.Runtime;

public interface IFlowRuntime
{
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
    Task ReplayAsync(DateTimeOffset? from = null, DateTimeOffset? until = null, CancellationToken ct = default);
    Task ReprojectAsync(string referenceId, string? viewName = null, CancellationToken ct = default);
}

public interface IFlowBuilder
{
    IFlowBuilder Standardize();
    IFlowBuilder Key();
    IFlowBuilder Associate();
    IFlowBuilder Project();
    IFlowBuilder Distribute();
}
