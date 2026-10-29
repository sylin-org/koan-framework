using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using AwesomeAssertions;
using Koan.Core.Diagnostics;
using Koan.Web.Hosting;
using Koan.Web.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Koan.Web.WellKnown.Tests.BaseUrlPathBaseSpec;

namespace Koan.Web.WellKnown.Tests;

/// <summary>
/// WEB-0079: <c>Koan:Web:KeyRingDirectory</c> names the folder the ASP.NET Data Protection key ring persists in. Keys
/// appear there at start, a value one host protected is read by a second host over the same folder even after the
/// application is renamed, the folder remembers the application name and refuses a conflicting pin, the
/// application's own Data Protection configuration wins and the facts say so, a folder that cannot be created,
/// written or used refuses the start with a correction, the folder is owner-only on Unix, and when the key is unset
/// no folder is touched.
/// </summary>
public sealed class KeyRingSpec
{
    private const string Key = "Koan:Web:KeyRingDirectory";
    private const string PinKey = "Koan:Web:KeyRingApplicationName";
    private const string NameKey = "Koan:Application:Name";

    [Fact]
    public async Task Keys_persist_in_the_named_folder_and_a_second_host_over_it_reads_what_the_first_protected()
    {
        using var folder = new TempFolder();
        string protectedValue;
        await using (var first = await BaseUrlHost.Start(new Dictionary<string, string?> { [Key] = folder.Path }))
        {
            Directory.GetFiles(folder.Path, "key-*.xml").Should().NotBeEmpty("the ring is loaded at start, so its first key is written then");
            Directory.GetFiles(folder.Path, KoanWebConstants.KeyRing.ProbePrefix + "*").Should().BeEmpty("the write probe is removed");

            var keyRing = first.Services.GetRequiredService<KoanWebKeyRing>();
            keyRing.IsPersisted.Should().BeTrue();
            keyRing.IsOverridden.Should().BeFalse();
            keyRing.Directory.Should().Be(folder.Path);

            var fact = Fact(first);
            fact.Subject.Should().Be(KoanWebConstants.KeyRing.FactSubject);
            fact.ReasonCode.Should().Be(KoanWebConstants.KeyRing.PersistedFactReason);
            fact.Summary.Should().Contain(folder.Path).And.Contain(Key).And.Contain(keyRing.ApplicationName).And.Contain("remembers");

            protectedValue = first.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("spec").Protect("survives");
        }

        await using var second = await BaseUrlHost.Start(new Dictionary<string, string?> { [Key] = folder.Path });
        second.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("spec").Unprotect(protectedValue)
            .Should().Be("survives", "the second host shares the first host's keys and application name");
    }

    [Fact]
    public async Task Renaming_the_application_keeps_the_name_the_folder_remembers()
    {
        using var folder = new TempFolder();
        string protectedValue;
        await using (var first = await BaseUrlHost.Start(new Dictionary<string, string?> { [Key] = folder.Path, [NameKey] = "Alpha Site" }))
        {
            first.Services.GetRequiredService<KoanWebKeyRing>().ApplicationName.Should().Be("alpha-site");
            protectedValue = first.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("spec").Protect("survives");
        }
        (await File.ReadAllTextAsync(Path.Combine(folder.Path, KoanWebConstants.KeyRing.ApplicationNameFile), TestContext.Current.CancellationToken))
            .Should().Be("alpha-site\n", "the name is one line ending in a newline");

        await using var renamed = await BaseUrlHost.Start(new Dictionary<string, string?> { [Key] = folder.Path, [NameKey] = "Beta Site" });
        renamed.Services.GetRequiredService<KoanWebKeyRing>().ApplicationName.Should().Be("alpha-site", "the folder remembers whose keys it holds");
        renamed.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("spec").Unprotect(protectedValue)
            .Should().Be("survives", "a rename does not orphan the keys");
    }

