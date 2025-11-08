using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Koan.Context.Services;

/// <summary>
/// Source URL generator - creates links to files in remote repositories (GitHub, GitLab, etc.)
/// </summary>
public sealed class UrlBuilder 
{
    private readonly ILogger<UrlBuilder> _logger;
    private readonly Dictionary<string, RemoteUrlInfo> _remoteCache = new();

    public UrlBuilder(ILogger<UrlBuilder> logger)
    {
        _logger = logger;
    }

    public async Task<string?> GenerateUrlAsync(
        string projectRootPath,
        string relativePath,
        string? commitSha = null,
        int? startLine = null,
        int? endLine = null,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(projectRootPath))
        {
            return null;
        }

        // Check cache
        if (_remoteCache.TryGetValue(projectRootPath, out var cached))
        {
            return BuildUrl(cached, relativePath, commitSha, startLine, endLine);
        }

        // Detect git remote
        var remoteInfo = await DetectGitRemoteAsync(projectRootPath, cancellationToken);
        if (remoteInfo == null)
        {
            return null;
        }

        _remoteCache[projectRootPath] = remoteInfo;
        return BuildUrl(remoteInfo, relativePath, commitSha, startLine, endLine);
    }

    private async Task<RemoteUrlInfo?> DetectGitRemoteAsync(
        string projectRootPath,
        CancellationToken cancellationToken)
    {
        try
        {
            var gitDir = Path.Combine(projectRootPath, ".git");
            if (!Directory.Exists(gitDir))
            {
                return null;
            }

            // Run: git remote get-url origin
            var processInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "remote get-url origin",
                WorkingDirectory = projectRootPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            if (process == null)
            {
                return null;
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                return null;
            }

            var remoteUrl = output.Trim();
            return ParseRemoteUrl(remoteUrl);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to detect git remote for {Path}", projectRootPath);
            return null;
        }
    }

    private RemoteUrlInfo? ParseRemoteUrl(string remoteUrl)
    {
        // GitHub HTTPS: https://github.com/owner/repo.git
        var githubHttpsMatch = Regex.Match(remoteUrl, @"https://github\.com/([^/]+)/(.+?)(\.git)?$");
        if (githubHttpsMatch.Success)
        {
            return new RemoteUrlInfo(
                Platform: "github",
                Owner: githubHttpsMatch.Groups[1].Value,
                Repo: githubHttpsMatch.Groups[2].Value.TrimEnd('/'),
                BaseUrl: "https://github.com");
        }

        // GitHub SSH: git@github.com:owner/repo.git
        var githubSshMatch = Regex.Match(remoteUrl, @"git@github\.com:([^/]+)/(.+?)(\.git)?$");
        if (githubSshMatch.Success)
        {
            return new RemoteUrlInfo(
                Platform: "github",
                Owner: githubSshMatch.Groups[1].Value,
                Repo: githubSshMatch.Groups[2].Value.TrimEnd('/'),
                BaseUrl: "https://github.com");
        }

        // GitLab HTTPS: https://gitlab.com/owner/repo.git
        var gitlabHttpsMatch = Regex.Match(remoteUrl, @"https://gitlab\.com/([^/]+)/(.+?)(\.git)?$");
        if (gitlabHttpsMatch.Success)
        {
            return new RemoteUrlInfo(
                Platform: "gitlab",
                Owner: gitlabHttpsMatch.Groups[1].Value,
                Repo: gitlabHttpsMatch.Groups[2].Value.TrimEnd('/'),
                BaseUrl: "https://gitlab.com");
        }

        // GitLab SSH: git@gitlab.com:owner/repo.git
        var gitlabSshMatch = Regex.Match(remoteUrl, @"git@gitlab\.com:([^/]+)/(.+?)(\.git)?$");
        if (gitlabSshMatch.Success)
        {
            return new RemoteUrlInfo(
                Platform: "gitlab",
                Owner: gitlabSshMatch.Groups[1].Value,
                Repo: gitlabSshMatch.Groups[2].Value.TrimEnd('/'),
                BaseUrl: "https://gitlab.com");
        }

        return null;
    }

    private string? BuildUrl(
        RemoteUrlInfo info,
        string relativePath,
        string? commitSha,
        int? startLine,
        int? endLine)
    {
        var normalizedPath = relativePath.Replace('\\', '/').TrimStart('/');
        var commit = commitSha ?? "HEAD";

        var url = info.Platform switch
        {
            "github" => BuildGitHubUrl(info, normalizedPath, commit, startLine, endLine),
            "gitlab" => BuildGitLabUrl(info, normalizedPath, commit, startLine, endLine),
            _ => null
        };

        return url;
    }

    private string BuildGitHubUrl(
        RemoteUrlInfo info,
        string path,
        string commit,
        int? startLine,
        int? endLine)
    {
        // https://github.com/owner/repo/blob/commit/path#L1-L10
        var url = $"{info.BaseUrl}/{info.Owner}/{info.Repo}/blob/{commit}/{path}";

        if (startLine.HasValue)
        {
            if (endLine.HasValue && endLine.Value != startLine.Value)
            {
                url += $"#L{startLine.Value}-L{endLine.Value}";
            }
            else
            {
                url += $"#L{startLine.Value}";
            }
        }

        return url;
    }

    private string BuildGitLabUrl(
        RemoteUrlInfo info,
        string path,
        string commit,
        int? startLine,
        int? endLine)
    {
        // https://gitlab.com/owner/repo/-/blob/commit/path#L1-10
        var url = $"{info.BaseUrl}/{info.Owner}/{info.Repo}/-/blob/{commit}/{path}";

        if (startLine.HasValue)
        {
            if (endLine.HasValue && endLine.Value != startLine.Value)
            {
                url += $"#L{startLine.Value}-{endLine.Value}";
            }
            else
            {
                url += $"#L{startLine.Value}";
            }
        }

        return url;
    }
}

/// <summary>
/// Parsed git remote information
/// </summary>
internal record RemoteUrlInfo(
    string Platform,
    string Owner,
    string Repo,
    string BaseUrl);
