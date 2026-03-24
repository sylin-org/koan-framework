using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using FluentAssertions;
using Koan.Core.Provenance;
using Koan.WebSockets;
using Koan.WebSockets.Initialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Koan.WebSockets.Tests;

public class KoanAutoRegistrarTests
{
    private const string ConfigurationSection = "Koan:Web:WebSockets";

    [Fact]
    public void Initialize_RegistersFactory()
    {
        var services = new ServiceCollection();
        var registrar = new KoanAutoRegistrar();

        registrar.Initialize(services);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IWebSocketStreamFactory>().Should().NotBeNull();
    }

    [Fact]
    public void Describe_PublishesOptionState()
    {
        var registrar = new KoanAutoRegistrar();
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            [$"{ConfigurationSection}:MessageType"] = WebSocketMessageType.Text.ToString(),
            [$"{ConfigurationSection}:LeaveOpen"] = bool.TrueString,
            [$"{ConfigurationSection}:SubProtocol"] = "chat.v2"
        });

        var environment = new FakeHostEnvironment();
        var registry = ProvenanceRegistry.Instance;
        var moduleWriter = registry.GetOrCreateModule("web", registrar.ModuleName);

        var snapshotBefore = CaptureModuleSettings(registry, registrar.ModuleName);

        try
        {
            ClearModuleSettings(registry, registrar.ModuleName);

            registrar.Describe(moduleWriter, configuration, environment);

            var snapshot = registry.CurrentSnapshot;
            var module = snapshot.Pillars
                .SelectMany(pillar => pillar.Modules)
                .FirstOrDefault(m => string.Equals(m.Name, registrar.ModuleName, StringComparison.OrdinalIgnoreCase));

            module.Should().NotBeNull();

            module!.Settings.Should().ContainSingle(setting => setting.Key == "message-type" && setting.Value == WebSocketMessageType.Text.ToString());
            module.Settings.Should().ContainSingle(setting => setting.Key == "leave-open" && setting.Value == bool.TrueString);
            module.Settings.Should().ContainSingle(setting => setting.Key == "sub-protocol" && setting.Value == "chat.v2");
        }
        finally
        {
            RestoreModuleSettings(registry, registrar.ModuleName, snapshotBefore);
        }
    }

    [Fact]
    public void Describe_WithMissingConfiguration_UsesDefaults()
    {
        var registrar = new KoanAutoRegistrar();
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            [$"{ConfigurationSection}:SubProtocol"] = ""
        });

        var environment = new FakeHostEnvironment();
        var registry = ProvenanceRegistry.Instance;
        var moduleWriter = registry.GetOrCreateModule("web", registrar.ModuleName);

        var snapshotBefore = CaptureModuleSettings(registry, registrar.ModuleName);

        try
        {
            ClearModuleSettings(registry, registrar.ModuleName);

            registrar.Describe(moduleWriter, configuration, environment);

            var snapshot = registry.CurrentSnapshot;
            var module = snapshot.Pillars
                .SelectMany(pillar => pillar.Modules)
                .FirstOrDefault(m => string.Equals(m.Name, registrar.ModuleName, StringComparison.OrdinalIgnoreCase));

            module.Should().NotBeNull();

            var messageTypeSetting = module!.Settings.Single(setting => setting.Key == "message-type");
            messageTypeSetting.Value.Should().Be(WebSocketMessageType.Binary.ToString());
            messageTypeSetting.State.Should().Be(ProvenanceSettingState.Default);

            var leaveOpenSetting = module.Settings.Single(setting => setting.Key == "leave-open");
            leaveOpenSetting.Value.Should().Be(bool.FalseString);
            leaveOpenSetting.State.Should().Be(ProvenanceSettingState.Default);

            var subProtocolSetting = module.Settings.Single(setting => setting.Key == "sub-protocol");
            subProtocolSetting.Value.Should().Be("(not-set)");
            subProtocolSetting.State.Should().Be(ProvenanceSettingState.Default);
        }
        finally
        {
            RestoreModuleSettings(registry, registrar.ModuleName, snapshotBefore);
        }
    }

    private static void ClearModuleSettings(ProvenanceRegistry registry, string moduleName)
    {
        var snapshot = registry.CurrentSnapshot;
        var module = snapshot.Pillars
            .SelectMany(pillar => pillar.Modules)
            .FirstOrDefault(m => string.Equals(m.Name, moduleName, StringComparison.OrdinalIgnoreCase));

        if (module is null)
        {
            return;
        }

        var writer = registry.GetOrCreateModule(module.PillarCode, moduleName);
        foreach (var setting in module.Settings)
        {
            writer.RemoveSetting(setting.Key);
        }
    }

    private static (string PillarCode, IReadOnlyList<ProvenanceSetting> Settings) CaptureModuleSettings(ProvenanceRegistry registry, string moduleName)
    {
        var snapshot = registry.CurrentSnapshot;
        var module = snapshot.Pillars
            .SelectMany(pillar => pillar.Modules)
            .FirstOrDefault(m => string.Equals(m.Name, moduleName, StringComparison.OrdinalIgnoreCase));

        if (module is null)
        {
            return ("web", []);
        }

        return (module.PillarCode, module.Settings.ToList());
    }

    private static void RestoreModuleSettings(ProvenanceRegistry registry, string moduleName, (string PillarCode, IReadOnlyList<ProvenanceSetting> Settings) snapshot)
    {
        ClearModuleSettings(registry, moduleName);

        if (snapshot.Settings.Count == 0)
        {
            return;
        }

        var writer = registry.GetOrCreateModule(snapshot.PillarCode, moduleName);

        foreach (var setting in snapshot.Settings)
        {
            writer.SetSetting(setting.Key, builder =>
            {
                builder
                    .Label(setting.Label)
                    .Description(setting.Description)
                    .Value(setting.Value)
                    .State(setting.State)
                    .Source(setting.Source, setting.SourceKey);

                if (setting.Consumers.Count > 0)
                {
                    builder.Consumers(setting.Consumers.ToArray());
                }

                if (setting.IsSecret)
                {
                    builder.Secret();
                }
            });
        }
    }

    private static IConfiguration BuildConfiguration(IReadOnlyDictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Koan.WebSockets.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
