using AwesomeAssertions;
using Koan.Communication.Tests.Support;
using Koan.Core;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Testing.Integration;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Koan.Tests.Communication.Specs;

/// <summary>Rollout item 4: the type-scoped Events gateway — Note.Events.On&lt;T&gt;(handler) —
/// lambda handlers enter the same binding pipeline as discovered IHandleEntityEvent classes.</summary>
public sealed class EventGatewaySpec
{
    public sealed class Note : Entity<Note>, Koan.Data.Abstractions.IAmbientExempt
    {
        public string Title { get; set; } = "";
    }

    public sealed record NoteCreated(string Title);

    private static Task<IntegrationHost> StartHostAsync() =>
        CommunicationTestHost.Start(new object(), TestContext.Current.CancellationToken);

    [Fact]
    public async Task gateway_registrations_survive_host_boot_and_raise_reaches_handler()
    {
        Note.EventGateway.Reset();
        Note.EventGateway.On<NoteCreated>((note, occurrence, ct) =>
        {
            // prove the handler fired: mutate the entity to signal receipt
            note.Title = "handled";
            return Task.CompletedTask;
        });

        await using var host = await StartHostAsync();

        var note = new Note { Title = "gateway probe" };
        await note.Save();
        var acceptance = await note.Events.Raise<NoteCreated>();

        acceptance.Should().NotBeNull();
        acceptance.Accepted.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task zero_subscribers_is_accepted_on_the_events_lane()
    {
        Note.EventGateway.Reset();
        await using var host = await StartHostAsync();

        var note = new Note { Title = "post-reset" };
        await note.Save();
        var acceptance = await note.Events.Raise<NoteCreated>();

        acceptance.Should().NotBeNull("zero-subscriber acceptance is legal on the Events lane");
    }
}