    [Fact]
    public async Task A_pinned_name_is_remembered_and_a_conflicting_pin_refuses_the_start_naming_both()
    {
        using var folder = new TempFolder();
        await using (var pinned = await BaseUrlHost.Start(new Dictionary<string, string?> { [Key] = folder.Path, [PinKey] = "pinned-one" }))
        {
            var keyRing = pinned.Services.GetRequiredService<KoanWebKeyRing>();
            keyRing.ApplicationName.Should().Be("pinned-one");
            Fact(pinned).Summary.Should().Contain(PinKey);
        }

        await using (var unpinned = await BaseUrlHost.Start(new Dictionary<string, string?> { [Key] = folder.Path, [NameKey] = "Other Site" }))
        {
            unpinned.Services.GetRequiredService<KoanWebKeyRing>().ApplicationName.Should().Be("pinned-one");
        }

        var start = () => BaseUrlHost.Start(new Dictionary<string, string?> { [Key] = folder.Path, [PinKey] = "pinned-two" });
        var failure = await start.Should().ThrowAsync<Exception>();
        Messages(failure.Which).Should().Contain(message =>
            message.Contains(PinKey) && message.Contains("'pinned-two'") && message.Contains("'pinned-one'") && message.Contains("unreadable"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_applications_own_data_protection_configuration_wins_and_the_fact_says_so(bool beforeKoan)
    {
        using var koanFolder = new TempFolder();
        using var applicationFolder = new TempFolder();
        Action<IServiceCollection> own = services => services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(applicationFolder.Path))
            .SetApplicationName("application-own");

        await using var host = await BaseUrlHost.Start(
            new Dictionary<string, string?> { [Key] = koanFolder.Path },
            configure: beforeKoan ? null : own,
            beforeKoan: beforeKoan ? own : null);

        Directory.GetFiles(applicationFolder.Path, "key-*.xml").Should().NotBeEmpty("the application's repository holds the ring");
        Directory.GetFiles(koanFolder.Path, "key-*.xml").Should().BeEmpty();

        var keyRing = host.Services.GetRequiredService<KoanWebKeyRing>();
        keyRing.IsOverridden.Should().BeTrue();
        keyRing.Directory.Should().Be(applicationFolder.Path);
        keyRing.ApplicationName.Should().Be("application-own");

        var fact = Fact(host);
        fact.ReasonCode.Should().Be(KoanWebConstants.KeyRing.ApplicationFactReason);
        fact.Summary.Should().Contain(koanFolder.Path).And.Contain(applicationFolder.Path)
            .And.Contain("'application-own'").And.Contain("application's own");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_ephemeral_provider_is_reported_as_the_applications_and_no_key_it_uses_persists_in_the_folder(bool beforeKoan)
    {
        using var folder = new TempFolder();
        Action<IServiceCollection> ephemeral = services => services.AddDataProtection().UseEphemeralDataProtectionProvider();
        string protectedValue;

        await using (var host = await BaseUrlHost.Start(
            new Dictionary<string, string?> { [Key] = folder.Path },
            configure: beforeKoan ? null : ephemeral,
            beforeKoan: beforeKoan ? ephemeral : null))
        {
            var keyRing = host.Services.GetRequiredService<KoanWebKeyRing>();
            keyRing.IsPersisted.Should().BeFalse();
            keyRing.IsOverridden.Should().BeTrue();
            keyRing.Directory.Should().BeNull();

            var fact = Fact(host);
            fact.ReasonCode.Should().Be(KoanWebConstants.KeyRing.ApplicationFactReason);
            fact.Summary.Should().Contain(folder.Path).And.Contain("EphemeralDataProtectionProvider")
                .And.Contain("application's own").And.Contain("no key");

            protectedValue = host.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("spec").Protect("lost");
        }

        // ASP.NET's own start-up may still write a key into the configured repository; the one the host used is not there.
        await using var after = await BaseUrlHost.Start(new Dictionary<string, string?> { [Key] = folder.Path });
        var unprotect = () => after.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("spec").Unprotect(protectedValue);
        unprotect.Should().Throw<System.Security.Cryptography.CryptographicException>("the ephemeral provider's key was never in the folder");
    }

    [Fact]
    public async Task A_relative_folder_resolves_against_the_content_root()
    {
        var relative = "keyring-" + Guid.NewGuid().ToString("N");
        var expected = Path.Combine(AppContext.BaseDirectory, relative);
        try
        {
            await using var host = await BaseUrlHost.Start(new Dictionary<string, string?> { [Key] = relative });
            host.Services.GetRequiredService<KoanWebKeyRing>().Directory.Should().Be(expected);
            Directory.Exists(expected).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(expected)) Directory.Delete(expected, recursive: true);
        }
    }

    [Fact]
    public async Task A_folder_that_cannot_be_created_refuses_the_start_naming_the_key()
    {
        var blocker = Path.Combine(Path.GetTempPath(), "koan-keyring-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(blocker, "a file where the folder should be", TestContext.Current.CancellationToken);
        try
        {
            var start = () => BaseUrlHost.Start(new Dictionary<string, string?> { [Key] = blocker });
            var failure = await start.Should().ThrowAsync<Exception>();
            Messages(failure.Which).Should().Contain(message =>
                message.Contains(Key) && message.Contains(blocker) && message.Contains("could not be created") && message.Contains("persistent volume"));
        }
        finally
        {
            File.Delete(blocker);
        }
    }

    /// <summary>
    /// A folder this process may not write: a deny-write rule for the current user on Windows, mode 0500 on Unix. A
    /// process whose privileges bypass either (root on Unix) cannot express the case, and the spec skips saying so.
    /// </summary>
    [Fact]
    public async Task A_folder_that_cannot_be_written_refuses_the_start_naming_the_key()
    {
        using var folder = new TempFolder();
        Directory.CreateDirectory(folder.Path);
        var restore = DenyWrites(folder.Path);
        try
        {
            if (CanWrite(folder.Path))
                Assert.Skip("This process writes the folder despite the deny rule or the read-only mode; its privileges bypass them.");

            var start = () => BaseUrlHost.Start(new Dictionary<string, string?> { [Key] = folder.Path });
            var failure = await start.Should().ThrowAsync<Exception>();
            Messages(failure.Which).Should().Contain(message =>
                message.Contains(Key) && message.Contains(folder.Path) && message.Contains("cannot be written") && message.Contains("persistent volume"));
        }
        finally
        {
            restore();
        }
    }

    [Fact]
    public async Task A_folder_whose_ring_cannot_be_loaded_refuses_the_start_naming_the_key()
    {
        using var folder = new TempFolder();
        Directory.CreateDirectory(folder.Path);
        await File.WriteAllTextAsync(Path.Combine(folder.Path, "key-00000000-0000-0000-0000-000000000000.xml"), "not a key",
            TestContext.Current.CancellationToken);

        var start = () => BaseUrlHost.Start(new Dictionary<string, string?> { [Key] = folder.Path });
        var failure = await start.Should().ThrowAsync<Exception>();
        Messages(failure.Which).Should().Contain(message =>
            message.Contains(Key) && message.Contains(folder.Path) && message.Contains("cannot hold the ring"));
    }

    [Fact]
    public async Task On_unix_a_new_folder_is_owner_only_and_a_wider_existing_one_is_reported()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows folders carry ACLs, not a POSIX mode; Koan leaves them unchanged.");
        if (OperatingSystem.IsWindows()) return;

        using var created = new TempFolder();
        await using (var host = await BaseUrlHost.Start(new Dictionary<string, string?> { [Key] = created.Path }))
        {
            File.GetUnixFileMode(created.Path).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Fact(host).Summary.Should().NotContain("wider than owner-only");
        }

        using var existing = new TempFolder();
        Directory.CreateDirectory(existing.Path);
        const UnixFileMode Wide = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
        File.SetUnixFileMode(existing.Path, Wide);
        await using (var host = await BaseUrlHost.Start(new Dictionary<string, string?> { [Key] = existing.Path }))
        {
            File.GetUnixFileMode(existing.Path).Should().Be(Wide, "an existing folder's mode is left alone");
            Fact(host).Summary.Should().Contain("0755").And.Contain("wider than owner-only");
        }
    }

    [Fact]
    public async Task Unset_the_ring_stays_where_aspnet_keeps_it_and_the_fact_says_so()
    {
        await using var host = await BaseUrlHost.Start(new Dictionary<string, string?>());

        var keyRing = host.Services.GetRequiredService<KoanWebKeyRing>();
        keyRing.IsConfigured.Should().BeFalse();
        keyRing.IsPersisted.Should().BeFalse();
        keyRing.Directory.Should().BeNull();

        var fact = Fact(host);
        fact.ReasonCode.Should().Be(KoanWebConstants.KeyRing.DefaultFactReason);
        fact.Summary.Should().Contain("not persisted by Koan").And.Contain(Key);
    }

    private static KoanFact Fact(BaseUrlHost host) => host.Services.GetRequiredService<IKoanRuntimeFacts>().Current.Facts
        .Should().ContainSingle(fact => fact.Code == KoanWebConstants.KeyRing.FactCode).Subject;

    private static Action DenyWrites(string folder) => OperatingSystem.IsWindows() ? DenyWritesByRule(folder) : DenyWritesByMode(folder);

    [SupportedOSPlatform("windows")]
    private static Action DenyWritesByRule(string folder)
    {
        var directory = new DirectoryInfo(folder);
        var rule = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.CreateFiles, AccessControlType.Deny);
        Apply(directory, security => security.AddAccessRule(rule));
        return () => Apply(directory, security => security.RemoveAccessRule(rule));
    }

    [SupportedOSPlatform("windows")]
    private static void Apply(DirectoryInfo directory, Action<DirectorySecurity> change)
    {
        var security = directory.GetAccessControl();
        change(security);
        directory.SetAccessControl(security);
    }

    [UnsupportedOSPlatform("windows")]
    private static Action DenyWritesByMode(string folder)
    {
        File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        return () => File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static bool CanWrite(string folder)
    {
        var probe = Path.Combine(folder, "spec-probe");
        try
        {
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static IEnumerable<string> Messages(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            yield return current.Message;
            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    foreach (var message in Messages(inner)) yield return message;
                }
            }
        }
    }

    /// <summary>A folder path under the temp directory, removed with everything in it.</summary>
    private sealed class TempFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "koan-keyring-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
