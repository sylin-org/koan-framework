using System.Buffers.Binary;
using System.Text;
using Koan.Core.Hosting.App;
using Koan.Web.Infrastructure;
using Koan.Web.Options;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Infrastructure;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Koan.Web.Hosting;

/// <summary>
/// Where the ASP.NET Data Protection key ring lives, decided once from <c>Koan:Web:KeyRingDirectory</c> (WEB-0079).
/// </summary>
/// <remarks>
/// <para>Every cookie, every protected authentication state and every protected session the host writes is
/// encrypted with that ring. Left to ASP.NET, the ring lives in the process's user profile or in memory, and the
/// application's name is derived from its content root, so a rebuilt container, or a second instance, cannot read
/// what the first one protected. When the application names a folder, the ring persists there under the application
/// name the folder remembers: the first start over a folder writes the name it uses (<c>Koan:Web:KeyRingApplicationName</c>
/// when pinned, else <see cref="ApplicationIdentitySnapshot.Code"/>) into <see cref="KoanWebConstants.KeyRing.ApplicationNameFile"/>,
/// and every later start reads it back, so renaming the application never orphans its keys.</para>
/// <para>Koan supplies defaults the application's own Data Protection configuration overrides: the folder is the
/// key repository only when the application configured none, and the name is the default application discriminator
/// ASP.NET asks <see cref="IApplicationDiscriminator"/> for, which <c>SetApplicationName</c> replaces. What is in
/// effect is read back from the resolved options, so <see cref="Directory"/>, <see cref="ApplicationName"/> and the
/// composition fact say where the ring really is.</para>
/// <para>The folder is resolved against the content root when it is relative, created at start (owner-only on Unix),
/// proved writable at start, and the ring is loaded at start, so a folder this process cannot use refuses the start
/// with a correction naming the key instead of failing the first sign-in. When the key is unset nothing changes.</para>
/// </remarks>
public sealed class KoanWebKeyRing : IConfigureOptions<KeyManagementOptions>, IApplicationDiscriminator
{
    internal const string Key = $"{ConfigurationConstants.Web.Section}:{ConfigurationConstants.Web.Keys.KeyRingDirectory}";
    internal const string ApplicationNameKey = $"{ConfigurationConstants.Web.Section}:{ConfigurationConstants.Web.Keys.KeyRingApplicationName}";
    private const string WarmPurpose = "Koan.Web.KeyRing.Start";
    private const string NameTerminator = "\n";
    private const int NameReadAttempts = 20;
    private const string KeyElement = "key";
    private const string KeyIdAttribute = "id";
    private static readonly TimeSpan NameReadPause = TimeSpan.FromMilliseconds(25);
    private static readonly UTF8Encoding NameEncoding = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly byte[] EffectProbe = [0];
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode GroupOrOther = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

    private readonly IServiceProvider _services;
    private readonly IApplicationDiscriminator? _hostDiscriminator;
    private readonly ILoggerFactory _loggers;
    private readonly string _applicationCode;
    private readonly string? _pinned;
    private readonly Lazy<Folder> _folder;
    private readonly Lazy<Effect> _effect;

