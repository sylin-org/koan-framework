using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Testing.Integration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

/// <summary>
/// ARCH-0140: Koan attaches the host and starts its modules (Data hydrates its routes) before any hosted service's
/// <c>StartAsync</c>, so an application hosted service registered before <c>AddKoan()</c> starts with a working
/// <c>Entity&lt;T&gt;</c> surface.
/// </summary>
public sealed class SqliteEarlyHostedServiceSpec
{
    [Fact(DisplayName = "Sqlite: a hosted service registered before AddKoan() saves and reads an entity in its start")]
    public async Task A_hosted_service_registered_before_AddKoan_starts_with_an_attached_host()
    {
        var file = Path.Combine(Path.GetTempPath(), $"koan-sqlite-early-{Guid.CreateVersion7():n}.db");
        EarlyProbe.Reset();
        var host = await KoanIntegrationHost.Configure()
            .WithSetting("Koan:Environment", "Test")
            .WithSetting("Koan:Data:Sqlite:DdlPolicy", "AutoCreate")
            .WithSetting("Koan:Data:Sqlite:ConnectionString", $"Data Source={file}")
            .WithSetting("Koan:Data:Sources:Default:Adapter", "sqlite")
            .WithSetting("Koan:Data:Sources:Default:ConnectionString", $"Data Source={file}")
            .ConfigureServices(services =>
            {
                services.AddHostedService<EarlyProbe>();
                services.AddKoan();
            })
            .StartAsync();
        try
        {
            EarlyProbe.Failure.Should().BeNull();
            EarlyProbe.AttachedHost.Should().NotBeNull("Koan attached the host before this service started");
            EarlyProbe.ReadBack.Should().Be("early");
        }
        finally
        {
            await host.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (File.Exists(file)) File.Delete(file);
        }
    }

    public sealed class EarlyNote : Entity<EarlyNote>
    {
        public string Text { get; set; } = "";
    }

    private sealed class EarlyProbe : IHostedService
    {
        public static IServiceProvider? AttachedHost { get; private set; }
        public static string? ReadBack { get; private set; }
        public static Exception? Failure { get; private set; }

        public static void Reset()
        {
            AttachedHost = null;
            ReadBack = null;
            Failure = null;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                AttachedHost = AppHost.Current;
                var note = await new EarlyNote { Text = "early" }.Save(cancellationToken);
                ReadBack = (await EarlyNote.Get(note.Id, cancellationToken))?.Text;
            }
            catch (Exception error)
            {
                Failure = error;
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
