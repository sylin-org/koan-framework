using Koan.Orchestration.Cli.Runtime;

namespace Koan.Orchestration.Cli.Commands;

internal sealed class DoctorCliCommand : ICliCommand
{
    private readonly CommandRuntime _runtime;

    public DoctorCliCommand(CommandRuntime runtime) => _runtime = runtime;

    public Task<int> ExecuteAsync(CommandArgs args)
    {
        var engine = args.GetValue("engine");
        var json = args.HasFlag("json");
        return _runtime.ExecuteDoctorAsync(new CommandRuntime.DoctorCommandOptions(engine, json));
    }
}
