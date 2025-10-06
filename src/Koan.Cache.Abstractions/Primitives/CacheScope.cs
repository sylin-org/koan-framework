namespace Koan.Cache.Abstractions.Primitives;

public enum CacheScope
{
    Entity,
    EntityQuery,
    ControllerAction,
    ControllerResponse,
    PipelineStep,
    Instruction,
    Custom
}
