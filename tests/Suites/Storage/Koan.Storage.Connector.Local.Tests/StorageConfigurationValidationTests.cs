using FluentAssertions;
using Koan.Storage;
using Koan.Storage.Abstractions;
using Koan.Storage.Connector.Local;
using Koan.Storage.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Koan.Storage.Connector.Local.Tests;

/// <summary>
/// Tests for StorageService configuration validation
/// Covers the bug found in S6.SnapVault where profiles were missing Provider property
/// </summary>
public class StorageConfigurationValidationTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly ILogger<StorageService> _logger;

    public StorageConfigurationValidationTests()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        services.AddSingleton<IStorageProvider, LocalStorageProvider>();
        services.AddOptions<LocalStorageOptions>().Configure(opts =>
        {
            opts.BasePath = Path.Combine(Path.GetTempPath(), "koan-storage-tests", Guid.NewGuid().ToString("N"));
        });

        _serviceProvider = services.BuildServiceProvider();
        _logger = _serviceProvider.GetRequiredService<ILogger<StorageService>>();
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }

    [Fact(DisplayName = "VALIDATION-001: Should fail when no profiles are configured")]
    public void Should_Fail_When_No_Profiles_Configured()
    {
        // Arrange
        var storageOptions = new StorageOptions
        {
            ValidateOnStart = true,
            Profiles = new Dictionary<string, StorageProfile>() // Empty profiles
        };

        var optionsMonitor = CreateOptionsMonitor(storageOptions);
        var providers = _serviceProvider.GetServices<IStorageProvider>();

        // Act
        Action act = () => new StorageService(_logger, providers, optionsMonitor);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*No storage Profiles configured*");
    }

    [Fact(DisplayName = "VALIDATION-002: Should fail when profile has no Provider property")]
    public void Should_Fail_When_Profile_Missing_Provider()
    {
        // Arrange - This was the bug in S6.SnapVault
        var storageOptions = new StorageOptions
        {
            ValidateOnStart = true,
            Profiles = new Dictionary<string, StorageProfile>
            {
                ["cold"] = new StorageProfile
                {
                    Provider = null!, // BUG: Missing provider
                    Container = "photos"
                }
            }
        };

        var optionsMonitor = CreateOptionsMonitor(storageOptions);
        var providers = _serviceProvider.GetServices<IStorageProvider>();

        // Act
        Action act = () => new StorageService(_logger, providers, optionsMonitor);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Profile 'cold' has no Provider configured*");
    }

    [Fact(DisplayName = "VALIDATION-003: Should fail when profile has empty Provider property")]
    public void Should_Fail_When_Profile_Has_Empty_Provider()
    {
        // Arrange
        var storageOptions = new StorageOptions
        {
            ValidateOnStart = true,
            Profiles = new Dictionary<string, StorageProfile>
            {
                ["warm"] = new StorageProfile
                {
                    Provider = "", // Empty string
                    Container = "gallery"
                }
            }
        };

        var optionsMonitor = CreateOptionsMonitor(storageOptions);
        var providers = _serviceProvider.GetServices<IStorageProvider>();

        // Act
        Action act = () => new StorageService(_logger, providers, optionsMonitor);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Profile 'warm' has no Provider configured*");
    }

    [Fact(DisplayName = "VALIDATION-004: Should fail when profile has no Container property")]
    public void Should_Fail_When_Profile_Missing_Container()
    {
        // Arrange
        var storageOptions = new StorageOptions
        {
            ValidateOnStart = true,
            Profiles = new Dictionary<string, StorageProfile>
            {
                ["hot-cdn"] = new StorageProfile
                {
                    Provider = "local",
                    Container = null! // Missing container
                }
            }
        };

        var optionsMonitor = CreateOptionsMonitor(storageOptions);
        var providers = _serviceProvider.GetServices<IStorageProvider>();

        // Act
        Action act = () => new StorageService(_logger, providers, optionsMonitor);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Profile 'hot-cdn' has no Container configured*");
    }

    [Fact(DisplayName = "VALIDATION-005: Should fail when profile references unknown provider")]
    public void Should_Fail_When_Profile_References_Unknown_Provider()
    {
        // Arrange - This simulates the error message we saw in the logs
        var storageOptions = new StorageOptions
        {
            ValidateOnStart = true,
            Profiles = new Dictionary<string, StorageProfile>
            {
                ["cold"] = new StorageProfile
                {
                    Provider = "filesystem", // Wrong name - should be "local"
                    Container = "photos"
                }
            }
        };

        var optionsMonitor = CreateOptionsMonitor(storageOptions);
        var providers = _serviceProvider.GetServices<IStorageProvider>();

        // Act
        Action act = () => new StorageService(_logger, providers, optionsMonitor);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Profile 'cold' references unknown provider 'filesystem'. Ensure the provider is registered*");
    }

    [Fact(DisplayName = "VALIDATION-006: Should pass with valid configuration")]
    public void Should_Pass_With_Valid_Configuration()
    {
        // Arrange - Correct configuration
        var storageOptions = new StorageOptions
        {
            ValidateOnStart = true,
            Profiles = new Dictionary<string, StorageProfile>
            {
                ["hot-cdn"] = new StorageProfile
                {
                    Provider = "local", // Correct provider name
                    Container = "thumbnails"
                },
                ["warm"] = new StorageProfile
                {
                    Provider = "local",
                    Container = "gallery"
                },
                ["cold"] = new StorageProfile
                {
                    Provider = "local",
                    Container = "photos"
                }
            }
        };

        var optionsMonitor = CreateOptionsMonitor(storageOptions);
        var providers = _serviceProvider.GetServices<IStorageProvider>();

        // Act
        var storageService = new StorageService(_logger, providers, optionsMonitor);

        // Assert
        storageService.Should().NotBeNull();
    }

    [Fact(DisplayName = "VALIDATION-007: Should fail when DefaultProfile is set but doesn't exist")]
    public void Should_Fail_When_DefaultProfile_Not_Found()
    {
        // Arrange
        var storageOptions = new StorageOptions
        {
            ValidateOnStart = true,
            DefaultProfile = "nonexistent",
            Profiles = new Dictionary<string, StorageProfile>
            {
                ["cold"] = new StorageProfile
                {
                    Provider = "local",
                    Container = "photos"
                }
            }
        };

        var optionsMonitor = CreateOptionsMonitor(storageOptions);
        var providers = _serviceProvider.GetServices<IStorageProvider>();

        // Act
        Action act = () => new StorageService(_logger, providers, optionsMonitor);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*DefaultProfile 'nonexistent' not found in Profiles*");
    }

    [Fact(DisplayName = "VALIDATION-008: Should warn when SingleProfileOnly with multiple profiles but no default")]
    public void Should_Fail_When_Multiple_Profiles_No_Default()
    {
        // Arrange
        var storageOptions = new StorageOptions
        {
            ValidateOnStart = true,
            FallbackMode = StorageFallbackMode.SingleProfileOnly,
            DefaultProfile = null,
            Profiles = new Dictionary<string, StorageProfile>
            {
                ["profile1"] = new StorageProfile { Provider = "local", Container = "container1" },
                ["profile2"] = new StorageProfile { Provider = "local", Container = "container2" }
            }
        };

        var optionsMonitor = CreateOptionsMonitor(storageOptions);
        var providers = _serviceProvider.GetServices<IStorageProvider>();

        // Act
        Action act = () => new StorageService(_logger, providers, optionsMonitor);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Multiple storage profiles configured but no DefaultProfile set*");
    }

    [Fact(DisplayName = "VALIDATION-009: Should pass with SingleProfileOnly when exactly one profile")]
    public void Should_Pass_With_SingleProfileOnly_When_One_Profile()
    {
        // Arrange
        var storageOptions = new StorageOptions
        {
            ValidateOnStart = true,
            FallbackMode = StorageFallbackMode.SingleProfileOnly,
            DefaultProfile = null,
            Profiles = new Dictionary<string, StorageProfile>
            {
                ["only-profile"] = new StorageProfile { Provider = "local", Container = "container" }
            }
        };

        var optionsMonitor = CreateOptionsMonitor(storageOptions);
        var providers = _serviceProvider.GetServices<IStorageProvider>();

        // Act
        var storageService = new StorageService(_logger, providers, optionsMonitor);

        // Assert
        storageService.Should().NotBeNull();
    }

    [Fact(DisplayName = "VALIDATION-010: Should allow skipping validation when ValidateOnStart is false")]
    public void Should_Skip_Validation_When_Disabled()
    {
        // Arrange - Invalid config but validation disabled
        var storageOptions = new StorageOptions
        {
            ValidateOnStart = false, // Validation disabled
            Profiles = new Dictionary<string, StorageProfile>
            {
                ["bad-profile"] = new StorageProfile
                {
                    Provider = "nonexistent",
                    Container = "container"
                }
            }
        };

        var optionsMonitor = CreateOptionsMonitor(storageOptions);
        var providers = _serviceProvider.GetServices<IStorageProvider>();

        // Act
        var storageService = new StorageService(_logger, providers, optionsMonitor);

        // Assert - Should not throw during construction
        storageService.Should().NotBeNull();
    }

    /// <summary>
    /// Helper to create an IOptionsMonitor from StorageOptions
    /// </summary>
    private IOptionsMonitor<StorageOptions> CreateOptionsMonitor(StorageOptions options)
    {
        var services = new ServiceCollection();
        services.Configure<StorageOptions>(opts =>
        {
            opts.ValidateOnStart = options.ValidateOnStart;
            opts.DefaultProfile = options.DefaultProfile;
            opts.FallbackMode = options.FallbackMode;
            opts.Profiles = options.Profiles;
        });
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IOptionsMonitor<StorageOptions>>();
    }
}
