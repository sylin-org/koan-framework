using Koan.Orchestration.Cli.Commands;
using Koan.Orchestration.Cli.Runtime;

namespace Koan.Orchestration.Cli;

internal static class CliApplication
{
    private static readonly string[] HelpTokens = { "-h", "--help", "/?" };

    public static Task<int> Run(string[] args)
    {
        var runtime = new CommandRuntime();
        var commands = BuildCommandMap(runtime);

        if (args.Length == 0)
        {
            return commands["inspect"].Command.Execute(new CommandArgs([]));
        }

        var commandName = args[0];
        if (IsHelpToken(commandName))
        {
            PrintUsage(commands);
            return Task.FromResult(0);
        }

        if (string.Equals(commandName, "help", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteHelp(args.Skip(1).ToArray(), commands);
        }

        if (!commands.TryGetValue(commandName, out var definition))
        {
            PrintUnknownCommand(commandName, commands);
            return Task.FromResult(1);
        }

        var tail = args.Skip(1).ToArray();
        var commandArgs = new CommandArgs(tail);
        if (HasHelpFlag(commandArgs))
        {
            PrintCommandUsage(commandName, definition);
            return Task.FromResult(0);
        }

        return definition.Command.Execute(commandArgs);
    }

    private static Dictionary<string, CommandDefinition> BuildCommandMap(CommandRuntime runtime)
        => new(StringComparer.OrdinalIgnoreCase)
        {
            ["export"] = new(new ExportCliCommand(runtime), "export [compose] --out <path>", "Render orchestration assets for the current project."),
            ["doctor"] = new(new DoctorCliCommand(runtime), "doctor [--engine <id>] [--json]", "Check provider availability and tooling health."),
            ["up"] = new(new UpCliCommand(runtime), "up [--file <compose.yml>] [--engine <id>] [--dry-run]", "Generate and start the project stack locally."),
            ["down"] = new(new DownCliCommand(runtime), "down [--file <compose.yml>] [--engine <id>] [--volumes]", "Stop the project stack and optionally prune volumes."),
            ["status"] = new(new StatusCliCommand(runtime), "status [--engine <id>] [--json]", "Show provider status, live endpoints, and plan hints."),
            ["logs"] = new(new LogsCliCommand(runtime), "logs [--service <id>] [--follow]", "Tail orchestrated service logs via the active provider."),
            ["inspect"] = new(new InspectCliCommand(runtime), "inspect [--json]", "Detect project context, dependencies, and provider readiness.")
        };

    private static Task<int> ExecuteHelp(string[] helpArgs, Dictionary<string, CommandDefinition> commands)
    {
        if (helpArgs.Length == 0)
        {
            PrintUsage(commands);
            return Task.FromResult(0);
        }

        var target = helpArgs[0];
        if (!commands.TryGetValue(target, out var definition))
        {
            PrintUnknownCommand(target, commands);
            return Task.FromResult(1);
        }

        PrintCommandUsage(target, definition);
        return Task.FromResult(0);
    }

    private static bool HasHelpFlag(CommandArgs args)
        => args.HasFlag("help") || args.HasFlag("-h");

    private static bool IsHelpToken(string value)
        => HelpTokens.Any(token => string.Equals(value, token, StringComparison.OrdinalIgnoreCase));

    private static void PrintUsage(Dictionary<string, CommandDefinition> commands)
    {
        Console.WriteLine("Koan <command> [options]");
        foreach (var (name, definition) in commands.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine($"  {name,-10} {definition.Description}");
        }
    }

    private static void PrintUnknownCommand(string name, Dictionary<string, CommandDefinition> commands)
    {
        Console.WriteLine($"Unknown command '{name}'.");
        PrintUsage(commands);
    }

    private static void PrintCommandUsage(string name, CommandDefinition definition)
    {
        Console.WriteLine($"Usage: koan {definition.Usage}");
        if (!string.IsNullOrWhiteSpace(definition.Description))
        {
            Console.WriteLine(definition.Description);
        }
    }

    private sealed record CommandDefinition(ICliCommand Command, string Usage, string Description);
}
