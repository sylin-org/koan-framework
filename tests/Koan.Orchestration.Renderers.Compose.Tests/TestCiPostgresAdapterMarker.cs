using Koan.Orchestration.Attributes;

[DefaultEndpoint("postgres", "localhost", 5432, "tcp", new[] { "test/ci-postgres" })]
[HostMount("/var/lib/postgresql/data")]
internal sealed class TestCiPostgresAdapterMarker { }
