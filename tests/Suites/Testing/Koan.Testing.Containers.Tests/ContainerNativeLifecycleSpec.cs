using AwesomeAssertions;
using Koan.Testing.Containers;
using Npgsql;

namespace Koan.Testing.Containers.Tests;

public sealed class ContainerNativeLifecycleSpec
{
    [Fact]
    [Trait("KoanLane", "native")]
    public async Task Postgres_fixture_supports_two_complete_owned_lifecycles()
    {
        for (var iteration = 0; iteration < 2; iteration++)
        {
            await using var fixture = new PostgresFixture();
            await fixture.InitializeAsync();

            fixture.IsAvailable.Should().BeTrue();
            fixture.ConnectionString.Should().NotBeNullOrWhiteSpace();

            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            var result = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
            result.Should().Be(1);
        }
    }
}
