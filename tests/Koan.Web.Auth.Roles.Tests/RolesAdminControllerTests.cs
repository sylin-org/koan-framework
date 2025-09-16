using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.FileProviders;
using Koan.Web.Auth.Roles.Contracts;
using Koan.Web.Auth.Roles.Controllers;
using Koan.Web.Auth.Roles.Options;
using Koan.Web.Auth.Roles.Services.Stores;
using Xunit;

namespace Koan.Web.Auth.Roles.Tests;

public class RolesAdminControllerTests
{
    private static (RolesAdminController ctrl, InMemoryStores stores) CreateController(Action<RoleAttributionOptions>? configure = null, bool production = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();
    services.AddSingleton<IRoleStore>(new InMemoryRoleStore());
    services.AddSingleton<IRoleAliasStore>(new InMemoryRoleAliasStore());
    services.AddSingleton<IRolePolicyBindingStore>(new InMemoryRolePolicyBindingStore());
        var cache = new LocalCache();
        services.AddSingleton<IRoleAttributionCache>(cache);
        services.AddSingleton<IRoleConfigSnapshotProvider, Koan.Web.Auth.Roles.Services.DefaultRoleConfigSnapshotProvider>();
        services.AddSingleton<IHostEnvironment>(sp => new HostEnvironmentStub(production ? "Production" : "Development"));
        services.AddSingleton<IOptionsMonitor<RoleAttributionOptions>>(sp =>
        {
            var opts = new RoleAttributionOptions();
            configure?.Invoke(opts);
            return new OptionsMonitorStub<RoleAttributionOptions>(opts);
        });

        var provider = services.BuildServiceProvider();

        // Controller with authenticated admin principal (policy evaluated elsewhere)
        var ctrl = new RolesAdminController(
            provider.GetRequiredService<IRoleStore>(),
            provider.GetRequiredService<IRoleAliasStore>(),
            provider.GetRequiredService<IRolePolicyBindingStore>(),
            provider.GetRequiredService<IOptionsMonitor<RoleAttributionOptions>>(),
            provider.GetRequiredService<IHostEnvironment>(),
            provider.GetRequiredService<IRoleAttributionCache>(),
            provider.GetRequiredService<IRoleConfigSnapshotProvider>()
        )
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "admin") }, "test"))
                }
            }
        };

    return (ctrl, new InMemoryStores(provider, cache));
    }

    [Fact]
    public async Task Import_dryRun_returns_diff_without_applying()
    {
        var (ctrl, stores) = CreateController(o =>
        {
            o.Roles = new() { new RoleAttributionOptions.RoleSeed { Id = "admin" } };
            o.Aliases.Map["admins"] = "admin";
            o.PolicyBindings = new() { new RoleAttributionOptions.RolePolicyBindingSeed { Id = "auth.roles.admin", Requirement = "role:admin" } };
        });

        var result = await ctrl.Import(dryRun: true, force: true);
        result.Result.Should().BeOfType<OkObjectResult>();

        // Nothing should be in stores yet
        (await stores.Roles.All()).Should().BeEmpty();
        (await stores.Aliases.All()).Should().BeEmpty();
        (await stores.Bindings.All()).Should().BeEmpty();
    }

    [Fact]
    public async Task Import_applies_template_and_reload_clears_cache()
    {
        var (ctrl, stores) = CreateController(o =>
        {
            o.Roles = new() { new RoleAttributionOptions.RoleSeed { Id = "admin" }, new RoleAttributionOptions.RoleSeed { Id = "reader" } };
            o.Aliases.Map.Clear();
            o.Aliases.Map["admins"] = "admin";
            o.PolicyBindings = new() { new RoleAttributionOptions.RolePolicyBindingSeed { Id = "auth.roles.admin", Requirement = "role:admin" } };
        });

        var ok = await ctrl.Import(dryRun: false, force: true);
        ok.Result.Should().BeOfType<OkObjectResult>();

        var roles = await stores.Roles.All();
        roles.Select(r => r.Id).Should().BeEquivalentTo(new[] { "admin", "reader" });

        var aliases = await stores.Aliases.All();
        aliases.Select(a => a.Id).Should().BeEquivalentTo(new[] { "admins" });

        var bindings = await stores.Bindings.All();
        bindings.Select(b => b.Id).Should().BeEquivalentTo(new[] { "auth.roles.admin" });

        var reload = await ctrl.Reload(default);
        reload.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task Import_rejects_in_production_without_allow_flag()
    {
        var (ctrl, _) = CreateController(o =>
        {
            o.Roles = new() { new RoleAttributionOptions.RoleSeed { Id = "admin" } };
            o.AllowSeedingInProduction = false;
        }, production: true);

        var result = await ctrl.Import(dryRun: false, force: true);
        var r = result.Result as ObjectResult;
        r!.StatusCode.Should().Be(403);
    }

    private sealed class OptionsMonitorStub<T>(T current) : IOptionsMonitor<T> where T : class
    {
        public T CurrentValue => current;
        public T Get(string? name) => current;
        public IDisposable OnChange(Action<T, string?> listener) => new NullDisposable();
        private sealed class NullDisposable : IDisposable { public void Dispose() { } }
    }

    private sealed class HostEnvironmentStub(string env) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = env;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class InMemoryStores(IServiceProvider sp, LocalCache cache)
    {
        public IRoleStore Roles => sp.GetRequiredService<IRoleStore>();
        public IRoleAliasStore Aliases => sp.GetRequiredService<IRoleAliasStore>();
        public IRolePolicyBindingStore Bindings => sp.GetRequiredService<IRolePolicyBindingStore>();
        public IRoleConfigSnapshotProvider Snapshot => sp.GetRequiredService<IRoleConfigSnapshotProvider>();
        public LocalCache Cache { get; } = cache;
    }

    private sealed class LocalCache : IRoleAttributionCache
    {
        private readonly Dictionary<string, RoleAttributionResult> _cache = new(StringComparer.Ordinal);
        public RoleAttributionResult? TryGet(string principalId) => _cache.TryGetValue(principalId, out var v) ? v : null;
        public void Set(string principalId, RoleAttributionResult result) => _cache[principalId] = result;
        public void Clear() => _cache.Clear();
        public int Count => _cache.Count;
    }

    [Fact]
    public async Task Import_apply_clears_cache_and_reload_updates_snapshot()
    {
        var (ctrl, stores) = CreateController(o =>
        {
            o.Roles = new() { new RoleAttributionOptions.RoleSeed { Id = "admin" }, new RoleAttributionOptions.RoleSeed { Id = "reader" } };
            o.Aliases.Map.Clear();
            o.Aliases.Map["admins"] = "admin";
            o.PolicyBindings = new() { new RoleAttributionOptions.RolePolicyBindingSeed { Id = "auth.roles.admin", Requirement = "role:admin" } };
        });

        // Seed cache with a fake entry
        stores.Cache.Set("u1", new RoleAttributionResult(new HashSet<string>(), new HashSet<string>(), "s1"));
        stores.Cache.Count.Should().Be(1);

        var apply = await ctrl.Import(dryRun: false, force: true);
        apply.Result.Should().BeOfType<OkObjectResult>();
        stores.Cache.Count.Should().Be(0, "import must clear attribution cache after applying changes");

        // Reload and verify snapshot reflects changes
        var reload = await ctrl.Reload(default);
        reload.Should().BeOfType<NoContentResult>();
        var snap = stores.Snapshot.Get();
        snap.Aliases.Should().ContainKey("admins").WhoseValue.Should().Be("admin");
        snap.PolicyBindings.Should().ContainKey("auth.roles.admin").WhoseValue.Should().Be("role:admin");
    }

    [Fact]
    public async Task Reload_endpoint_also_clears_cache()
    {
        var (ctrl, stores) = CreateController();
        stores.Cache.Set("u2", new RoleAttributionResult(new HashSet<string>(), new HashSet<string>(), "s2"));
        stores.Cache.Count.Should().Be(1);
        var res = await ctrl.Reload(default);
        res.Should().BeOfType<NoContentResult>();
        stores.Cache.Count.Should().Be(0);
    }

    private sealed class InMemoryRoleStore : IRoleStore
    {
        private readonly Dictionary<string, IKoanAuthRole> _items = new(StringComparer.OrdinalIgnoreCase);
        public Task<IReadOnlyList<IKoanAuthRole>> All(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<IKoanAuthRole>>(_items.Values.ToList());
        public Task UpsertMany(IEnumerable<IKoanAuthRole> items, CancellationToken ct = default)
        {
            foreach (var i in items) _items[i.Id] = i;
            return Task.CompletedTask;
        }
        public Task<bool> Delete(string id, CancellationToken ct = default) => Task.FromResult(_items.Remove(id));
    }

    private sealed class InMemoryRoleAliasStore : IRoleAliasStore
    {
        private readonly Dictionary<string, IKoanAuthRoleAlias> _items = new(StringComparer.OrdinalIgnoreCase);
        public Task<IReadOnlyList<IKoanAuthRoleAlias>> All(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<IKoanAuthRoleAlias>>(_items.Values.ToList());
        public Task UpsertMany(IEnumerable<IKoanAuthRoleAlias> items, CancellationToken ct = default)
        {
            foreach (var i in items) _items[i.Id] = i;
            return Task.CompletedTask;
        }
        public Task<bool> Delete(string id, CancellationToken ct = default) => Task.FromResult(_items.Remove(id));
    }

    private sealed class InMemoryRolePolicyBindingStore : IRolePolicyBindingStore
    {
        private readonly Dictionary<string, IKoanAuthRolePolicyBinding> _items = new(StringComparer.OrdinalIgnoreCase);
        public Task<IReadOnlyList<IKoanAuthRolePolicyBinding>> All(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<IKoanAuthRolePolicyBinding>>(_items.Values.ToList());
        public Task UpsertMany(IEnumerable<IKoanAuthRolePolicyBinding> items, CancellationToken ct = default)
        {
            foreach (var i in items) _items[i.Id] = i;
            return Task.CompletedTask;
        }
        public Task<bool> Delete(string id, CancellationToken ct = default) => Task.FromResult(_items.Remove(id));
    }
}
