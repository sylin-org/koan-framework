using System.Diagnostics;
using System.Text;

namespace Koan.Packaging.Infrastructure;

internal sealed class ProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken,
        bool echo = false,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // Reusable MSBuild workers can outlive their dotnet parent while retaining inherited
        // redirected pipe handles. Packaging favors deterministic child completion over node reuse.
        startInfo.Environment[PackagingConstants.MsBuildDisableNodeReuseEnvironmentVariable] =
            PackagingConstants.MsBuildDisableNodeReuseEnvironmentValue;
        if (environment is not null)
        {
            foreach (var pair in environment) startInfo.Environment[pair.Key] = pair.Value;
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stdoutTask = CaptureAsync(process.StandardOutput, echo ? Console.Out : null, cancellationToken);
        var stderrTask = CaptureAsync(process.StandardError, echo ? Console.Error : null, cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    public async Task<string> RequireAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken,
        bool echo = false,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        var result = await RunAsync(fileName, arguments, workingDirectory, cancellationToken, echo, environment);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{fileName} failed with exit code {result.ExitCode}.{Environment.NewLine}{result.StandardError}{result.StandardOutput}");
        }

        return result.StandardOutput.Trim();
    }

    private static async Task<string> CaptureAsync(
        StreamReader reader,
        TextWriter? liveOutput,
        CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            output.AppendLine(line);
            if (liveOutput is not null) await liveOutput.WriteLineAsync(line.AsMemory(), cancellationToken);
        }
        return output.ToString();
    }
}

internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
