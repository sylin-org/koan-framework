using System.Linq;
using AwesomeAssertions;
using Koan.Core.Ordering;
using Xunit;

namespace Koan.Web.OpenGraph.Tests;

/// <summary>
/// Guards the CORE-0091 ordering declaration: the OpenGraph pillar is a web-layer module and must be
/// initialized after the Koan.Web core registrar (the convention the other web pillars follow).
/// </summary>
public sealed class AutoRegistrarOrderingTests
{
    [Fact]
    public void Registrar_declares_initialization_after_the_web_core()
    {
        var registrar = typeof(Koan.Web.OpenGraph.Initialization.KoanAutoRegistrar);

        var after = registrar
            .GetCustomAttributes(typeof(AfterAttribute), inherit: false)
            .Cast<AfterAttribute>()
            .SelectMany(a => a.Targets);

        after.Should().Contain(typeof(Koan.Web.Initialization.KoanAutoRegistrar));
    }
}
