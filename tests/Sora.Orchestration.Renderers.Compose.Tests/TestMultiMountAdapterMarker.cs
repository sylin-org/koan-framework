using Sora.Orchestration.Attributes;

[DefaultEndpoint("http", "localhost", 8080, "tcp", new[] { "test/multi" })]
[HostMount("/var/lib/serviceA")]
[HostMount("/var/lib/serviceB")]
internal sealed class TestMultiMountAdapterMarker { }
