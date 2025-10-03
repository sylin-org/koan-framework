using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Xunit;

namespace Koan.Data.Connector.Mongo.Tests;

public class MongoPagingGuardrailsTests : IClassFixture<MongoAutoFixture>
{
    private readonly IServiceProvider? _sp;

    public MongoPagingGuardrailsTests(MongoAutoFixture fx)
    {
        _sp = fx.Services;
    }

    private record Todo(string Id, string Title) : IEntity<string>;

    // DATA-0061: unpaged queries should materialize the full set
    [Fact]
    public async Task Query_Should_Return_All_When_Unpaged()
    {
        if (_sp is null) return; // effectively skip when Mongo isn't available
        var repo = _sp.GetRequiredService<IDataService>().GetRepository<Todo, string>();
        // Seed 300 docs
        for (int i = 0; i < 300; i++)
            await repo.UpsertAsync(new Todo(Guid.NewGuid().ToString("n"), "t" + i));

        var results = await repo.QueryAsync(null!);
        results.Count.Should().Be(300);
        await TestMongoTeardown.DropDatabaseAsync(_sp);
    }

    // DATA-0061: LINQ unpaged returns full predicate result
    [Fact]
    public async Task LinqQuery_Should_Return_All_When_Unpaged()
    {
        if (_sp is null) return; // effectively skip
        var repo = (ILinqQueryRepository<Todo, string>)_sp.GetRequiredService<IDataService>().GetRepository<Todo, string>();
        // Seed 120 docs with same prefix
        for (int i = 0; i < 120; i++)
            await repo.UpsertAsync(new Todo(Guid.NewGuid().ToString("n"), "same"));

        var results = await repo.QueryAsync(x => x.Title == "same");
        results.Count.Should().Be(120);
        await TestMongoTeardown.DropDatabaseAsync(_sp);
    }
}

