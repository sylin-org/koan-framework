namespace Koan.Tests.Canon.Unit.Specs.Optimization;

public sealed class CanonOptimizationSpec
{
    private readonly ITestOutputHelper _output;

    public CanonOptimizationSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public Task Production_preset_prioritizes_throughput()
        => TestPipeline.For<CanonOptimizationSpec>(_output, nameof(Production_preset_prioritizes_throughput))
            .Act(ctx =>
            {
                var options = CanonOptimizationPresets.Production();

                options.Features.EnableBatchOperations.Should().BeTrue();
                options.Features.UseOptimizedProjectionWorker.Should().BeTrue();
                options.Performance.DefaultBatchSize.Should().Be(2000);
                options.Performance.MaxBatchSize.Should().Be(10000);
                options.Monitoring.ReportingInterval.Should().Be(TimeSpan.FromSeconds(15));

                return ValueTask.CompletedTask;
            })
            .RunAsync();

    [Fact]
    public Task Development_preset_remains_cautious()
        => TestPipeline.For<CanonOptimizationSpec>(_output, nameof(Development_preset_remains_cautious))
            .Act(ctx =>
            {
                var options = CanonOptimizationPresets.Development();

                options.Features.EnableBatchOperations.Should().BeTrue();
                options.Features.UseOptimizedProjectionWorker.Should().BeFalse();
                options.Performance.DefaultBatchSize.Should().Be(500);
                options.Performance.MaxBatchSize.Should().Be(2000);
                options.Monitoring.ReportingInterval.Should().Be(TimeSpan.FromSeconds(30));

                return ValueTask.CompletedTask;
            })
            .RunAsync();

    [Fact]
    public Task Adaptive_batch_processor_adjusts_within_bounds()
        => TestPipeline.For<CanonOptimizationSpec>(_output, nameof(Adaptive_batch_processor_adjusts_within_bounds))
            .Act(async ctx =>
            {
                using var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug));
                var options = CanonOptimizationPresets.Development();
                var processor = new AdaptiveBatchProcessor(loggerFactory.CreateLogger<AdaptiveBatchProcessor>(), options);

                var initial = await processor.GetOptimalBatchSize().ConfigureAwait(false);
                initial.Should().Be(options.Performance.DefaultBatchSize);

                await processor.RecordMetrics(TimeSpan.FromMilliseconds(200), 500, 50 * 1024 * 1024).ConfigureAwait(false);
                var increased = await processor.GetOptimalBatchSize().ConfigureAwait(false);
                increased.Should().BeInRange(100, options.Performance.MaxBatchSize);

                await processor.RecordMetrics(TimeSpan.FromSeconds(6), 100, 800 * 1024 * 1024).ConfigureAwait(false);
                var decreased = await processor.GetOptimalBatchSize().ConfigureAwait(false);
                decreased.Should().BeInRange(100, options.Performance.MaxBatchSize);
            })
            .RunAsync();

    [Fact]
    public Task Performance_monitor_records_and_reports()
        => TestPipeline.For<CanonOptimizationSpec>(_output, nameof(Performance_monitor_records_and_reports))
            .Act(async ctx =>
            {
                using var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug));
                var options = new CanonOptimizationOptions
                {
                    Monitoring = new CanonOptimizationOptions.MonitoringSettings
                    {
                        Enabled = true,
                        ReportingInterval = TimeSpan.FromMilliseconds(50)
                    }
                };

                await using var monitor = new CanonPerformanceMonitor(loggerFactory.CreateLogger<CanonPerformanceMonitor>(), options);
                monitor.Start(ctx.Cancellation);
                monitor.RecordMetrics("canonize", TimeSpan.FromMilliseconds(120), 250, 75 * 1024 * 1024);

                await Task.Delay(TimeSpan.FromMilliseconds(120), ctx.Cancellation).ConfigureAwait(false);
            })
            .RunAsync();
}
