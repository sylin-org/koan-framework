using Koan.Testing.Integration;
using Koan.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Koan.Identity.Erasure;
using Koan.Identity.Roles;
using Xunit;

namespace Koan.Identity.Tests;

public sealed class IdentityHostFixture : IAsyncLifetime
{
    public const string DevUser = "devboss";
    private IntegrationHost? _host;
    public IServiceProvider Services => _host?.Services ?? throw new InvalidOperationException("Host not started.");
    public async ValueTask InitializeAsync()
    {
        _host = await KoanIntegrationHost.Configure().ConfigureServices(services =>
        {
            services.TryAddEnumerable(ServiceDescriptor.Scoped<IIdentityErasureContributor, TestIdentityErasureContributor>());
            services.AddKoan();
        }).StartAsync();
    }
    public async ValueTask DisposeAsync() { if (_host is not null) await _host.DisposeAsync(); }
}

internal sealed class IdentityRoleTestClient(RoleCollection roles)
{
    public async Task<Role> GrantAsync(string person, string key)
    {
        if (await roles.Get(key) is null) await roles.Define(key, key, ["global:test"]);
        return await roles.Add(key, person);
    }
    public async Task<bool> RevokeAsync(string person, string key)
    {
        if (await roles.Get(key) is null) return false;
        await roles.Remove(key, person); return true;
    }
    public async Task<IReadOnlyList<TestRoleGrant>> ListAsync(string person)
        => (await roles.ForPerson(person)).Select(role => new TestRoleGrant(role.Id)).ToArray();
}
internal sealed record TestRoleGrant(string RoleKey);
