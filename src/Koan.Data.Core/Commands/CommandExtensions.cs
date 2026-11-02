using Koan.Core.Hosting.App;

namespace Koan.Data.Core.Commands;

/// <summary>A command's own verbs (ARCH-0135), which reach this host's <see cref="CommandQueue"/>.</summary>
public static class CommandExtensions
{
    extension<TResult>(ICommand<TResult> command)
    {
        /// <summary>Queues the command, to run in this context, and answers with its outcome once it has run. <paramref name="ct"/> bounds
        /// this wait and never the command: once queued, a cancelled wait throws <see cref="OperationCanceledException"/> and the command
        /// still runs. Refused from inside an open transaction or a running command, which would wait for themselves.</summary>
        public Task<Outcome<TResult>> Send(CancellationToken ct = default) =>
            AppHost.GetRequiredService<CommandQueue>("command send").Send(command, ct);

        /// <summary>Queues the command only where there is room, never waiting: null, and the command never runs, when the queue is
        /// full or has ended. For work a later attempt makes good. Refused from inside a running command, as a send is.</summary>
        public Task<Outcome<TResult>>? TrySend() =>
            AppHost.GetRequiredService<CommandQueue>("command send").TrySend(command);
    }
}
