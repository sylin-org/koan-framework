using Koan.Orchestration.Cli.Infrastructure;
using Koan.Orchestration.Cli.Runtime;

namespace Koan.Orchestration.Cli.Commands;

internal sealed class DownCliCommand : ICliCommand
{
    private readonly CommandRuntime _runtime;

    public DownCliCommand(CommandRuntime runtime) => _runtime = runtime;

    public Task<int> ExecuteAsync(CommandArgs args)
    {
        var output = args.GetValue("file") ?? Constants.DefaultComposePath;
        var removeVolumes = args.HasFlag("volumes") || args.HasFlag("prune-data");
        var engine = args.GetValue("engine");
        return _runtime.ExecuteDownAsync(new CommandRuntime.DownCommandOptions(output, removeVolumes, engine));
    }
}
