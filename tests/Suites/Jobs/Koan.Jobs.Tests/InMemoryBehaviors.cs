using Koan.Jobs;
using Koan.Jobs.TestKit;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Jobs.Tests;

/// <summary>Runs the shared <see cref="JobBehaviorSuite"/> on the in-memory tier.</summary>
public sealed class InMemoryBehaviors : JobBehaviorSuite
{
    protected override Task<JobsHarness> CreateHostAsync(Action<JobsOptions>? configure = null, Action<IServiceCollection>? configureServices = null)
        => JobsHarness.StartInMemoryAsync(configure, configureServices);

    [Fact]
    public async Task an_offset_only_change_between_the_settles_read_and_its_write_is_caught()
    {
        // JOBS-0010: the in-memory store compares a guard's DateTimeOffset by its instant and its offset, so another
        // writer's change of offset alone — the same instant — misses the guard and is kept.
        await using var host = await CreateHostAsync();
        var at = new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
        var item = new TitledJob { Name = "first", At = at };
        await TitledJob.Upsert(item);

        var binding = host.Services.GetRequiredService<JobTypeRegistry>().Get(typeof(TitledJob).FullName!)!;
        var reads = 0;
        var racing = binding.WithLoad(async (id, ct) =>
        {
            var row = await binding.Load(id, ct);
            if (Interlocked.Increment(ref reads) == 1)
            {
                var other = await TitledJob.Get(id, ct);
                other!.At = at.ToOffset(TimeSpan.FromHours(2));
                await TitledJob.Upsert(other, ct);
            }
            return row;
        });

        var handled = (await TitledJob.Get(item.Id))!;
        var loaded = WorkItemImage.Capture(handled);
        handled.Name = "renamed";
        await new WorkItemWriter(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance)
            .Persist(racing, item.Id, handled, loaded, default);

        var stored = (await TitledJob.Get(item.Id))!;
        stored.Name.Should().Be("renamed");
        stored.At.Offset.Should().Be(TimeSpan.FromHours(2), "the other writer's offset stands");
    }
}
