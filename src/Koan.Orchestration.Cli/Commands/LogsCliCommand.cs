using Koan.Orchestration.Cli.Runtime;

namespace Koan.Orchestration.Cli.Commands;

internal sealed class LogsCliCommand : ICliCommand
{
    private readonly CommandRuntime _runtime;

    public LogsCliCommand(CommandRuntime runtime) => _runtime = runtime;

    public Task<int> ExecuteAsync(CommandArgs args)
    {
        var follow = args.HasFlag("-f") || args.HasFlag("follow");
        var tail = ParseNullableInt(args.GetValue("tail"));
        var service = args.GetValue("service");
        var since = args.GetValue("since");
        var engine = args.GetValue("engine");

        return _runtime.ExecuteLogsAsync(new CommandRuntime.LogsCommandOptions(follow, tail, service, since, engine));
    }

    private static int? ParseNullableInt(string? value)
        => int.TryParse(value, out var result) ? result : null;
}
