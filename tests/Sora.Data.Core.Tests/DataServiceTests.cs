using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sora.Data.Abstractions;
using Xunit;

namespace Sora.Data.Core.Tests;

public class DataServiceTests
{
    [Fact]
    public async Task Resolves_Per_Type_Repository_Via_Provider_Config()
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new[] { new KeyValuePair<string, string?>("SORA_DATA_PROVIDER", "json") }).Build();
        var sc = new ServiceCollection();
        sc.AddSingleton<IConfiguration>(cfg);
        sc.AddSoraDataCore();
        sc.AddSingleton<IDataService, DataService>();
        // Directory can be provided via options if desired; default is .\\data
        var sp = sc.BuildServiceProvider();

        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<Todo, string>();
        var saved = await repo.UpsertAsync(new Todo { Title = "x" });
        saved.Id.Should().NotBeNullOrWhiteSpace();
    }

    public class Todo : Sora.Data.Abstractions.IEntity<string>
    {
        [Identifier]
        public string Id { get; set; } = default!;
        public string Title { get; set; } = string.Empty;
    }
}