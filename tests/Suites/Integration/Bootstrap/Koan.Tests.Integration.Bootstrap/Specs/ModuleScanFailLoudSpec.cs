using System;
using System.IO;
using System.Reflection;
using AwesomeAssertions;
using Koan.Core.Hosting.Bootstrap;
using Xunit;

namespace Koan.Tests.Integration.Bootstrap.Specs;

/// <summary>
/// ARCH-0138: the assembly-scan prongs tolerate what they were written to tolerate (an unloadable reference, file or
/// manifest line) and never a <see cref="KoanBootException"/> from the registry manifest loader. Whether that failure
/// stops boot is decided once, by <c>KOAN_BOOT_LENIENT</c>, inside the loader wrapper.
/// </summary>
public sealed class ModuleScanFailLoudSpec
{
    [Fact]
    public void A_manifest_loader_failure_escapes_the_embedded_manifest_prong()
    {
        var boot = Boot();

        var scan = () => AppBootstrapper.LoadIntentModulesFromManifest(
            (_, _) => throw boot,
            typeof(ModuleScanFailLoudSpec).Assembly);

        scan.Should().Throw<KoanBootException>().Which.Should().BeSameAs(boot);
    }

    [Fact]
    public void A_manifest_loader_failure_escapes_the_shared_scan_step()
    {
        var boot = Boot();

        var scan = () => AppBootstrapper.TryScan(
            () => typeof(ModuleScanFailLoudSpec).Assembly,
            _ => throw boot,
            out _);

        scan.Should().Throw<KoanBootException>().Which.Should().BeSameAs(boot);
    }

    [Fact]
    public void An_unloadable_reference_is_a_counted_skip()
    {
        var added = AppBootstrapper.TryScan(
            () => throw new FileNotFoundException("planted missing reference"),
            _ => true,
            out var skipped);

        added.Should().BeFalse();
        skipped.Should().BeTrue();
    }

    [Fact]
    public void Nothing_to_load_is_neither_added_nor_skipped()
    {
        var added = AppBootstrapper.TryScan(() => null, _ => true, out var skipped);

        added.Should().BeFalse();
        skipped.Should().BeFalse();
    }

    private static KoanBootException Boot() => new(
        typeof(ModuleScanFailLoudSpec),
        "Koan.Spec.Scan",
        "1.0.0",
        "manifest-invoker(scanning 'Koan.Spec.Scan')",
        new InvalidOperationException("planted manifest-loader failure"));
}
