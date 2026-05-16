using Koan.Orchestration.Cli.Infrastructure;
using Koan.Orchestration.Cli.Runtime;

namespace Koan.Orchestration.Cli.Commands;

internal sealed class UpCliCommand : ICliCommand
{
    private readonly CommandRuntime _runtime;

    public UpCliCommand(CommandRuntime runtime) => _runtime = runtime;

    public Task<int> Execute(CommandArgs args)
    {
        var output = args.GetValue("file") ?? Constants.DefaultComposePath;
        var explain = args.HasFlag("explain");
        var dryRun = args.HasFlag("dry-run");
        var verbose = args.HasFlag("verbose") || args.HasFlag("-v");
        var trace = args.HasFlag("trace");
        var quiet = args.HasFlag("quiet") || args.HasFlag("-q");
        var engine = args.GetValue("engine");
        var profile = args.GetValue("profile");
        var timeout = ParseNullableInt(args.GetValue("timeout"));
        var basePort = ParseNullableInt(args.GetValue("base-port"));
        var portOverride = ParseNullableInt(args.GetValue("port"));
        var exposeInternals = args.HasFlag("expose-internals");
        var noManifest = args.HasFlag("no-launch-manifest");
        var conflicts = args.GetValue("conflicts");

        return _runtime.ExecuteUp(new CommandRuntime.UpCommandOptions(
            output,
            explain,
            dryRun,
            verbose,
            trace,
            quiet,
            engine,
            profile,
            timeout,
            basePort,
            portOverride,
            exposeInternals,
            noManifest,
            conflicts));
    }

    private static int? ParseNullableInt(string? value)
        => int.TryParse(value, out var result) ? result : null;
}
