using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sora.Data.Abstractions;
using Sora.Data.Core;
using Xunit;
using Xunit.Sdk;

namespace Sora.Data.Mongo.Tests;

public class MongoPagingGuardrailsTests : IClassFixture<MongoAutoFixture>
{
    private readonly IServiceProvider? _sp;

    public MongoPagingGuardrailsTests(MongoAutoFixture fx)
    {
        _sp = fx.Services;
    }

    private record Todo(string Id, string Title) : Sora.Data.Abstractions.IEntity<string>;

    [Fact]
    public async Task Query_Should_Apply_Default_Limit_When_Unpaged()
    {
    if (_sp is null) return; // effectively skip when Mongo isn't available
        var repo = _sp.GetRequiredService<IDataService>().GetRepository<Todo, string>();
        // Seed 300 docs
        for (int i = 0; i < 300; i++)
            await repo.UpsertAsync(new Todo(Guid.NewGuid().ToString("n"), "t" + i));

        var opts = _sp.GetRequiredService<IOptions<Sora.Data.Mongo.MongoOptions>>().Value;
        var results = await repo.QueryAsync(null!);
    results.Count.Should().Be(opts.DefaultPageSize, "default page size should bound unpaged queries");
    await TestMongoTeardown.DropDatabaseAsync(_sp);
    }

    [Fact]
    public async Task LinqQuery_Should_Apply_Default_Limit_When_Unpaged()
    {
    if (_sp is null) return; // effectively skip
        var repo = (ILinqQueryRepository<Todo, string>)_sp.GetRequiredService<IDataService>().GetRepository<Todo, string>();
        // Seed 120 docs with same prefix
        for (int i = 0; i < 120; i++)
            await ((IDataRepository<Todo, string>)repo).UpsertAsync(new Todo(Guid.NewGuid().ToString("n"), "same"));

        var opts = _sp.GetRequiredService<IOptions<Sora.Data.Mongo.MongoOptions>>().Value;
        var results = await repo.QueryAsync(x => x.Title == "same");
    results.Count.Should().Be(opts.DefaultPageSize);
    await TestMongoTeardown.DropDatabaseAsync(_sp);
    }
}
