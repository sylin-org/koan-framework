using Koan.Orchestration.Attributes;

[DefaultEndpoint("postgres", "localhost", 5432, "tcp", new[] { "test/postgres" })]
[HostMount("/var/lib/postgresql/data")]
internal sealed class TestPostgresAdapterMarker { }
