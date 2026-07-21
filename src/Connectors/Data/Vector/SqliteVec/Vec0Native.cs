using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace Koan.Data.Vector.Connector.SqliteVec;

/// <summary>
/// Resolves and loads the embedded sqlite-vec <c>vec0</c> native extension. The binary for the current
/// floor RID is carried as an assembly resource and self-extracted to a per-version temp dir on first use,
/// so it travels as an assembly resource and needs no NuGet RID-asset plumbing. Floor RIDs only:
/// win-x64, linux-x64, linux-arm64 — anything else fails loud.
/// </summary>
internal static class Vec0Native
{
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
            using var payload = new MemoryStream();
            stream.CopyTo(payload);
            var bytes = payload.ToArray();
            var expectedHash = SHA256.HashData(bytes);

            var dir = Path.Combine(
                Path.GetTempPath(),
                $"koan-sqlite-vec-{Infrastructure.Constants.Native.Version}",
                rid);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, file);
            if (!HasHash(path, expectedHash))
            {
                var temporary = Path.Combine(dir, $".{file}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");
                try
                {
                    File.WriteAllBytes(temporary, bytes);
                    File.Move(temporary, path, overwrite: true);
                }
                catch (IOException) when (HasHash(path, expectedHash))
                {
                    // Another process completed the same versioned extraction first.
                }
                finally
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
            }
            _extracted = path;
            return path;
        }
    }

    private static bool HasHash(string path, byte[] expected)
    {
        if (!File.Exists(path)) return false;
        try
        {
            using var file = File.OpenRead(path);
            return CryptographicOperations.FixedTimeEquals(SHA256.HashData(file), expected);
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static (string Rid, string File) Resolve()
    {
        var arm = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
        if (OperatingSystem.IsWindows() && !arm) return ("win-x64", "vec0.dll");
        if (OperatingSystem.IsLinux()) return (arm ? "linux-arm64" : "linux-x64", "vec0.so");
        throw new PlatformNotSupportedException(
            $"sqlite-vec native binary is not bundled for this platform. Supported RIDs: {string.Join(", ", Infrastructure.Constants.Native.SupportedRids)}.");
    }
}
