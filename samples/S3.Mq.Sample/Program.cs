using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using S3.Mq.Sample;
using Sora.Core;
using Sora.Data.Core;
using Sora.Messaging;

var services = new ServiceCollection();

// Minimal configuration for RabbitMQ
var cfg = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .Build();
services.AddSingleton<IConfiguration>(cfg);

// Add Sora with discovery; RabbitMQ initializer registers itself when referenced
services.AddSora();

// register handlers with simple chaining
services
    .OnMessage<Hello>((env, msg) =>
    {
        Console.WriteLine($"[handler] {env.TypeAlias} -> Hello, {msg.Name}!");
    })
    .OnMessage<UserRegistered>((env, msg) =>
    {
        Console.WriteLine($"[handler] {env.TypeAlias} -> Welcome user {msg.UserId} ({msg.Email})");
    })
    // batch handler: grouped processing for larger/paginated payloads
    .OnBatch<UserRegistered>((env, batch, ct) =>
    {
        Console.WriteLine($"[batch] {env.TypeAlias} -> Saving {batch.Items.Count} users...");
        // simulate save/commit
        foreach (var u in batch.Items)
            Console.WriteLine($"[batch] -> {u.UserId} ({u.Email})");
        return Task.CompletedTask;
    });

// alternatively, using a semantic builder
// services.OnMessages(h =>
// {
//     h.On<Hello>((env, msg) => Console.WriteLine($"[handler] {env.TypeAlias} -> Hello, {msg.Name}!"));
//     h.On<UserRegistered>((env, msg) => Console.WriteLine($"[handler] {env.TypeAlias} -> Welcome user {msg.UserId} ({msg.Email})"));
// });

// Build and start
var sp = services.BuildServiceProvider();
sp.UseSora();

Console.WriteLine("S3.Mq.Sample: sending two test messages...");

await new Hello { Name = "Sora" }.Send();
await new UserRegistered { UserId = "u-1", Email = "u1@example.com" }.Send();

// send a batch (grouped) example
var moreUsers = new List<UserRegistered>
{
    new() { UserId = "u-2", Email = "u2@example.com" },
    new() { UserId = "u-3", Email = "u3@example.com" },
    new() { UserId = "u-4", Email = "u4@example.com" }
};
await moreUsers.SendAsBatch();

Console.WriteLine("Message sent. Listening... Press Ctrl+C to exit.");

await Task.Delay(-1);

namespace S3.Mq.Sample
{
    public sealed class Hello { public string Name { get; init; } = string.Empty; }

    public sealed class UserRegistered
    {
        public string UserId { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
    }

// ...existing code...
}