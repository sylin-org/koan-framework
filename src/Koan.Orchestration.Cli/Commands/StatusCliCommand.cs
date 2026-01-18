using Koan.Orchestration.Cli.Runtime;

namespace Koan.Orchestration.Cli.Commands;

internal sealed class StatusCliCommand : ICliCommand
{
    private readonly CommandRuntime _runtime;

    public StatusCliCommand(CommandRuntime runtime) => _runtime = runtime;

    public Task<int> ExecuteAsync(CommandArgs args)
    {
        var json = args.HasFlag("json");
        var engine = args.GetValue("engine");
        var profile = args.GetValue("profile");
        var basePort = ParseNullableInt(args.GetValue("base-port"));
        var portOverride = ParseNullableInt(args.GetValue("port"));
        var exposeInternals = args.HasFlag("expose-internals");
        var noManifest = args.HasFlag("no-launch-manifest");

        return _runtime.ExecuteStatusAsync(new CommandRuntime.StatusCommandOptions(json, engine, profile, basePort, portOverride, exposeInternals, noManifest));
    }

    private static int? ParseNullableInt(string? value)
        => int.TryParse(value, out var result) ? result : null;
}
