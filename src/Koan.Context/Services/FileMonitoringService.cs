using System.Collections.Concurrent;
using Koan.Context.Models;
using Koan.Context.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Context.Services;

/// <summary>
/// Hosted service that monitors project directories for file changes
/// </summary>
public class FileMonitoringService : IHostedService, IDisposable
{
    private readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers = new();
    private readonly ConcurrentDictionary<string, DebouncingQueue> _debounceQueues = new();
    private readonly ILogger<FileMonitoringService> _logger;
    private readonly FileMonitoringOptions _options;
    private readonly IServiceProvider _serviceProvider;

    public FileMonitoringService(
        ILogger<FileMonitoringService> logger,
        IOptions<FileMonitoringOptions> options,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _options = options.Value;
        _serviceProvider = serviceProvider;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("File monitoring is disabled");
            return;
        }

        // Load all active projects and start watching
        var projects = await Project.Query(p => p.IsActive, cancellationToken);

        foreach (var project in projects.Where(p => p.IsMonitoringEnabled))
        {
            await StartWatchingProjectAsync(project, cancellationToken);
        }

        _logger.LogInformation("Started monitoring {Count} projects", _watchers.Count);
    }

    public async Task StartWatchingProjectAsync(Project project, CancellationToken cancellationToken = default)
    {
        if (_watchers.ContainsKey(project.Id))
        {
            _logger.LogWarning("Already watching project {Name}", project.Name);
            return;
        }

        if (!Directory.Exists(project.RootPath))
        {
            _logger.LogWarning("Project root path does not exist: {Path}", project.RootPath);
            return;
        }

        try
        {
            var watcher = new FileSystemWatcher(project.RootPath)
            {
                NotifyFilter = NotifyFilters.FileName
                             | NotifyFilters.LastWrite
                             | NotifyFilters.CreationTime,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };

            // Set up event handlers
            watcher.Changed += (s, e) => OnFileChanged(project, e);
            watcher.Created += (s, e) => OnFileChanged(project, e);
            watcher.Deleted += (s, e) => OnFileDeleted(project, e);
            watcher.Renamed += (s, e) => OnFileRenamed(project, e);
            watcher.Error += (s, e) => OnWatcherError(project, e);

            _watchers[project.Id] = watcher;
            _debounceQueues[project.RootPath] = new DebouncingQueue(
                _options.DebounceMilliseconds,
                async changes => await ProcessChangesAsync(project, changes));

            _logger.LogInformation("Started watching project: {Name} at {Path}",
                project.Name, project.RootPath);

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start watching project {Name}", project.Name);
        }
    }

    private void OnFileChanged(Project project, FileSystemEventArgs e)
    {
        // Filter by project monitoring settings
        var relativePath = Path.GetRelativePath(project.RootPath, e.FullPath);

        // Early exit: Exclude system directories and temp files
        if (ShouldExcludeFromMonitoring(relativePath))
        {
            return;
        }

        var category = PathCategorizer.DeriveCategory(relativePath);

        bool shouldIndex = category switch
        {
            "source" or "test" => project.MonitorCodeChanges,
            "decision" or "guide" or "api-doc" or "documentation" => project.MonitorDocChanges,
            _ => project.MonitorCodeChanges  // Default to code
        };

        if (!shouldIndex)
        {
            _logger.LogTrace("Skipping {Path} - monitoring disabled for {Category}",
                e.FullPath, category);
            return;
        }

        // Check against .gitignore
        var gitignore = GitignoreParser.LoadFromDirectory(project.RootPath);

        if (gitignore.ShouldExclude(relativePath))
        {
            _logger.LogTrace("Skipping {Path} - excluded by .gitignore", relativePath);
            return;
        }

        // Add to debouncing queue
        if (_debounceQueues.TryGetValue(project.RootPath, out var queue))
        {
            queue.Enqueue(new FileChange
            {
                Path = e.FullPath,
                Type = FileChangeType.Modified
            });
        }
    }

    private void OnFileDeleted(Project project, FileSystemEventArgs e)
    {
        var relativePath = Path.GetRelativePath(project.RootPath, e.FullPath);

        // Early exit: Exclude system directories and temp files
        if (ShouldExcludeFromMonitoring(relativePath))
        {
            return;
        }

        if (_debounceQueues.TryGetValue(project.RootPath, out var queue))
        {
            queue.Enqueue(new FileChange
            {
                Path = e.FullPath,
                Type = FileChangeType.Deleted
            });
        }
    }

    private void OnFileRenamed(Project project, RenamedEventArgs e)
    {
        var relativePath = Path.GetRelativePath(project.RootPath, e.FullPath);

        // Early exit: Exclude system directories and temp files
        if (ShouldExcludeFromMonitoring(relativePath))
        {
            return;
        }

        // Treat rename as delete old + create new
        if (_debounceQueues.TryGetValue(project.RootPath, out var queue))
        {
            queue.Enqueue(new FileChange
            {
                Path = e.OldFullPath,
                Type = FileChangeType.Deleted
            });

            queue.Enqueue(new FileChange
            {
                Path = e.FullPath,
                Type = FileChangeType.Modified
            });
        }
    }

    private void OnWatcherError(Project project, ErrorEventArgs e)
    {
        _logger.LogError(e.GetException(), "File watcher error for project {Name}", project.Name);

        if (_options.RestartOnFailure)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(_options.RestartDelayMilliseconds);
                await StopWatchingProjectAsync(project.Id);
                await StartWatchingProjectAsync(project);
            });
        }
    }

    /// <summary>
    /// Determines if a file path should be excluded from monitoring
    /// </summary>
    /// <param name="relativePath">Relative path from project root</param>
    /// <returns>True if file should be excluded</returns>
    private bool ShouldExcludeFromMonitoring(string relativePath)
    {
        // Excluded system directories (same as DocumentDiscoveryService)
        var excludedDirectories = new[]
        {
            ".git", "node_modules", "bin", "obj", ".vs", ".vscode",
            "dist", "build", "target", "coverage", ".next", ".nuxt",
            "packages", "vendor", "__pycache__", ".pytest_cache"
        };

        // Check if path contains any excluded directory
        var pathParts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        foreach (var part in pathParts)
        {
            if (excludedDirectories.Contains(part, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // Exclude common temp file patterns
        var fileName = Path.GetFileName(relativePath);
        if (fileName.StartsWith("~") ||      // Office temp files
            fileName.StartsWith(".#") ||      // Emacs temp files
            fileName.EndsWith(".tmp") ||      // Generic temp files
            fileName.EndsWith(".swp"))        // Vim swap files
        {
            return true;
        }

        return false;
    }

    private async Task ProcessChangesAsync(Project project, List<FileChange> changes)
    {
        using var scope = _serviceProvider.CreateScope();
        var incrementalIndexer = scope.ServiceProvider
            .GetRequiredService<IncrementalIndexingService>();

        _logger.LogInformation("Processing {Count} file changes for project {Name}",
            changes.Count, project.Name);

        await incrementalIndexer.ProcessFileChangesAsync(project.Id, changes);
    }

    public async Task StopWatchingProjectAsync(string projectId)
    {
        if (_watchers.TryRemove(projectId, out var watcher))
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();

            _logger.LogInformation("Stopped watching project {ProjectId}", projectId);
        }

        await Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var watcher in _watchers.Values)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        _watchers.Clear();
        _logger.LogInformation("Stopped all file monitoring");
        await Task.CompletedTask;
    }

    public void Dispose()
    {
        foreach (var watcher in _watchers.Values)
        {
            watcher?.Dispose();
        }
        _watchers.Clear();
    }
}

/// <summary>
/// Configuration options for file monitoring
/// </summary>
public class FileMonitoringOptions
{
    public bool Enabled { get; set; } = true;
    public int DebounceMilliseconds { get; set; } = 2000;
    public int BatchWindowMilliseconds { get; set; } = 5000;
    public int MaxConcurrentReindexOperations { get; set; } = 3;
    public int MaxConcurrentFileWatchers { get; set; } = 10;
    public bool RestartOnFailure { get; set; } = true;
    public int RestartDelayMilliseconds { get; set; } = 5000;
}
