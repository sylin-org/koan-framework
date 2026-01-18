using Koan.Orchestration.Cli.Infrastructure;
using Koan.Orchestration.Cli.Runtime;

namespace Koan.Orchestration.Cli.Commands;

internal sealed class ExportCliCommand : ICliCommand
{
    private readonly CommandRuntime _runtime;

    public ExportCliCommand(CommandRuntime runtime) => _runtime = runtime;

    public Task<int> ExecuteAsync(CommandArgs args)
    {
        var format = args.Positionals.FirstOrDefault() ?? "compose";
        var output = args.GetValue("out") ?? Constants.DefaultComposePath;
        var profile = args.GetValue("profile");
        var basePort = ParseNullableInt(args.GetValue("base-port"));
        var portOverride = ParseNullableInt(args.GetValue("port"));
        var exposeInternals = args.HasFlag("expose-internals");
        var noManifest = args.HasFlag("no-launch-manifest");

        return _runtime.ExecuteExportAsync(new CommandRuntime.ExportCommandOptions(format, output, profile, basePort, portOverride, exposeInternals, noManifest));
    }

    private static int? ParseNullableInt(string? value)
        => int.TryParse(value, out var result) ? result : null;
}
