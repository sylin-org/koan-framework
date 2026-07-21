using Koan.Data.Connector.Mongo;

namespace Koan.Data.Connector.Mongo.Tests.Specs.Connection;

/// <summary>
/// Characterization tests for <see cref="MongoConnectionString"/> (ARCH-0103 §L). These pin the exact
/// behavior harvested from the four former bespoke copies — the discovery/orchestration connection-string
/// paths that the integration oracle masks (its fixtures use explicit connections). They are the safety
/// net under which the helper's internals may be consolidated.
/// </summary>
public sealed class MongoConnectionStringSpec
{
    // ---- Build(host, port, db, user, pass) — the component builder ------------------------------------

    [Theory]
    [InlineData("localhost", 27017, "Koan", null, null, "mongodb://localhost:27017/Koan")]
    [InlineData("localhost", 27017, null, null, null, "mongodb://localhost:27017")]
    [InlineData("localhost", 27017, "", null, null, "mongodb://localhost:27017")]
    [InlineData("h", 27017, "db", "user", "pass", "mongodb://user:pass@h:27017/db")]
    [InlineData("h", 27017, "db", "user", null, "mongodb://user:@h:27017/db")]
    [InlineData("h", 27017, "", "user", "pass", "mongodb://user:pass@h:27017")]
    public void Build_from_components(string host, int port, string? db, string? user, string? pass, string expected)
        => MongoConnectionString.Build(host, port, db, user, pass).Should().Be(expected);

    // ---- Build(hostPort, db, user, pass) — the orchestration endpoint builder ------------------------

    [Theory]
    [InlineData("host", "db", null, null, "mongodb://host:27017/db")]
    [InlineData("host:1234", "db", null, null, "mongodb://host:1234/db")]
    [InlineData("host:1234", null, "u", "p", "mongodb://u:p@host:1234")]
    [InlineData("host:1234", "", null, null, "mongodb://host:1234")]
    public void Build_from_endpoint(string hostPort, string? db, string? user, string? pass, string expected)
        => MongoConnectionString.Build(hostPort, db, user, pass).Should().Be(expected);

    // ---- MergeOverrides — PRESERVE policy (keep existing auth; fill db only when path empty) ----------

    [Fact]
    public void MergeOverrides_fills_database_when_path_empty()
        => MongoConnectionString.MergeOverrides("mongodb://h:27017", "mydb", null, null)
            .Should().Be("mongodb://h:27017/mydb");

    [Fact]
    public void MergeOverrides_does_not_overwrite_existing_database()
        => MongoConnectionString.MergeOverrides("mongodb://h:27017/existing", "mydb", null, null)
            .Should().Be("mongodb://h:27017/existing");

    [Fact]
    public void MergeOverrides_preserves_existing_auth_ignoring_new_credentials()
        => MongoConnectionString.MergeOverrides("mongodb://user:pass@h:27017", "mydb", "newuser", "newpass")
            .Should().Be("mongodb://user:pass@h:27017/mydb");

    [Fact]
    public void MergeOverrides_adds_credentials_when_none_present()
        => MongoConnectionString.MergeOverrides("mongodb://h:27017", null, "u", "p")
            .Should().Be("mongodb://u:p@h:27017");

    [Fact]
    public void MergeOverrides_preserves_srv_scheme()
        => MongoConnectionString.MergeOverrides("mongodb+srv://cluster.example.com", "mydb", null, null)
            .Should().Be("mongodb+srv://cluster.example.com/mydb");

    [Fact]
    public void MergeOverrides_handles_replica_set_with_query_and_fills_database()
        => MongoConnectionString.MergeOverrides("mongodb://h1:27017,h2:27017/?replicaSet=rs0", "mydb", null, null)
            .Should().Be("mongodb://h1:27017,h2:27017/mydb?replicaSet=rs0");

    [Fact]
    public void MergeOverrides_returns_input_unchanged_when_not_a_uri()
        => MongoConnectionString.MergeOverrides("not-a-connection-string", "mydb", "u", "p")
            .Should().Be("not-a-connection-string");

    // ---- ApplyParameters — REPLACE policy (strip+replace auth; always replace db) ---------------------

    [Fact]
    public void ApplyParameters_replaces_database_on_bare_host()
        => MongoConnectionString.ApplyParameters("mongodb://h:27017", "mydb", null, null)
            .Should().Be("mongodb://h:27017/mydb");

    [Fact]
    public void ApplyParameters_strips_existing_auth_and_applies_new()
        => MongoConnectionString.ApplyParameters("mongodb://old:cred@h:27017", "mydb", "newu", "newp")
            .Should().Be("mongodb://newu:newp@h:27017/mydb");

    [Fact]
    public void ApplyParameters_replaces_existing_database()
        => MongoConnectionString.ApplyParameters("mongodb://h:27017/olddb", "newdb", null, null)
            .Should().Be("mongodb://h:27017/newdb");

    [Fact]
    public void ApplyParameters_replaces_database_preserving_query()
        => MongoConnectionString.ApplyParameters("mongodb://h:27017/olddb?w=1", "newdb", null, null)
            .Should().Be("mongodb://h:27017/newdb?w=1");

    [Fact]
    public void ApplyParameters_applies_auth_only_when_both_user_and_password()
        => MongoConnectionString.ApplyParameters("mongodb://h:27017", null, "useronly", null)
            .Should().Be("mongodb://h:27017");

    [Fact]
    public void ApplyParameters_strips_existing_auth_even_without_replacement()
        => MongoConnectionString.ApplyParameters("mongodb://old:cred@h:27017", null, null, null)
            .Should().Be("mongodb://h:27017");

    [Fact]
    public void ApplyParameters_no_op_when_nothing_supplied()
        => MongoConnectionString.ApplyParameters("mongodb://h:27017", null, null, null)
            .Should().Be("mongodb://h:27017");

    // ---- ResolveRoutedConnection — the non-Default "auto" quirk fix (ARCH-0103 §8 P4) ----------------

    [Fact]
    public void ResolveRoutedConnection_keeps_explicit_source_connection()
        => MongoConnectionString.ResolveRoutedConnection("mongodb://explicit:27017", "mongodb://default:27017")
            .Should().Be("mongodb://explicit:27017");

    [Theory]
    [InlineData("auto")]
    [InlineData("AUTO")]
    [InlineData("  auto  ")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ResolveRoutedConnection_falls_back_to_resolved_default_for_auto_or_blank(string? sourceConnection)
        => MongoConnectionString.ResolveRoutedConnection(sourceConnection, "mongodb://default:27017/Koan")
            .Should().Be("mongodb://default:27017/Koan");

    // ---- ExtractHost --------------------------------------------------------------------------------

    [Theory]
    [InlineData("mongodb://h:27017", "h:27017")]
    [InlineData("mongodb://h:1234/db", "h:1234")]
    [InlineData("h:27017", "h:27017")]
    public void ExtractHost_returns_endpoint(string input, string expected)
        => MongoConnectionString.ExtractHost(input).Should().Be(expected);
}
