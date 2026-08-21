using System.Diagnostics.CodeAnalysis;
using Koan.Data.Core;
using Microsoft.Extensions.DependencyInjection;

namespace AotRelational;

/// <summary>
/// One write and one read through the ordinary <c>Entity&lt;T&gt;</c> surface, against whichever
/// relational connector was referenced at build time. The point of the sample is that this file does
/// not change between SQLite and a server — only the connector reference and the connection string do.
/// </summary>
internal static class Program
{
    // Newtonsoft reaches the entity late-bound, so ILC must be told to keep it (nativeaot-howto.md §2).
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Note))]
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
        using var app = new ServiceCollection().StartKoan();

        // Name the adapter that actually took the call, so a silent fallback cannot pass as a proof.
        var adapter = app.Services
            .GetRequiredService<IDataService>()
            .GetScopeDiagnostics<Note, string>()
            .AdapterName;
        Console.WriteLine($"adapter={adapter}");

        var marker = $"aot-probe-{Guid.NewGuid():N}";
        var written = new Note { Title = marker, Stamp = DateTimeOffset.UtcNow };
        await written.Save();
        Console.WriteLine($"wrote id={written.Id} title={written.Title}");

        var read = await Note.Get(written.Id);
        if (read is null)
        {
            Console.Error.WriteLine("FAIL: the row written a moment ago read back as nothing.");
            return 1;
        }

        if (!string.Equals(read.Title, marker, StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"FAIL: read back title={read.Title}, expected {marker}.");
            return 1;
        }

        Console.WriteLine($"read  id={read.Id} title={read.Title} stamp={read.Stamp:O}");
        Console.WriteLine("OK");
        return 0;
    }
}
