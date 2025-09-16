using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Koan.Web.Auth.Roles.Contracts;
using Koan.Web.Auth.Roles.Extensions;
using Koan.Web.Auth.Roles.Options;
using Koan.Web.Auth.Roles.Services;
using Koan.Web.Auth.Roles.Services.Stores;
using Xunit;

namespace Koan.Web.Auth.Roles.Tests;

public class DefaultRoleAttributionServiceTests
{
    private static IRoleAttributionService CreateService(Action<RoleAttributionOptions>? configure = null, Action<IServiceCollection>? extra = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKoanWebAuthRoles();
    services.AddSingleton<IRoleBootstrapStateStore>(new InMemoryBootstrapStore());
        extra?.Invoke(services);

        // Replace stores with in-memory fakes by leveraging default stores over Json repo if needed in future.
        services.AddSingleton<IOptionsMonitor<RoleAttributionOptions>>(sp =>
        {
            var opts = new RoleAttributionOptions();
            configure?.Invoke(opts);
            return new OptionsMonitorStub<RoleAttributionOptions>(opts);
        });

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IRoleAttributionService>();
    }

    [Fact]
    public async Task Extracts_roles_from_claims_and_applies_aliases()
    {
        var svc = CreateService(o =>
        {
            o.ClaimKeys.Roles = new[] { ClaimTypes.Role, "roles" };
            o.Aliases.Map["admins"] = "admin";
            o.MaxRoles = 10;
            o.MaxPermissions = 10;
        });

        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "u1"));
        identity.AddClaim(new Claim(ClaimTypes.Role, "admins"));
        identity.AddClaim(new Claim("roles", "reader, writer"));
        var user = new ClaimsPrincipal(identity);

        var result = await svc.ComputeAsync(user);

        result.Roles.Should().BeEquivalentTo(new[] { "admin", "reader", "writer" });
        result.Permissions.Should().BeEmpty();
        result.Stamp.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Bootstrap_FirstUser_adds_admin_once()
    {
        var svc = CreateService(o =>
        {
            o.Bootstrap = new RoleAttributionOptions.BootstrapOptions
            {
                Mode = "FirstUser"
            };
            o.MaxRoles = 10;
            o.MaxPermissions = 10;
        });

        // First call should elevate
        var u1 = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "user-1"),
            new Claim(ClaimTypes.Role, "reader")
        }, "test"));

        var r1 = await svc.ComputeAsync(u1);
        r1.Roles.Should().Contain("admin");

        // Second user should not be elevated because state is persisted
        var u2 = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "user-2"),
            new Claim(ClaimTypes.Role, "reader")
        }, "test"));

        var r2 = await svc.ComputeAsync(u2);
        r2.Roles.Should().NotContain("admin");
    }

    [Fact]
    public async Task ClaimMatch_bootstrap_respects_claim_type_and_values()
    {
        var svc = CreateService(o =>
        {
            o.Bootstrap = new RoleAttributionOptions.BootstrapOptions
            {
                Mode = "ClaimMatch",
                ClaimType = ClaimTypes.Email,
                ClaimValues = new[] { "admin@unit.test" }
            };
        });

        var elevated = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "user-1"),
            new Claim(ClaimTypes.Email, "admin@unit.test")
        }, "test"));

        var r1 = await svc.ComputeAsync(elevated);
        r1.Roles.Should().Contain("admin");

        var notElevated = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "user-2"),
            new Claim(ClaimTypes.Email, "user@unit.test")
        }, "test"));

        var r2 = await svc.ComputeAsync(notElevated);
        r2.Roles.Should().NotContain("admin");
    }

    [Fact]
    public async Task DevFallback_applies_reader_when_enabled_and_no_roles()
    {
        // Simulate development via environment variable
        var original = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        try
        {
            var svc = CreateService(o =>
            {
                o.DevFallback.Enabled = true;
                o.DevFallback.Role = "reader";
                o.ClaimKeys.Roles = new[] { "none" }; // ensure we don't read any roles
            });

            var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "user-1")
            }, "test"));

            var r = await svc.ComputeAsync(user);
            r.Roles.Should().Contain("reader");
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", original);
        }
    }

    [Fact]
    public async Task DevFallback_not_applied_in_production()
    {
        var original = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
        try
        {
            var svc = CreateService(o =>
            {
                o.DevFallback.Enabled = true;
                o.DevFallback.Role = "reader";
                o.ClaimKeys.Roles = new[] { "none" };
            });

            var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "user-1")
            }, "test"));

            var r = await svc.ComputeAsync(user);
            r.Roles.Should().BeEmpty();
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", original);
        }
    }

    [Fact]
    public async Task Alias_mapping_prefers_snapshot_over_options()
    {
        // Options define admins->admin, but snapshot will define admins->superadmin
        var aliasStore = new InMemoryAliasStore(new Dictionary<string, IKoanAuthRoleAlias>
        {
            ["admins"] = new Alias("admins", "superadmin")
        });

        IRoleAttributionService? svc = null;
        IRoleConfigSnapshotProvider? snap = null;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKoanWebAuthRoles();
        services.AddSingleton<IRoleBootstrapStateStore>(new InMemoryBootstrapStore());
        services.AddSingleton<IRoleAliasStore>(aliasStore);
        services.AddSingleton<IRolePolicyBindingStore>(new InMemoryBindingStore());
        services.AddSingleton<IOptionsMonitor<RoleAttributionOptions>>(sp =>
        {
            var opts = new RoleAttributionOptions();
            opts.ClaimKeys.Roles = new[] { ClaimTypes.Role, "roles" };
            opts.Aliases.Map["admins"] = "admin"; // would be ignored after reload
            return new OptionsMonitorStub<RoleAttributionOptions>(opts);
        });
        var provider = services.BuildServiceProvider();
        svc = provider.GetRequiredService<IRoleAttributionService>();
        snap = provider.GetRequiredService<IRoleConfigSnapshotProvider>();

        await snap.ReloadAsync();

        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "u1"));
        identity.AddClaim(new Claim(ClaimTypes.Role, "admins"));
        var user = new ClaimsPrincipal(identity);

        var result = await svc.ComputeAsync(user);
        result.Roles.Should().Contain("superadmin");
    }

    [Fact]
    public async Task Duplicate_roles_are_deduped_after_aliasing()
    {
        var svc = CreateService(o =>
        {
            o.ClaimKeys.Roles = new[] { ClaimTypes.Role };
            o.Aliases.Map["admins"] = "admin";
        });

        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "u1"));
        identity.AddClaim(new Claim(ClaimTypes.Role, "admins"));
        identity.AddClaim(new Claim(ClaimTypes.Role, "admin"));
        var user = new ClaimsPrincipal(identity);

        var result = await svc.ComputeAsync(user);
        result.Roles.Should().BeEquivalentTo(new[] { "admin" });
    }

    private sealed class OptionsMonitorStub<T>(T current) : IOptionsMonitor<T> where T : class
    {
        public T CurrentValue => current;
        public T Get(string? name) => current;
        public IDisposable OnChange(Action<T, string?> listener) => NullLogger.Instance.BeginScope("");
    }

    private sealed class InMemoryBootstrapStore : IRoleBootstrapStateStore
    {
        private bool _bootstrapped;
        public Task<bool> IsAdminBootstrappedAsync(CancellationToken ct = default) => Task.FromResult(_bootstrapped);
        public Task MarkAdminBootstrappedAsync(string userId, string mode, CancellationToken ct = default)
        {
            _bootstrapped = true;
            return Task.CompletedTask;
        }
    }

    private sealed class Alias(string id, string target) : IKoanAuthRoleAlias
    {
        public string Id { get; set; } = id;
        public string TargetRole { get; set; } = target;
        public byte[]? RowVersion { get; set; }
    }
    private sealed class InMemoryAliasStore(Dictionary<string, IKoanAuthRoleAlias>? seed = null) : IRoleAliasStore
    {
        private readonly Dictionary<string, IKoanAuthRoleAlias> _items = seed ?? new(StringComparer.OrdinalIgnoreCase);
        public Task<IReadOnlyList<IKoanAuthRoleAlias>> All(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<IKoanAuthRoleAlias>>(_items.Values.ToList());
        public Task UpsertMany(IEnumerable<IKoanAuthRoleAlias> items, CancellationToken ct = default) { foreach (var i in items) _items[i.Id] = i; return Task.CompletedTask; }
        public Task<bool> Delete(string id, CancellationToken ct = default) => Task.FromResult(_items.Remove(id));
    }
    private sealed class InMemoryBindingStore : IRolePolicyBindingStore
    {
        private readonly Dictionary<string, IKoanAuthRolePolicyBinding> _items = new(StringComparer.OrdinalIgnoreCase);
        public Task<IReadOnlyList<IKoanAuthRolePolicyBinding>> All(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<IKoanAuthRolePolicyBinding>>(_items.Values.ToList());
        public Task UpsertMany(IEnumerable<IKoanAuthRolePolicyBinding> items, CancellationToken ct = default) { foreach (var i in items) _items[i.Id] = i; return Task.CompletedTask; }
        public Task<bool> Delete(string id, CancellationToken ct = default) => Task.FromResult(_items.Remove(id));
    }
}
