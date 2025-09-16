using System;
using System.Net;
using System.Net.Http;
using Newtonsoft.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Koan.Secrets.Abstractions;
using Koan.Secrets.Vault;
using Xunit;

namespace Koan.Secrets.Vault.Tests;

public class VaultProviderTests
{
    private sealed class CaptureHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    [Fact]
    public async Task KvV2_path_and_value_materialization()
    {
        HttpRequestMessage? seenReq = null;
        var handler = new CaptureHandler(req =>
        {
            seenReq = req;
            var payload = new
            {
                data = new
                {
                    data = new { value = "s3cr3t" },
                    metadata = new { version = 2, created_time = "2024-01-01T00:00:00Z" }
                }
            };
            var json = JsonConvert.SerializeObject(payload);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
        });
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://vault:8200/") };
        client.DefaultRequestHeaders.Add("X-Vault-Token", "t");
        var factory = new SingleClientFactory(client);
        var opts = Options.Create(new VaultOptions { Enabled = true, Address = new Uri("https://vault:8200/"), Token = "t", Mount = "secret", UseKvV2 = true });
        var prov = new VaultSecretProvider(new ServiceCollection().BuildServiceProvider(), factory, opts, NullLogger<VaultSecretProvider>.Instance);

        var id = SecretId.Parse("secret+vault://db/main?version=2");
        var value = await prov.GetAsync(id);

        value.AsString().Should().Be("s3cr3t");
        value.Meta.Provider.Should().Be("vault");
        value.Meta.Version.Should().Be("2");
        seenReq.Should().NotBeNull();
        seenReq!.RequestUri!.ToString().Should().StartWith("https://vault:8200/v1/secret/data/db/main?version=2");
    }

    [Fact]
    public async Task KvV1_path_when_disabled_v2()
    {
        HttpRequestMessage? seenReq = null;
        var handler = new CaptureHandler(req =>
        {
            seenReq = req;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"value\":\"abc\"}") };
        });
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://vault:8200/") };
        client.DefaultRequestHeaders.Add("X-Vault-Token", "t");
        var factory = new SingleClientFactory(client);
        var opts = Options.Create(new VaultOptions { Enabled = true, Address = new Uri("https://vault:8200/"), Token = "t", Mount = "kv", UseKvV2 = false });
        var prov = new VaultSecretProvider(new ServiceCollection().BuildServiceProvider(), factory, opts, NullLogger<VaultSecretProvider>.Instance);

        var id = SecretId.Parse("secret://team/key"); // no provider forced => allowed
        var value = await prov.GetAsync(id);

        value.AsString().Should().Be("abc");
        seenReq.Should().NotBeNull();
        seenReq!.RequestUri!.ToString().Should().Be("https://vault:8200/v1/kv/team/key");
    }

    [Fact]
    public async Task Provider_mismatch_yields_not_found()
    {
        var handler = new CaptureHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://vault:8200/") };
        client.DefaultRequestHeaders.Add("X-Vault-Token", "t");
        var factory = new SingleClientFactory(client);
        var opts = Options.Create(new VaultOptions { Enabled = true, Address = new Uri("https://vault:8200/"), Token = "t" });
        var prov = new VaultSecretProvider(new ServiceCollection().BuildServiceProvider(), factory, opts, NullLogger<VaultSecretProvider>.Instance);

        var id = new SecretId("db", "main", Provider: "aws");
        Func<Task> act = async () => await prov.GetAsync(id);
        await act.Should().ThrowAsync<SecretNotFoundException>();
    }
}
