using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Diagnostics;
using Koan.Core.Observability.Health;
using Koan.Data.Core;
using Koan.Jobs;
using Microsoft.Extensions.DependencyInjection;
using OrderIntake.Domain;

namespace Koan.Samples.OrderIntake.Tests;

public sealed class OrderIntakeGoldenPathSpec(OrderIntakeFixture fixture) : IClassFixture<OrderIntakeFixture>
{
    [Fact]
    public async Task Fresh_host_completes_local_intake_cleans_its_orders_and_corrects_an_unavailable_target()
    {
        using var client = fixture.CreateClient();

        var dashboard = await client.GetAsync("/", TestContext.Current.CancellationToken);
        dashboard.StatusCode.Should().Be(HttpStatusCode.OK);
        (await dashboard.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().Contain("One honest receipt");

        (await client.GetAsync("/health/ready", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var health = fixture.Services.GetServices<IHealthContributor>()
            .Where(contributor => contributor.Name.StartsWith("data:", StringComparison.Ordinal))
            .ToDictionary(contributor => contributor.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var optional in new[] { "data:mongo", "data:postgres", "data:redis" })
        {
            health[optional].IsCritical.Should().BeFalse();
            (await health[optional].Check(TestContext.Current.CancellationToken)).State
                .Should().Be(HealthState.Unknown);
        }

        using var submitted = await ReadJson(
            await client.PostAsync("/api/trials/Local?count=24", content: null, TestContext.Current.CancellationToken),
            HttpStatusCode.Accepted);
        var trialId = submitted.RootElement.GetProperty("id").GetString()!;

        using var completed = await WaitForStatus(client, trialId, "Completed");
        var receipt = completed.RootElement.GetProperty("receipt");
        receipt.GetProperty("target").GetString().Should().Be("Local");
        receipt.GetProperty("requested").GetInt32().Should().Be(24);
        receipt.GetProperty("written").GetInt32().Should().Be(24);
        receipt.GetProperty("readBack").GetInt32().Should().Be(24);
        receipt.GetProperty("verified").GetInt32().Should().Be(24);
        receipt.GetProperty("removed").GetInt32().Should().Be(24);
        receipt.GetProperty("provider").GetString().Should().ContainEquivalentOf("Sqlite");
        receipt.GetProperty("capabilities").GetArrayLength().Should().BeGreaterThan(0);
        receipt.GetProperty("framework").GetString().Should().Contain(".NET");

        var expectedIds = Enumerable.Range(1, 24)
            .Select(sequence => TrialOrder.For(trialId, sequence).Id)
            .ToArray();
        using (EntityContext.Source(WorkloadTarget.Local.ToString()))
        {
            (await TrialOrder.Get(expectedIds, TestContext.Current.CancellationToken))
                .Should().OnlyContain(order => order == null);
        }

        var persisted = await OrderIntakeTrial.Get(trialId, TestContext.Current.CancellationToken);
        persisted.Should().NotBeNull();
        persisted!.Receipt.Should().NotBeNull();
        persisted.Receipt!.Verified.Should().Be(24);

        var records = await OrderIntakeTrial.Jobs.Query(
            new JobQuery(WorkId: trialId),
            TestContext.Current.CancellationToken);
        records.Should().ContainSingle(record =>
            record.Status == JobStatus.Completed
            && record.ProgressFraction == 1
            && record.ProgressMessage!.Contains("Receipt ready", StringComparison.Ordinal));

        using var unavailableSubmission = await ReadJson(
            await client.PostAsync("/api/trials/Documents?count=2", content: null, TestContext.Current.CancellationToken),
            HttpStatusCode.Accepted);
        var unavailableId = unavailableSubmission.RootElement.GetProperty("id").GetString()!;
        using var failed = await WaitForStatus(client, unavailableId, "Failed", "Dead");
        failed.RootElement.TryGetProperty("receipt", out _).Should().BeFalse(
            "the public JSON policy omits null receipt values");
        failed.RootElement.GetProperty("error").GetString().Should().NotBeNullOrWhiteSpace();
        failed.RootElement.GetProperty("correction").GetString().Should().Be(
            "Start MongoDB with: docker compose -f samples/applications/OrderIntake/docker/compose.yml up -d mongo");

        (await client.GetAsync("/health/ready", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable,
                "an explicitly selected unavailable source is now an active dependency");
        health["data:mongo"].IsCritical.Should().BeTrue();
        (await health["data:mongo"].Check(TestContext.Current.CancellationToken)).State
            .Should().Be(HealthState.Unhealthy);

        var factsResponse = await client.GetAsync("/.well-known/Koan/facts", TestContext.Current.CancellationToken);
        factsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await factsResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().NotContain("CollectionFailed");

        var snapshot = fixture.Services.GetRequiredService<IKoanRuntimeFacts>().Current;
        snapshot.Complete.Should().BeTrue();
        snapshot.Facts.Should().Contain(fact =>
            fact.Subject == "data:default"
            && fact.Summary.Contains("sqlite", StringComparison.OrdinalIgnoreCase));
        snapshot.Facts.Should().Contain(fact =>
            fact.Subject == "data:local"
            && fact.Summary.Contains("sqlite", StringComparison.OrdinalIgnoreCase));
        snapshot.Facts.Should().Contain(fact =>
            fact.Code == "koan.jobs.ledger.selected"
            && fact.Summary.Contains("durable", StringComparison.OrdinalIgnoreCase));
        snapshot.Facts.Should().NotContain(fact => fact.State == KoanFactState.CollectionFailed);
    }

    private static async Task<JsonDocument> WaitForStatus(
        HttpClient client,
        string id,
        params string[] terminalStatuses)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));

        while (true)
        {
            using var response = await client.GetAsync($"/api/trials/{id}", timeout.Token);
            var document = await ReadJson(response, HttpStatusCode.OK);
            var status = document.RootElement.GetProperty("status").GetString();
            if (terminalStatuses.Contains(status, StringComparer.Ordinal)) return document;
            document.Dispose();
            await Task.Delay(50, timeout.Token);
        }
    }

    private static async Task<JsonDocument> ReadJson(HttpResponseMessage response, HttpStatusCode expected)
    {
        response.StatusCode.Should().Be(expected);
        return await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken),
            cancellationToken: TestContext.Current.CancellationToken);
    }
}
