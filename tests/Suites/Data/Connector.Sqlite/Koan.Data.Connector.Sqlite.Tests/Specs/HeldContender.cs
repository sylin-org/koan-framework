using System.Diagnostics;
using Koan.Core;
using Koan.Data.Abstractions.Annotations;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

[Storage(Name = "KOAN_HOLD_COUNTER")]
public sealed class HeldCounter : Entity<HeldCounter>
{
    public long Value { get; set; }
}

[Storage(Name = "KOAN_HOLD_NOTE")]
public sealed class HeldNote : Entity<HeldNote>
{
    public string Text { get; set; } = "";
}

/// <summary>
/// The other process of <see cref="SqliteHeldTransactionSpec"/>: this test assembly started again, booted over the same
/// database file through a real <c>AddKoan()</c> host.
/// </summary>
internal static class HeldContender
{
    public const string Verb = "--held-contender";
    private const string Held = "HELD";
    private const string Done = "DONE";

    public static async Task<int> Run(string[] args)
    {
        var (mode, path, argument) = (args[0], args[1], args[2]);
        await using var host = await KoanIntegrationHost.Configure()
            .WithSettings(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
                ["Koan:Data:Sources:Default:ConnectionString"] = $"Data Source={path}",
            })
            .ConfigureServices(services => services.AddKoan())
            .StartAsync(CancellationToken.None);

        switch (mode)
        {
            case "hold":
                using (await EntityContext.Hold("contender"))
                {
                    await new HeldNote { Id = argument, Text = "written by the other process" }.Save();
                    Console.Out.WriteLine(Held);
                    Console.Out.Flush();
                    // The spec answers on stdin once it has seen what it came to see.
                    if (await Console.In.ReadLineAsync() == "commit") await EntityContext.Commit();
                }
                break;
            case "count":
                var (id, times) = (argument, int.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture));
                for (var i = 0; i < times; i++)
                {
                    using (await EntityContext.Hold("contender"))
                    {
                        var counter = await HeldCounter.Get(id) ?? throw new InvalidOperationException("The counter is gone.");
                        counter.Value++;
                        await counter.Save();
                        await EntityContext.Commit();
                    }
                }
                break;
        }
        Console.Out.WriteLine(Done);
        return 0;
    }

    /// <summary>A contender and the bounded conversation a spec has with it.</summary>
    internal sealed class Process : IAsyncDisposable
    {
        private readonly System.Diagnostics.Process _process;
        private readonly TaskCompletionSource _held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<string> _output = [];

        private Process(System.Diagnostics.Process process)
        {
            _process = process;
            _process.OutputDataReceived += (_, line) =>
            {
                if (line.Data is null) return;
                lock (_output) _output.Add(line.Data);
                if (line.Data == Held) _held.TrySetResult();
                if (line.Data == Done) _done.TrySetResult();
            };
            _process.ErrorDataReceived += (_, line) =>
            {
                if (line.Data is not null) lock (_output) _output.Add(line.Data);
            };
        }

        public static Process Start(params string[] arguments)
        {
            var info = new ProcessStartInfo("dotnet")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            info.ArgumentList.Add("exec");
            info.ArgumentList.Add(typeof(HeldContender).Assembly.Location);
            info.ArgumentList.Add(Verb);
            foreach (var argument in arguments) info.ArgumentList.Add(argument);
            // A test host is not Production; the same posture as KoanIntegrationHost's default.
            info.Environment["DOTNET_ENVIRONMENT"] = "Test";
            var process = new Process(System.Diagnostics.Process.Start(info)!);
            process._process.BeginOutputReadLine();
            process._process.BeginErrorReadLine();
            return process;
        }

        public string Output
        {
            get
            {
                lock (_output) return string.Join(Environment.NewLine, _output);
            }
        }

        private async Task Bounded(Task task, string what)
        {
            var finished = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken));
            if (finished != task) throw new TimeoutException($"The contender did not {what}. It said:\n{Output}");
            await task;
        }

        public Task Holding() => Bounded(_held.Task, "take its hold");

        public async Task Say(string line)
        {
            await _process.StandardInput.WriteLineAsync(line);
            await _process.StandardInput.FlushAsync();
        }

        public Task Finished() => Bounded(_done.Task, "finish");

        public void Kill()
        {
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit(30_000);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!_process.HasExited) Kill();
            }
            catch (InvalidOperationException)
            {
            }
            _process.Dispose();
            await Task.CompletedTask;
        }
    }
}
