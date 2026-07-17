using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using CustomerCanon.Domain;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Web.Extensions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Koan.Samples.CustomerCanon.Tests;

public sealed class CustomerCanonGoldenPathSpec
{
    [Fact]
    public async Task Messy_arrivals_converge_and_invalid_input_never_becomes_canonical()
    {
        var root = Path.Combine(Path.GetTempPath(), "customer-canon", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        AppHost.Current = null;

        try
        {
            _ = typeof(Customer);
            using var host = await Host.CreateDefaultBuilder()
                .ConfigureWebHost(web => web
                    .UseTestServer()
                    .UseEnvironment("Development")
                    .ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Koan:Environment"] = "Development",
                            ["Koan:Data:Sources:Default:Adapter"] = "json",
                            ["Koan:Data:Sources:Default:DirectoryPath"] = root,
                            ["Koan:BackgroundServices:Enabled"] = "false"
                        }))
                    .ConfigureServices(services => services.AddKoan().AsWebApi())
                    .Configure(_ => { }))
                .StartAsync(TestContext.Current.CancellationToken);

            var client = host.GetTestClient();
            client.BaseAddress = new Uri("http://localhost");

            (await client.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.OK);

            var models = await client.GetFromJsonAsync<JsonElement>("/api/canon/models");
            var modelItems = models.EnumerateArray().ToArray();
            modelItems.Should().ContainSingle();
            var customerModel = modelItems[0];
            customerModel.GetProperty("slug").GetString().Should().Be("customer");
            customerModel.GetProperty("hasPipeline").GetBoolean().Should().BeTrue();
            customerModel.GetProperty("aggregationKeys").EnumerateArray()
                .Select(static value => value.GetString()).Should().ContainSingle().Which.Should().Be("Email");

            var arrival = new
            {
                email = " Alice@Example.COM ",
                phone = "+1 (212) 555-0123",
                firstName = " Alice ",
                lastName = " Example ",
                country = "us",
                language = "EN"
            };

            var firstResponse = await client.PostAsJsonAsync("/api/canon/customer", arrival);
            firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var first = await firstResponse.Content.ReadFromJsonAsync<JsonElement>();
            var firstCanonical = first.GetProperty("canonical");
            var canonicalId = firstCanonical.GetProperty("id").GetString();
            first.GetProperty("outcome").GetString().Should().Be("Canonized");
            firstCanonical.GetProperty("email").GetString().Should().Be("alice@example.com");
            firstCanonical.GetProperty("phone").GetString().Should().Be("+12125550123");
            firstCanonical.GetProperty("displayName").GetString().Should().Be("Alice Example");
            firstCanonical.GetProperty("accountTier").GetString().Should().Be("Premium");
            firstCanonical.GetProperty("state").GetProperty("readiness").GetString().Should().Be("Complete");

            var secondResponse = await client.PostAsJsonAsync("/api/canon/customer", arrival);
            secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var second = await secondResponse.Content.ReadFromJsonAsync<JsonElement>();
            second.GetProperty("canonical").GetProperty("id").GetString().Should().Be(canonicalId);

            var invalidResponse = await client.PostAsJsonAsync("/api/canon/customer", new
            {
                email = "not-an-email",
                firstName = "Invalid",
                lastName = "Arrival"
            });
            invalidResponse.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            var invalid = await invalidResponse.Content.ReadFromJsonAsync<JsonElement>();
            invalid.GetProperty("outcome").GetString().Should().Be("Failed");
            invalid.GetProperty("events")[0].GetProperty("detail").GetString()
                .Should().Contain("Invalid email format");

            using (AppHost.PushScope(host.Services))
            {
                var customers = await Customer.All(TestContext.Current.CancellationToken);
                customers.Should().ContainSingle().Which.Id.Should().Be(canonicalId);
            }

            Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories).Should().NotBeEmpty();
            var facts = await client.GetStringAsync("/.well-known/Koan/facts");
            facts.Should().Contain("Koan.Canon");
            facts.Should().Contain("koan.semantic.component.active");
            facts.Should().Contain("complete");

            await host.StopAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            AppHost.Current = null;
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch { /* best-effort temporary cleanup */ }
        }
    }
}
