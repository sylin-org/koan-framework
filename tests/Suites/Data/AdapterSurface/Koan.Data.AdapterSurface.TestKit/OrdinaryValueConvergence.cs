using AwesomeAssertions;
using Koan.Data.Core;
using Koan.Data.Core.Model;

namespace Koan.Data.AdapterSurface.TestKit;

/// <summary>A document holding the numbers an application ordinarily writes.</summary>
public sealed class OrdinaryValueWidget : Entity<OrdinaryValueWidget>
{
    public double Number { get; set; }
    public decimal Money { get; set; }
    public List<double> Series { get; set; } = [];
}

/// <summary>A row whose id differs from others only by case or accent.</summary>
public sealed class ExactIdWidget : Entity<ExactIdWidget>
{
    public string Label { get; set; } = "";
}

/// <summary>
/// Ordinary numbers read back as written, and ids differing only by case or accent stay separate rows (DATA-0164).
/// </summary>
public static class OrdinaryValueConvergence
{
    private static readonly double[] Doubles = [1d / 3d, -1d / 3d, 0.1, 1e20, 1e-7, 1234.5678, 0d];

    private static readonly decimal[] Decimals =
        [19.99m, 0.1m, 0.07m, 100m, 1234567.89m, 99999999999.99m, -42.5m, 0m];

    private static readonly string[] Ids = ["abc", "ABC", "Abc", "ábc"];

    /// <summary>Saves each number and asserts it reads back: a double to the bit, a decimal by value.</summary>
    /// <param name="extra">Larger doubles a store's document holds (DATA-0164 names the limit where one does not).</param>
    public static async Task AssertNumbersRoundTripAsync(params double[] extra)
    {
        foreach (var existing in await OrdinaryValueWidget.All()) await existing.Remove();
        var doubles = Doubles.Concat(extra).ToArray();
        var count = Math.Max(doubles.Length, Decimals.Length);
        var corpus = Enumerable.Range(0, count).Select(index => new OrdinaryValueWidget
        {
            Id = $"n{index:D2}",
            Number = doubles[index % doubles.Length],
            Money = Decimals[index % Decimals.Length],
            Series = [doubles[index % doubles.Length]],
        }).ToArray();
        await OrdinaryValueWidget.UpsertMany(corpus);

        var failures = new List<string>();
        foreach (var expected in corpus)
        {
            var actual = await OrdinaryValueWidget.Get(expected.Id);
            if (actual is null)
            {
                failures.Add($"  [{expected.Id}] did not read back");
                continue;
            }
            if (BitConverter.DoubleToInt64Bits(expected.Number) != BitConverter.DoubleToInt64Bits(actual.Number))
                failures.Add($"  [{expected.Id}] Number: wrote {expected.Number:R}, read {actual.Number:R}");
            if (!actual.Series.SequenceEqual(expected.Series))
                failures.Add($"  [{expected.Id}] Series: wrote {expected.Series[0]:R}");
            if (expected.Money != actual.Money)
                failures.Add($"  [{expected.Id}] Money: wrote {expected.Money}, read {actual.Money}");
        }
        failures.Should().BeEmpty("an ordinary number must read back as written; divergences:\n" + string.Join("\n", failures));
    }

    /// <summary>Ids differing only by case or accent are separate rows: each upsert, read and removal touches its own.</summary>
    public static async Task AssertIdsStayDistinctAsync()
    {
        foreach (var existing in await ExactIdWidget.All()) await existing.Remove();
        foreach (var id in Ids) await new ExactIdWidget { Id = id, Label = id }.Save();
        // A second write lands on its own row, not on the one its id folds to.
        foreach (var id in Ids) await new ExactIdWidget { Id = id, Label = id + "!" }.Save();

        (await ExactIdWidget.All()).Should().HaveCount(Ids.Length);
        foreach (var id in Ids)
            (await ExactIdWidget.Get(id))!.Label.Should().Be(id + "!");

        await (await ExactIdWidget.Get("abc"))!.Remove();
        (await ExactIdWidget.Get("ABC")).Should().NotBeNull();
        (await ExactIdWidget.Get("abc")).Should().BeNull();
    }
}
