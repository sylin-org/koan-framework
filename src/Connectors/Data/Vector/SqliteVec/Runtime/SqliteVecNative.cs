using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace Koan.Data.Vector.Connector.SqliteVec;

internal sealed class SqliteVecNative
{
    private readonly object _gate = new();
    private string? _path;
    private int _versionVerified;

    internal void Load(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        connection.EnableExtensions(true);
        try
        {
            connection.LoadExtension(Path(), Infrastructure.Constants.Native.EntryPoint);
            if (Volatile.Read(ref _versionVerified) == 0)
            {
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT vec_version()";
                var version = command.ExecuteScalar() as string;
                if (!string.Equals(version, Infrastructure.Constants.Native.ReportedVersion, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"SqliteVec loaded native version '{version ?? "unknown"}', but this connector requires " +
                        $"'{Infrastructure.Constants.Native.ReportedVersion}'. Replace the connector package with a matching build.");
                Volatile.Write(ref _versionVerified, 1);
            }
        }
        catch (Exception error) when (error is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"SqliteVec could not load its pinned native extension for this platform. Supported RIDs: " +
                $"{string.Join(", ", Infrastructure.Constants.Native.SupportedRids)}.", error);
        }
    }

    private string Path()
    {
        if (_path is not null) return _path;
        lock (_gate)
        {
            if (_path is not null) return _path;
            var native = ResolveNative();
            var assembly = typeof(SqliteVecNative).Assembly;
            using var stream = assembly.GetManifestResourceStream($"vec0.{native.Rid}")
                ?? throw new PlatformNotSupportedException(
                    $"SqliteVec has no embedded native payload for '{native.Rid}'. Supported RIDs: " +
                    $"{string.Join(", ", Infrastructure.Constants.Native.SupportedRids)}.");
            using var payload = new MemoryStream();
            stream.CopyTo(payload);
            var bytes = payload.ToArray();
            var expected = Convert.FromHexString(native.Hash);
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), expected))
                throw new InvalidOperationException(
                    $"The embedded sqlite-vec payload for '{native.Rid}' does not match the connector's pinned hash. Reinstall the package.");

            var directory = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "koan",
                "sqlite-vec",
                Infrastructure.Constants.Native.Version,
                native.Rid);
            Directory.CreateDirectory(directory);
            var path = System.IO.Path.Combine(directory, native.File);
            if (!Matches(path, expected)) Write(path, bytes, expected);
            _path = path;
            return path;
        }
    }

    private static void Write(string path, byte[] bytes, byte[] expected)
    {
        var temporary = path + $".{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, true);
        }
        catch (IOException) when (Matches(path, expected))
        {
            // Another host completed the same immutable extraction.
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static bool Matches(string path, byte[] expected)
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

    private static NativeAsset ResolveNative()
    {
        if (OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64)
            return new NativeAsset("win-x64", "vec0.dll", Infrastructure.Constants.Native.WindowsX64Hash);
        if (OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.X64)
            return new NativeAsset("linux-x64", "vec0.so", Infrastructure.Constants.Native.LinuxX64Hash);
        if (OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
            return new NativeAsset("linux-arm64", "vec0.so", Infrastructure.Constants.Native.LinuxArm64Hash);
        throw new PlatformNotSupportedException(
            $"SqliteVec does not ship a native payload for this platform. Supported RIDs: " +
            $"{string.Join(", ", Infrastructure.Constants.Native.SupportedRids)}.");
    }

    private sealed record NativeAsset(string Rid, string File, string Hash);
}
