using AwesomeAssertions;
using Koan.Core;
using Koan.Data.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Koan.AI.Prompt.Tests;

public sealed class PromptCatalogTests
{
    [Fact]
    public async Task Referenced_catalog_resolves_latest_active_and_exact_versions()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddKoan();
        using var host = builder.Build();
        await host.StartAsync();

        var name = $"support-{Guid.NewGuid():N}";
        await new PromptEntry
        {
            Name = name,
            Version = 1,
            Status = PromptStatus.Active,
            Content = "Answer {question} briefly."
        }.Save();
        await new PromptEntry
        {
            Name = name,
            Version = 2,
            Status = PromptStatus.Active,
            Content = "Answer {question} with one sentence."
        }.Save();

        var latest = await PromptCatalog.Load(name);
        var pinned = await PromptCatalog.Load(name, 1);

        latest.Resolve(new { question = "Why?" })
            .Should().Be("Answer Why? with one sentence.");
        latest.Meta["version"].Should().Be("2");
        pinned.Meta["version"].Should().Be("1");

        await host.StopAsync();
    }

    [Fact]
    public async Task Missing_catalog_identity_fails_with_the_requested_name()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddKoan();
        using var host = builder.Build();
        await host.StartAsync();

        var name = $"missing-{Guid.NewGuid():N}";
        var load = async () => await PromptCatalog.Load(name);

        await load.Should().ThrowAsync<PromptNotFoundException>()
            .WithMessage($"*{name}*");

        await host.StopAsync();
    }

    [Fact]
    public async Task Duplicate_name_and_version_fails_instead_of_selecting_an_arbitrary_entity()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddKoan();
        using var host = builder.Build();
        await host.StartAsync();

        var name = $"duplicate-{Guid.NewGuid():N}";
        await new PromptEntry
        {
            Name = name,
            Version = 1,
            Status = PromptStatus.Active,
            Content = "first"
        }.Save();
        await new PromptEntry
        {
            Name = name,
            Version = 1,
            Status = PromptStatus.Active,
            Content = "second"
        }.Save();

        var load = async () => await PromptCatalog.Load(name, 1);

        await load.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*multiple entries*{name}*version 1*");

        await host.StopAsync();
    }
}
