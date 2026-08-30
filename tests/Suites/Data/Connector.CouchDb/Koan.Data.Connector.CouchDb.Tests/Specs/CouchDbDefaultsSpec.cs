using Koan.Core.Adapters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Koan.Data.Connector.CouchDb.Tests.Specs;

/// <summary>
/// The zero-configuration credential contract, pinned: with nothing configured, CouchDB resolves to
/// the development default admin/password — the same defaults the Testcontainers CouchDB modules and
/// the official image documentation use, viable because CouchDB 3.x refuses to start without an
/// admin user. The official image's own environment convention (COUCHDB_USER/COUCHDB_PASSWORD) sits
/// between configuration and the default, so the credentials an operator already typed for
/// `docker run` are honored without any application configuration. Explicit keys always win.
/// </summary>
public sealed class CouchDbDefaultsSpec : IDisposable
{
    private const string UserEnv = "COUCHDB_USER";
    private const string PasswordEnv = "COUCHDB_PASSWORD";

    public CouchDbDefaultsSpec()
    {
        Environment.SetEnvironmentVariable(UserEnv, null);
        Environment.SetEnvironmentVariable(PasswordEnv, null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(UserEnv, null);
        Environment.SetEnvironmentVariable(PasswordEnv, null);
    }

    private (string? UserId, string? Password) Resolve(Action<Dictionary<string, string?>>? settings = null)
    {
        var builder = new ConfigurationBuilder();
        if (settings is not null)
        {
            var values = new Dictionary<string, string?>();
            settings(values);
            builder.AddInMemoryCollection(values);
        }
        var options = new CouchDbOptions();
        new CouchDbOptionsConfigurator(
            builder.Build(),
            NullLogger<CouchDbOptionsConfigurator>.Instance,
            Options.Create(new AdaptersReadinessOptions())
        ).Configure(options);
        return (options.UserId, options.Password);
    }

    [Fact]
    public void Nothing_configured_resolves_to_the_development_default()
    {
        var (userId, password) = Resolve();
        userId.Should().Be("admin");
        password.Should().Be("password");
    }

    [Fact]
    public void Image_environment_convention_beats_the_default()
    {
        Environment.SetEnvironmentVariable(UserEnv, "couch");
        Environment.SetEnvironmentVariable(PasswordEnv, "other");
        var (userId, password) = Resolve();
        userId.Should().Be("couch");
        password.Should().Be("other");
    }

    [Fact]
    public void Explicit_configuration_beats_both()
    {
        Environment.SetEnvironmentVariable(UserEnv, "couch");
        Environment.SetEnvironmentVariable(PasswordEnv, "other");
        var (userId, password) = Resolve(settings =>
        {
            settings["Koan:Data:CouchDb:UserId"] = "configured";
            settings["Koan:Data:CouchDb:Password"] = "configured-pass";
        });
        userId.Should().Be("configured");
        password.Should().Be("configured-pass");
    }
}
