global using Koan.Data.SoftDelete;
global using Koan.Jobs;

// One receiver deliberately satisfies every current module constraint. This makes the cell prove
// actual member coexistence, rather than merely proving that the namespaces can be imported together.
[SoftDelete]
public sealed class AllModuleTodo : Entity<AllModuleTodo>, IKoanJob<AllModuleTodo>
{
    public static Task Execute(AllModuleTodo job, JobContext context, CancellationToken ct)
        => Task.CompletedTask;
}

public static class AllModuleConsumer
{
    public static EntityCacheExplanation Explain() => AllModuleTodo.Cache.Explain();
    public static JobStatics<AllModuleTodo> Jobs() => AllModuleTodo.Jobs;
    public static IDisposable WithDeleted() => AllModuleTodo.WithDeleted();
    public static JobOps<AllModuleTodo> Job(AllModuleTodo todo) => todo.Job;
    public static Task<bool> HardDelete(AllModuleTodo todo) => todo.HardDelete();
}
