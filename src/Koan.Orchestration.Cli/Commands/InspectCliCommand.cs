using Koan.Orchestration.Cli.Runtime;

namespace Koan.Orchestration.Cli.Commands;

internal sealed class InspectCliCommand : ICliCommand
{
    private readonly CommandRuntime _runtime;

    public InspectCliCommand(CommandRuntime runtime) => _runtime = runtime;

    public Task<int> ExecuteAsync(CommandArgs args)
    {
        var json = args.HasFlag("json");
        var quiet = args.HasFlag("quiet");
        var engine = args.GetValue("engine");
        var profile = args.GetValue("profile");
        var basePort = ParseNullableInt(args.GetValue("base-port"));
        var portOverride = ParseNullableInt(args.GetValue("port"));
        var exposeInternals = args.HasFlag("expose-internals");

        return _runtime.ExecuteInspectAsync(new CommandRuntime.InspectCommandOptions(json, quiet, engine, profile, basePort, portOverride, exposeInternals));
    }

    private static int? ParseNullableInt(string? value)
        => int.TryParse(value, out var result) ? result : null;
}