    internal KoanWebKeyRing(
        IServiceProvider services,
        IOptions<KoanWebOptions> options,
        string applicationCode,
        IHostEnvironment? environment = null,
        ILoggerFactory? loggers = null,
        IApplicationDiscriminator? hostDiscriminator = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationCode);
        _services = services;
        _hostDiscriminator = hostDiscriminator;
        _loggers = loggers ?? NullLoggerFactory.Instance;
        _applicationCode = applicationCode;
        _folder = new Lazy<Folder>(PrepareFolder, LazyThreadSafetyMode.ExecutionAndPublication);
        _effect = new Lazy<Effect>(ReadEffect, LazyThreadSafetyMode.ExecutionAndPublication);
        var configured = options.Value.KeyRingDirectory;
        if (string.IsNullOrWhiteSpace(configured)) return;
        Configured = configured;
        ConfiguredDirectory = Path.GetFullPath(configured, environment?.ContentRootPath ?? AppContext.BaseDirectory);
        _pinned = string.IsNullOrWhiteSpace(options.Value.KeyRingApplicationName) ? null : options.Value.KeyRingApplicationName.Trim();
    }

    /// <summary>The configured <c>Koan:Web:KeyRingDirectory</c>, or null when the application names no folder.</summary>
    public string? Configured { get; }

    /// <summary>The configured folder resolved against the content root, or null when none is named.</summary>
    public string? ConfiguredDirectory { get; }

    /// <summary>True when <c>Koan:Web:KeyRingDirectory</c> is set.</summary>
    public bool IsConfigured => ConfiguredDirectory is not null;

    /// <summary>
    /// The folder the ring persists in, as in effect: Koan's folder, the application's own file-system repository's,
    /// or null when the ring is in no folder this host configured (ASP.NET's default, or another kind of repository).
    /// </summary>
    public string? Directory => _effect.Value.Directory;

    /// <summary>The application name the ring is scoped to, as in effect, so instances over one folder share the keys.</summary>
    public string? ApplicationName => _effect.Value.ApplicationName;

    /// <summary>
    /// True when the ring in force persists in a repository this host configured, Koan's folder or the application's own:
    /// the provider in force protects with a key that repository holds.
    /// </summary>
    public bool IsPersisted => _effect.Value.Persists;

    /// <summary>
    /// True when the application's own Data Protection configuration replaced Koan's folder or name, or replaced the
    /// provider so that no key the host uses is in the configured repository.
    /// </summary>
    public bool IsOverridden => _effect.Value.RepositoryOverridden || _effect.Value.NameOverridden || _effect.Value.ProviderOverridden;

    string? IApplicationDiscriminator.Discriminator
        => ConfiguredDirectory is null ? _hostDiscriminator?.Discriminator : _folder.Value.ApplicationName;

    void IConfigureOptions<KeyManagementOptions>.Configure(KeyManagementOptions options)
    {
        // The application's own repository, configured before Koan's hook runs, wins; one configured after replaces this.
        if (ConfiguredDirectory is null || options.XmlRepository is not null) return;
        _ = _folder.Value;
        options.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(ConfiguredDirectory), _loggers);
    }

    /// <summary>
    /// Create the folder, prove it writable, settle the application name it remembers, and prove the provider in force
    /// can protect a value; when Koan's ring is the one in force, that writes the first key into the folder. Nothing
    /// happens when no folder is named.
    /// </summary>
    /// <exception cref="InvalidOperationException">The folder cannot be created, written or used for the ring, or a
    /// pinned application name differs from the one the folder holds.</exception>
    public void Prepare()
    {
        if (ConfiguredDirectory is null) return;
        _ = _folder.Value;
        try { _services.GetRequiredService<IDataProtectionProvider>().CreateProtector(WarmPurpose).Protect("ready"); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw Refusal("cannot hold the ring", exception);
        }
    }

    /// <summary>The composition fact's summary: where the ring lives, and whose configuration put it there.</summary>
    internal string Statement
    {
        get
        {
            var effect = _effect.Value;
            var where = effect.Directory ?? $"a {effect.Repository}";
            if (ConfiguredDirectory is null)
            {
                if (effect.ProviderOverridden)
                    return $"The Data Protection key ring is not persisted by Koan ({Key} is unset); the application's own Data Protection " +
                           $"provider ({effect.Repository}) is in effect, and no key it uses is in its configured key repository.";
                return effect.Persists
                    ? $"The Data Protection key ring is not persisted by Koan ({Key} is unset); the application's own key repository holds it in " +
                      $"{where} under the application name '{effect.ApplicationName}'."
                    : $"The Data Protection key ring is not persisted by Koan ({Key} is unset); it lives where ASP.NET keeps it by default, " +
                      "which a rebuilt container does not keep.";
            }

            if (effect.ProviderOverridden)
                return $"{Key} names {ConfiguredDirectory}, but the application's own Data Protection provider ({effect.Repository}) is in " +
                       "effect, so no key it uses persists in that folder.";

            var folder = _folder.Value;
            var mode = folder.WiderMode is { } wider
                ? $" The folder's mode is {Octal(wider)}, wider than owner-only ({Octal(OwnerOnly)}), and the keys in it are written in clear; " +
                  "restrict it to the process's user."
                : "";
            if (!effect.RepositoryOverridden && !effect.NameOverridden)
            {
                var source = _pinned is null ? "which the folder remembers" : $"pinned by {ApplicationNameKey}";
                return $"The Data Protection key ring persists in {effect.Directory} ({Key}) under the application name " +
                       $"'{effect.ApplicationName}', {source}; every instance over that folder shares it.{mode}";
            }

            var repository = effect.RepositoryOverridden ? $"the application's own key repository, {where}" : effect.Directory;
            var name = effect.NameOverridden
                ? $"the application's own application name '{effect.ApplicationName}'"
                : $"the application name '{effect.ApplicationName}'";
            return $"{Key} names {ConfiguredDirectory}, but the application's own Data Protection configuration wins: the key ring " +
                   $"persists in {repository} under {name}.{mode}";
        }
    }

    /// <summary>The composition fact's reason code.</summary>
    internal string FactReason => ConfiguredDirectory is not null && !IsOverridden
        ? KoanWebConstants.KeyRing.PersistedFactReason
        : IsPersisted || IsOverridden ? KoanWebConstants.KeyRing.ApplicationFactReason : KoanWebConstants.KeyRing.DefaultFactReason;

    private Folder PrepareFolder()
    {
        var directory = ConfiguredDirectory!;
        var existed = System.IO.Directory.Exists(directory);
        try
        {
            if (OperatingSystem.IsWindows()) System.IO.Directory.CreateDirectory(directory);
            else System.IO.Directory.CreateDirectory(directory, OwnerOnly);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw Refusal("could not be created", exception);
        }

        UnixFileMode? wider = null;
        if (existed && !OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(directory);
            if ((mode & GroupOrOther) != 0) wider = mode;
        }

        // The probe's name is unique to the attempt, so instances starting together over one folder never collide.
        var probe = Path.Combine(directory, KoanWebConstants.KeyRing.ProbePrefix + Guid.NewGuid().ToString("N"));
        try
        {
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw Refusal("cannot be written", exception);
        }
        Discard(probe);

        return new Folder(Remember(directory), wider);
    }

    // The folder remembers whose keys it holds: the first start writes the name it uses, every later start reads it.
    // Creating the file is exclusive on every platform, so of instances starting together over an empty folder exactly
    // one writes its name and every other reads that one.
    private string Remember(string directory)
    {
        var file = Path.Combine(directory, KoanWebConstants.KeyRing.ApplicationNameFile);
        var remembered = File.Exists(file) ? ReadSettled(file) : null;
        if (remembered is null)
        {
            FileStream? created;
            try { created = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.Read | FileShare.Delete); }
            catch (IOException) when (File.Exists(file)) { created = null; }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw Refusal("cannot be written", exception);
            }

            if (created is null)
            {
                remembered = ReadSettled(file);
            }
            else
            {
                var name = _pinned ?? _applicationCode;
                using (created)
                {
                    try
                    {
                        created.Write(NameEncoding.GetBytes(name + NameTerminator));
                        created.Flush(flushToDisk: true);
                    }
                    catch (IOException exception)
                    {
                        throw Refusal("cannot be written", exception);
                    }
                }
                remembered = name;
            }
        }

        if (_pinned is not null && !string.Equals(_pinned, remembered, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{ApplicationNameKey} is '{_pinned}', but the key ring folder '{directory}' ({Key}) holds the keys of the application " +
                $"name '{remembered}' ({KoanWebConstants.KeyRing.ApplicationNameFile}); under '{_pinned}' every key in it would be unreadable. " +
                $"Set {ApplicationNameKey} to '{remembered}' or unset it, or point {Key} at a folder of its own.");
        }
        return remembered;
    }

    // The name is one line ending in a newline. A file another instance is still writing is empty, unterminated or
    // briefly unreadable, so the read shares everything and waits a few short pauses for the terminator; after that it
    // takes what is there (a name written by hand without a newline), and an empty file refuses the start.
    private string ReadSettled(string file)
    {
        for (var attempt = 1; ; attempt++)
        {
            string text;
            try
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, NameEncoding);
                text = reader.ReadToEnd();
            }
            catch (IOException) when (attempt < NameReadAttempts)
            {
                Thread.Sleep(NameReadPause);
                continue;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw Refusal("cannot be read", exception);
            }

            if (!text.EndsWith(NameTerminator, StringComparison.Ordinal) && attempt < NameReadAttempts)
            {
                Thread.Sleep(NameReadPause);
                continue;
            }

            var name = text.Trim();
            return name.Length > 0
                ? name
                : throw Refusal("cannot be used", new InvalidDataException(
                    $"{KoanWebConstants.KeyRing.ApplicationNameFile} is empty; it names the application whose keys the folder holds."));
        }
    }

    // What is in force, read back from the resolved services: the repository the options name holds the ring only when
    // the provider in force protects with a key that repository holds (an ephemeral or replaced provider does not).
    private Effect ReadEffect()
    {
        var repository = _services.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlRepository;
        var name = _services.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator;
        var provider = _services.GetRequiredService<IDataProtectionProvider>();
        var persists = repository is not null && ProtectsWithKeyIn(provider, repository);
        var providerOverridden = repository is not null && !persists;
        var directory = persists ? (repository as FileSystemXmlRepository)?.Directory.FullName : null;
        var holder = persists ? repository!.GetType().Name : providerOverridden ? provider.GetType().Name : null;
        if (ConfiguredDirectory is null || !persists)
            return new Effect(directory, holder, persists ? name : null, persists, false, false, providerOverridden);

        return new Effect(
            directory,
            holder,
            name,
            Persists: true,
            RepositoryOverridden: directory is null || !SamePath(directory, ConfiguredDirectory),
            NameOverridden: !string.Equals(name, _folder.Value.ApplicationName, StringComparison.Ordinal),
            ProviderOverridden: false);
    }

    // ASP.NET's protected payload starts with its magic header and the id of the key that protected it.
    private static bool ProtectsWithKeyIn(IDataProtectionProvider provider, IXmlRepository repository)
    {
        var payload = provider.CreateProtector(WarmPurpose).Protect(EffectProbe);
        if (payload.Length < 20 || BinaryPrimitives.ReadUInt32BigEndian(payload) != KoanWebConstants.KeyRing.PayloadMagicHeader) return false;
        var key = new Guid(payload.AsSpan(4, 16));
        return repository.GetAllElements().Any(element =>
            element.Name.LocalName == KeyElement && Guid.TryParse((string?)element.Attribute(KeyIdAttribute), out var id) && id == key);
    }

    private static bool SamePath(string left, string right) => string.Equals(
        Path.TrimEndingDirectorySeparator(left),
        Path.TrimEndingDirectorySeparator(right),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static void Discard(string file)
    {
        // A file left behind is harmless; another instance's cleanup or a scanner holding it must not refuse the start.
        try { File.Delete(file); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private static string Octal(UnixFileMode mode) => Convert.ToString((int)mode & 0x1FF, 8).PadLeft(4, '0');

    private InvalidOperationException Refusal(string problem, Exception cause) => new(
        $"{Key} is '{Configured}', resolved to '{ConfiguredDirectory}', but the folder {problem}: {cause.Message} " +
        "Point it at a folder this process may create and write, such as a persistent volume mounted in the container, " +
        "or unset it to keep the key ring where ASP.NET Data Protection puts it by default.", cause);

    private sealed record Folder(string ApplicationName, UnixFileMode? WiderMode);

    private sealed record Effect(
        string? Directory,
        string? Repository,
        string? ApplicationName,
        bool Persists,
        bool RepositoryOverridden,
        bool NameOverridden,
        bool ProviderOverridden);
}
