using System.Diagnostics;
using System.Reflection;
using AwesomeAssertions;

namespace Koan.Samples.S0Console.Tests;

public sealed class S0ConsoleGoldenPathSpec
{
    [Fact]
    public async Task Console_process_persists_explains_and_exits_cleanly()
    {
        var projectDirectory = ProjectDirectory();
        var dataDirectory = Path.Combine(Path.GetTempPath(), "koan-s0", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDirectory);

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("dotnet")
                {
                    WorkingDirectory = projectDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                }
            };
            process.StartInfo.ArgumentList.Add("run");
            process.StartInfo.ArgumentList.Add("--project");
            process.StartInfo.ArgumentList.Add(Path.Combine(projectDirectory, "S0.ConsoleJsonRepo.csproj"));
            process.StartInfo.ArgumentList.Add("-c");
            process.StartInfo.ArgumentList.Add("Release");
            process.StartInfo.ArgumentList.Add("--no-build");
            process.StartInfo.Environment["Koan__Data__Json__DirectoryPath"] = dataDirectory;
            process.StartInfo.Environment["DOTNET_NOLOGO"] = "1";

            process.Start().Should().BeTrue();
            var stdoutTask = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                throw;
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            process.ExitCode.Should().Be(0, stderr);
            stdout.Should().Contain("Koan selected adapter 'json'");
            stdout.Should().Contain("lockfile ok");
            stdout.Should().Contain("Checklist ready: 3 total, 1 complete, 2 open.");
            stdout.Should().Contain("Review the release notes");
            stdout.Should().Contain("Walk the dog");
            stdout.Should().Contain("Application is shutting down");
            (stdout + stderr).Should().NotContain("CollectionFailed");
            (stdout + stderr).Should().NotContain("Unhandled exception");

            var jsonFiles = Directory.GetFiles(dataDirectory, "*.json", SearchOption.AllDirectories);
            jsonFiles.Should().ContainSingle();
            var persisted = await File.ReadAllTextAsync(jsonFiles[0], TestContext.Current.CancellationToken);
            persisted.Should().Contain("Buy milk");
            persisted.Should().Contain("\"done\":true");
        }
        finally
        {
            if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, recursive: true);
        }
    }

    private static string ProjectDirectory()
        => Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "S0ProjectDirectory")
            .Value!;
}
