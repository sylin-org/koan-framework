using FluentAssertions;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Xunit;

namespace S0.ConsoleJsonRepo.Tests;

public class RunSampleTests
{
    [Fact]
    public async Task Sample_Runs_And_Emits_Expected_Lines()
    {
        var root = FindRepoRoot();
        var project = Path.Combine(root, "samples", "S0.ConsoleJsonRepo", "S0.ConsoleJsonRepo.csproj");
        var tmp = Path.Combine(Path.GetTempPath(), "Koan-s0-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(tmp);

        var psi = new ProcessStartInfo("dotnet", $"run --project \"{project}\" \"{tmp}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var p = Process.Start(psi)!;
        var outTask = p.StandardOutput.ReadToEndAsync();
        var errTask = p.StandardError.ReadToEndAsync();
        var waitTask = p.WaitForExitAsync();
        var winner = await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromSeconds(15)));
        var exited = winner == waitTask;
        if (!exited)
        {
            try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
            // give it a moment to flush/exit
            await Task.WhenAny(waitTask, Task.Delay(2000));
        }
        var output = await outTask;
        var error = await errTask;
        exited.Should().BeTrue("sample should finish quickly");
        p.ExitCode.Should().Be(0, error);

        output.Should().Contain("Created:");
        output.Should().Contain("Batch:");
        output.Should().Contain("Total items:");
    }

    private static string FindRepoRoot([CallerFilePath] string? thisFile = null)
    {
        var dir = Path.GetDirectoryName(thisFile!) ?? Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(dir, "Koan.sln")))
        {
            dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException("Could not locate Koan.sln");
        }
        return dir;
    }
}
