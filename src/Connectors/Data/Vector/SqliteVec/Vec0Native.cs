using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;

namespace Koan.Data.Vector.Connector.SqliteVec;

/// <summary>
/// Resolves and loads the embedded sqlite-vec <c>vec0</c> native extension. The binary for the current
/// floor RID is carried as an assembly resource and self-extracted to a per-version temp dir on first use,
/// so it travels inside a single-file / NativeAOT publish and needs no NuGet RID-asset plumbing (the exact
/// fragility the survey flagged). Floor RIDs only: win-x64, linux-x64, linux-arm64 — anything else fails loud.
/// </summary>
internal static class Vec0Native
{
    private const string Version = "0.1.9";
    private static readonly object Gate = new();
    private static string? _extracted;

    public static void Load(SqliteConnection connection)
    {
        connection.EnableExtensions(true);
        var path = ExtractedPath();
        // sqlite-vec's C entry point is sqlite3_vec_init; the filename-derived default (sqlite3_vec0_init)
        // would not resolve, so pass it explicitly. Fall back to the default for any future renamed build.
        try { connection.LoadExtension(path, "sqlite3_vec_init"); }
        catch (SqliteException) { connection.LoadExtension(path); }
    }

    private static string ExtractedPath()
    {
        if (_extracted is not null) return _extracted;
        lock (Gate)
        {
            if (_extracted is not null) return _extracted;
            var (rid, file) = Resolve();
            var asm = typeof(Vec0Native).Assembly;
            using var stream = asm.GetManifestResourceStream($"vec0.{rid}")
                ?? throw new PlatformNotSupportedException(
                    $"sqlite-vec native binary is not bundled for RID '{rid}'. Floor RIDs: win-x64, linux-x64, linux-arm64.");
            var dir = Path.Combine(Path.GetTempPath(), $"koan-sqlite-vec-{Version}", rid);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, file);
            if (!File.Exists(path) || new FileInfo(path).Length != stream.Length)
            {
                using var fs = File.Create(path);
                stream.CopyTo(fs);
            }
            _extracted = path;
            return path;
        }
    }

    private static (string Rid, string File) Resolve()
    {
        var arm = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
        if (OperatingSystem.IsWindows()) return (arm ? "win-arm64" : "win-x64", "vec0.dll");
        if (OperatingSystem.IsLinux()) return (arm ? "linux-arm64" : "linux-x64", "vec0.so");
        if (OperatingSystem.IsMacOS()) return (arm ? "osx-arm64" : "osx-x64", "vec0.dylib");
        throw new PlatformNotSupportedException("Unsupported OS for sqlite-vec.");
    }
}
