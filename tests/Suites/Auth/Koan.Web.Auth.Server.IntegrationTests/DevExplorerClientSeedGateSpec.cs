using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Data.Core;
using Koan.Web.Auth.Server.Protocol;
using Xunit;

namespace Koan.Web.Auth.Server.IntegrationTests;

/// <summary>
/// SEC-0006 addendum (WEB-0072 P3) — the fail-closed gates on the dev-client seed. The operator opt-out
/// (<c>SeedDevClient=false</c>) keeps even Development free of the seeded client, and the device endpoint refuses
/// the now-unknown client with <c>invalid_client</c> — the exerciser's honest dead-end.
/// </summary>
[Collection(AuthServerHostCollection.Name)]
public sealed class DevExplorerClientSeedDisabledSpec : IClassFixture<SeedDisabledFixture>
{
    private readonly SeedDisabledFixture _fx;
    public DevExplorerClientSeedDisabledSpec(SeedDisabledFixture fx) => _fx = fx;

    private static CancellationToken Ct => new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token;

    [Fact]
    public async Task Knob_off_leaves_no_seeded_client()
    {
        var client = await OAuthClient.Get(AuthServerModule.DevExplorerClientId, Ct);
        client.Should().BeNull("SeedDevClient=false suppresses the seed even in Development");
    }

    [Fact]
    public async Task Device_request_for_the_unseeded_client_fails_closed()
    {
        using var device = _fx.NewClient();
        var res = await device.PostAsync("/oauth/device", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = AuthServerModule.DevExplorerClientId,
            ["scope"] = "mcp.read",
            ["resource"] = $"{_fx.BaseUrl}/mcp",
        }), Ct);

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await res.Content.ReadAsStringAsync(Ct)).Should().Contain("invalid_client");
    }
}

/// <summary>
/// SEC-0006 addendum (WEB-0072 P3) — the security-critical env gate. Even with the knob at its default (true), a
/// non-Development host must <b>never</b> seed the known <c>koan-dev-explorer</c> client. This is the takeover-vector
/// guard, not a preference.
/// </summary>
[Collection(AuthServerHostCollection.Name)]
public sealed class DevExplorerClientProductionGateSpec : IClassFixture<ProductionSeedGateFixture>
{
    private readonly ProductionSeedGateFixture _fx;
    public DevExplorerClientProductionGateSpec(ProductionSeedGateFixture fx) => _fx = fx;

    private static CancellationToken Ct => new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token;

    [Fact]
    public async Task Production_never_seeds_the_known_dev_client()
    {
        var client = await OAuthClient.Get(AuthServerModule.DevExplorerClientId, Ct);
        client.Should().BeNull("the env gate keeps the guessable dev client out of production — a takeover-vector guard");
    }
}
