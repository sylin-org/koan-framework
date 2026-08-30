using System;
using Koan.Data.Connector.CouchDb;

namespace Koan.Data.Connector.CouchDb.Tests.Specs;

/// <summary>
/// The one endpoint grammar every CouchDB entry point accepts: the factory's route resolution, the
/// client's wire base address, and discovery's health validation all go through
/// <see cref="CouchDbEndpoint.Parse"/>. A `couchdb://` URI carries credentials in its user-info and
/// normalizes to http (port 5984 when omitted); an http(s) URL keeps its scheme and its user-info
/// the same way. This is the contract whose absence made discovery refuse a connection string the
/// application path accepted.
/// </summary>
public sealed class CouchDbEndpointSpec
{
    [Fact]
    public void Couchdb_uri_normalizes_to_http_with_default_port_and_credentials()
    {
        var (endpoint, user, password) = CouchDbEndpoint.Parse("couchdb://koan:secret@localhost");

        endpoint.Scheme.Should().Be("http");
        endpoint.Port.Should().Be(5984);
        endpoint.UserInfo.Should().BeEmpty();
        user.Should().Be("koan");
        password.Should().Be("secret");
    }

    [Fact]
    public void Couchdb_uri_keeps_an_explicit_port()
    {
        var (endpoint, _, _) = CouchDbEndpoint.Parse("couchdb://localhost:55984");

        endpoint.Scheme.Should().Be("http");
        endpoint.Port.Should().Be(55984);
    }

    [Fact]
    public void Https_url_keeps_its_scheme()
    {
        var (endpoint, _, _) = CouchDbEndpoint.Parse("https://couch.example.com:6984");

        endpoint.Scheme.Should().Be("https");
        endpoint.Port.Should().Be(6984);
    }

    [Fact]
    public void Http_url_credentials_are_returned_not_inlined()
    {
        var (endpoint, user, password) = CouchDbEndpoint.Parse("http://admin:p%40ss@localhost:5984");

        endpoint.UserInfo.Should().BeEmpty();
        user.Should().Be("admin");
        password.Should().Be("p@ss");
    }

    [Fact]
    public void Values_outside_the_grammar_reject_correctively()
    {
        var act = () => CouchDbEndpoint.Parse("not-a-uri");
        act.Should().Throw<ArgumentException>().WithParameterName("value");
    }
}
