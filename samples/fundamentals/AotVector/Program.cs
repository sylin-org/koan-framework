using System.Diagnostics.CodeAnalysis;
using Koan.Core;
using Koan.Data.Core;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace AotVector;

/// <summary>
/// The vector-plane twin of the <c>AotRelational</c> sample: one save, one read, one search, and one
/// delete through the ordinary <c>Vector&lt;T&gt;</c> surface, published as a single NativeAOT binary.
/// The point is the same as the relational sample's — this file does not change between stores; only
/// the connector reference and the endpoint do.
/// </summary>
internal static class Program
{
    // Newtonsoft reaches the metadata materializer late-bound; keep the metadata shape intact (ILC
    // also trims anonymous-type properties, so metadata crosses as a dictionary, not an anonymous object).
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ProbeVectorDocument))]
    internal static async Task<int> Main()
    {
        // A NativeAOT binary turns an unhandled exception into a fail-fast, which Windows reports as
        // a stack-buffer overrun dialog and which blocks an unattended run. Report it instead.
        try
        {
            return await Probe();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL: {ex.GetType().FullName}: {ex.Message}");
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static async Task<int> Probe()
    {
        var services = new ServiceCollection();
        services.AddKoan(koan => koan
            .Data.Source("Default")
            .Vector<ProbeVectorDocument>(space => space
                .Name("probe")
                .Dimensions(8)
                .Metric(VectorMetric.Cosine)
                .Visibility(VectorVisibility.Session)));

        using var app = services.StartKoan();

        // Name the adapter that actually took the call, so a silent fallback cannot pass as a proof.
        var adapter = app.Services.GetRequiredService<IVectorAdapterFactory>();
        Console.WriteLine($"adapter={adapter.GetType().Name}");

        var embedding = new float[8];
        embedding[0] = 1f;
        var marker = $"aot-vector-{Guid.NewGuid():N}";
        await Vector<ProbeVectorDocument>.Save("aot-probe", embedding, new Dictionary<string, object?>
        {
            ["Marker"] = marker
        });
        Console.WriteLine("wrote id=aot-probe dims=8");

        var read = await Vector<ProbeVectorDocument>.Get("aot-probe");
        if (read is null)
        {
            Console.Error.WriteLine("FAIL: the point written a moment ago read back as nothing.");
            return 1;
        }
        if (read.Embedding.Length != 8)
        {
            Console.Error.WriteLine($"FAIL: read back {read.Embedding.Length} dimensions, expected 8.");
            return 1;
        }

        var hits = await Vector<ProbeVectorDocument>.Search(embedding, query => query.Top(3));
        if (hits.Items.Count == 0 || hits.Items[0].Id != "aot-probe")
        {
            Console.Error.WriteLine("FAIL: the search did not return the point just written as the nearest hit.");
            return 1;
        }

        if (!await Vector<ProbeVectorDocument>.Delete("aot-probe"))
        {
            Console.Error.WriteLine("FAIL: delete reported the point missing right after reading it.");
            return 1;
        }
        if (await Vector<ProbeVectorDocument>.Get("aot-probe") is not null)
        {
            Console.Error.WriteLine("FAIL: the point read back after a successful delete.");
            return 1;
        }

        Console.WriteLine($"search hits=[{string.Join(',', hits.Items.Select(h => h.Id))}]");
        Console.WriteLine("OK");
        return 0;
    }
}

/// <summary>One ordinary vector document. Nothing here knows which vector store it lands in.</summary>
public sealed class ProbeVectorDocument : Koan.Data.Core.Model.Entity<ProbeVectorDocument>;
