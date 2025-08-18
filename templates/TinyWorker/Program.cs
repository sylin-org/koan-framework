using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sora.Core;
using Sora.Data.Core;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSora();
// JSON adapter auto-discovers

builder.Services.AddHostedService<TimedJob>();

var app = builder.Build();
await app.RunAsync();

sealed class TimedJob : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested)
        {
            Console.WriteLine($"[{DateTimeOffset.Now:u}] Hello from TinyWorker");
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
