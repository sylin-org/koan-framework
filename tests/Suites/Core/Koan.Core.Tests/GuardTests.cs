#nullable enable
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type
#pragma warning disable CS8620 // Argument nullability mismatch
#pragma warning disable CS8634 // Nullable type parameter constraint mismatch
#pragma warning disable xUnit1012 // Null should not be used for non-nullable parameter
using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Koan.Core.Utilities.Guard;
using Xunit;

namespace Koan.Core.Tests;

/// <summary>
/// Tests for fluent guard pattern: value.Must().NotBe.X() and value.Must().Be.X()
/// </summary>
public class GuardTests
{
    #region NotBe.Null Tests

    [Fact]
    public void NotBe_Null_WithNonNullValue_ReturnsValue()
    {
        // Arrange
        var value = "test";

        // Act
        var result = value.Must().NotBe.Null();

        // Assert
        result.Should().Be("test");
    }

    [Fact]
    public void NotBe_Null_WithNullValue_ThrowsArgumentNullException()
    {
        // Arrange
        string value = null;

        // Act
        Action act = () => value.Must().NotBe.Null();

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("value");
    }

    #endregion

    #region NotBe.Blank Tests

    [Theory]
    [InlineData("test")]
    [InlineData("  test  ")]
    [InlineData("a")]
    public void NotBe_Blank_WithNonBlankString_ReturnsValue(string value)
    {
        // Act
        var result = value.Must().NotBe.Blank();

        // Assert
        result.Should().Be(value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void NotBe_Blank_WithBlankString_ThrowsArgumentException(string value)
    {
        // Act
        Action act = () => value.Must().NotBe.Blank();

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("value");
    }

    #endregion

    #region NotBe.Empty (Guid) Tests

    [Fact]
    public void NotBe_Empty_WithNonEmptyGuid_ReturnsValue()
    {
        // Arrange
        var value = Guid.NewGuid();

        // Act
        var result = value.Must().NotBe.Empty();

        // Assert
        result.Should().Be(value);
    }

    [Fact]
    public void NotBe_Empty_WithEmptyGuid_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var value = Guid.Empty;

        // Act
        Action act = () => value.Must().NotBe.Empty();

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("value");
    }

    #endregion

    #region NotBe.Default Tests

    [Fact]
    public void NotBe_Default_WithNonDefaultInt_ReturnsValue()
    {
        // Arrange
        var value = 42;

        // Act
        var result = value.Must().NotBe.Default();

        // Assert
        result.Should().Be(42);
    }

    [Fact]
    public void NotBe_Default_WithDefaultInt_ThrowsArgumentException()
    {
        // Arrange
        var value = 0;

        // Act
        Action act = () => value.Must().NotBe.Default();

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("value");
    }

    [Fact]
    public void NotBe_Default_WithNonDefaultDateTime_ReturnsValue()
    {
        // Arrange
        var value = DateTime.UtcNow;

        // Act
        var result = value.Must().NotBe.Default();

        // Assert
        result.Should().Be(value);
    }

    [Fact]
    public void NotBe_Default_WithDefaultDateTime_ThrowsArgumentException()
    {
        // Arrange
        var value = default(DateTime);

        // Act
        Action act = () => value.Must().NotBe.Default();

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("value");
    }

    #endregion

    #region NotBe.Where Tests

    [Fact]
    public void NotBe_Where_WhenPredicateFalse_ReturnsValue()
    {
        // Arrange
        var value = 42;

        // Act
        var result = value.Must().NotBe.Where(x => x > 100);

        // Assert
        result.Should().Be(42);
    }

    [Fact]
    public void NotBe_Where_WhenPredicateTrue_ThrowsArgumentException()
    {
        // Arrange
        var value = 42;

        // Act
        Action act = () => value.Must().NotBe.Where(x => x < 100, "Value must be at least 100");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("Value must be at least 100*")
            .WithParameterName("value");
    }

    #endregion

    #region Be.Positive Tests (int)

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(int.MaxValue)]
    public void Be_Positive_Int_WithPositiveValue_ReturnsValue(int value)
    {
        // Act
        var result = value.Must().Be.Positive();

        // Assert
        result.Should().Be(value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Be_Positive_Int_WithNonPositiveValue_ThrowsArgumentOutOfRangeException(int value)
    {
        // Act
        Action act = () => value.Must().Be.Positive();

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("value");
    }

    #endregion

    #region Be.Positive Tests (long)

    [Theory]
    [InlineData(1L)]
    [InlineData(42L)]
    [InlineData(long.MaxValue)]
    public void Be_Positive_Long_WithPositiveValue_ReturnsValue(long value)
    {
        // Act
        var result = value.Must().Be.Positive();

        // Assert
        result.Should().Be(value);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void Be_Positive_Long_WithNonPositiveValue_ThrowsArgumentOutOfRangeException(long value)
    {
        // Act
        Action act = () => value.Must().Be.Positive();

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("value");
    }

    #endregion

    #region Be.Positive Tests (decimal)

    [Theory]
    [InlineData(0.1)]
    [InlineData(1.0)]
    [InlineData(42.5)]
    public void Be_Positive_Decimal_WithPositiveValue_ReturnsValue(double doubleValue)
    {
        // Arrange
        var value = (decimal)doubleValue;

        // Act
        var result = value.Must().Be.Positive();

        // Assert
        result.Should().Be(value);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.1)]
    [InlineData(-42.5)]
    public void Be_Positive_Decimal_WithNonPositiveValue_ThrowsArgumentOutOfRangeException(double doubleValue)
    {
        // Arrange
        var value = (decimal)doubleValue;

        // Act
        Action act = () => value.Must().Be.Positive();

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("value");
    }

    #endregion

    #region Be.NonNegative Tests

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(42)]
    public void Be_NonNegative_WithNonNegativeValue_ReturnsValue(int value)
    {
        // Act
        var result = value.Must().Be.NonNegative();

        // Assert
        result.Should().Be(value);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-42)]
    [InlineData(int.MinValue)]
    public void Be_NonNegative_WithNegativeValue_ThrowsArgumentOutOfRangeException(int value)
    {
        // Act
        Action act = () => value.Must().Be.NonNegative();

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("value");
    }

    #endregion

    #region Be.InRange Tests (int)

    [Theory]
    [InlineData(1, 1, 10)]
    [InlineData(5, 1, 10)]
    [InlineData(10, 1, 10)]
    public void Be_InRange_Int_WithValueInRange_ReturnsValue(int value, int min, int max)
    {
        // Act
        var result = value.Must().Be.InRange(min, max);

        // Assert
        result.Should().Be(value);
    }

    [Theory]
    [InlineData(0, 1, 10)]
    [InlineData(11, 1, 10)]
    [InlineData(-5, 1, 10)]
    public void Be_InRange_Int_WithValueOutOfRange_ThrowsArgumentOutOfRangeException(int value, int min, int max)
    {
        // Act
        Action act = () => value.Must().Be.InRange(min, max);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("value");
    }

    #endregion

    #region Be.AtLeast Tests

    [Theory]
    [InlineData(13, 13)]
    [InlineData(14, 13)]
    [InlineData(100, 13)]
    public void Be_AtLeast_WithValueAtLeastMin_ReturnsValue(int value, int min)
    {
        // Act
        var result = value.Must().Be.AtLeast(min);

        // Assert
        result.Should().Be(value);
    }

    [Theory]
    [InlineData(12, 13)]
    [InlineData(0, 13)]
    [InlineData(-5, 13)]
    public void Be_AtLeast_WithValueLessThanMin_ThrowsArgumentOutOfRangeException(int value, int min)
    {
        // Act
        Action act = () => value.Must().Be.AtLeast(min);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("value");
    }

    #endregion

    #region Be.AtMost Tests

    [Theory]
    [InlineData(100, 100)]
    [InlineData(99, 100)]
    [InlineData(0, 100)]
    public void Be_AtMost_WithValueAtMostMax_ReturnsValue(int value, int max)
    {
        // Act
        var result = value.Must().Be.AtMost(max);

        // Assert
        result.Should().Be(value);
    }

    [Theory]
    [InlineData(101, 100)]
    [InlineData(200, 100)]
    public void Be_AtMost_WithValueGreaterThanMax_ThrowsArgumentOutOfRangeException(int value, int max)
    {
        // Act
        Action act = () => value.Must().Be.AtMost(max);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("value");
    }

    #endregion

    #region Be.Where Tests

    [Fact]
    public void Be_Where_WhenPredicateTrue_ReturnsValue()
    {
        // Arrange
        var value = 42;

        // Act
        var result = value.Must().Be.Where(x => x > 40);

        // Assert
        result.Should().Be(42);
    }

    [Fact]
    public void Be_Where_WhenPredicateFalse_ThrowsArgumentException()
    {
        // Arrange
        var value = 42;

        // Act
        Action act = () => value.Must().Be.Where(x => x > 100, "Value must be greater than 100");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("Value must be greater than 100*")
            .WithParameterName("value");
    }

    #endregion

    #region Chaining Tests

    [Fact]
    public void Guards_CanChainWithBusinessLogic()
    {
        // Arrange
        var input = "  Hello World  ";

        // Act
        var result = input.Must().NotBe.Blank().Trim().ToUpper();

        // Assert
        result.Should().Be("HELLO WORLD");
    }

    [Fact]
    public void Guards_CaptureParameterNameCorrectly()
    {
        // Arrange
        string userName = null;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => userName.Must().NotBe.Null());
        exception.ParamName.Should().Be("userName");
    }

    #endregion

    #region Be.Between Tests - Inclusive Range

    [Theory]
    [InlineData(10, 10, 20)] // Lower bound
    [InlineData(15, 10, 20)] // Middle
    [InlineData(20, 10, 20)] // Upper bound
    public void Be_Between_Inclusive_WithValueInRange_ReturnsValue(int value, int min, int max)
    {
        // Act
        var result = value.Must().Be.Between(min, max, RangeType.Inclusive);

        // Assert
        result.Should().Be(value);
    }

    [Theory]
    [InlineData(9, 10, 20)]  // Below lower bound
    [InlineData(21, 10, 20)] // Above upper bound
    public void Be_Between_Inclusive_WithValueOutOfRange_ThrowsArgumentOutOfRangeException(int value, int min, int max)
    {
        // Act
        Action act = () => value.Must().Be.Between(min, max, RangeType.Inclusive);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("value")
            .WithMessage("*[10, 20]*");
    }

    #endregion

    #region Be.Between Tests - Exclusive Range

    [Theory]
    [InlineData(11, 10, 20)] // Just above lower bound
    [InlineData(15, 10, 20)] // Middle
    [InlineData(19, 10, 20)] // Just below upper bound
    public void Be_Between_Exclusive_WithValueInRange_ReturnsValue(int value, int min, int max)
    {
        // Act
        var result = value.Must().Be.Between(min, max, RangeType.Exclusive);

        // Assert
        result.Should().Be(value);
    }

    [Theory]
    [InlineData(10, 10, 20)] // Lower bound (excluded)
    [InlineData(20, 10, 20)] // Upper bound (excluded)
    [InlineData(9, 10, 20)]  // Below range
    [InlineData(21, 10, 20)] // Above range
    public void Be_Between_Exclusive_WithValueOutOfRange_ThrowsArgumentOutOfRangeException(int value, int min, int max)
    {
        // Act
        Action act = () => value.Must().Be.Between(min, max, RangeType.Exclusive);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("value")
            .WithMessage("*(10, 20)*");
    }

    #endregion

    #region Be.Between Tests - InclusiveExclusive Range

    [Theory]
    [InlineData(10, 10, 20)] // Lower bound (included)
    [InlineData(15, 10, 20)] // Middle
    [InlineData(19, 10, 20)] // Just below upper bound
    public void Be_Between_InclusiveExclusive_WithValueInRange_ReturnsValue(int value, int min, int max)
    {
        // Act
        var result = value.Must().Be.Between(min, max, RangeType.InclusiveExclusive);

        // Assert
        result.Should().Be(value);
    }

    [Theory]
    [InlineData(20, 10, 20)] // Upper bound (excluded)
    [InlineData(9, 10, 20)]  // Below range
    [InlineData(21, 10, 20)] // Above range
    public void Be_Between_InclusiveExclusive_WithValueOutOfRange_ThrowsArgumentOutOfRangeException(int value, int min, int max)
    {
        // Act
        Action act = () => value.Must().Be.Between(min, max, RangeType.InclusiveExclusive);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("value")
            .WithMessage("*[10, 20)*");
    }

    #endregion

    #region Be.Between Tests - ExclusiveInclusive Range

    [Theory]
    [InlineData(11, 10, 20)] // Just above lower bound
    [InlineData(15, 10, 20)] // Middle
    [InlineData(20, 10, 20)] // Upper bound (included)
    public void Be_Between_ExclusiveInclusive_WithValueInRange_ReturnsValue(int value, int min, int max)
    {
        // Act
        var result = value.Must().Be.Between(min, max, RangeType.ExclusiveInclusive);

        // Assert
        result.Should().Be(value);
    }

    [Theory]
    [InlineData(10, 10, 20)] // Lower bound (excluded)
    [InlineData(9, 10, 20)]  // Below range
    [InlineData(21, 10, 20)] // Above range
    public void Be_Between_ExclusiveInclusive_WithValueOutOfRange_ThrowsArgumentOutOfRangeException(int value, int min, int max)
    {
        // Act
        Action act = () => value.Must().Be.Between(min, max, RangeType.ExclusiveInclusive);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("value")
            .WithMessage("*(10, 20]*");
    }

    #endregion

    #region Be.Between Tests - Multiple Types

    [Fact]
    public void Be_Between_Long_WithValueInRange_ReturnsValue()
    {
        // Arrange
        var value = 15L;

        // Act
        var result = value.Must().Be.Between(10L, 20L, RangeType.Inclusive);

        // Assert
        result.Should().Be(15L);
    }

    [Fact]
    public void Be_Between_Decimal_WithValueInRange_ReturnsValue()
    {
        // Arrange
        var value = 15.5m;

        // Act
        var result = value.Must().Be.Between(10.0m, 20.0m, RangeType.Inclusive);

        // Assert
        result.Should().Be(15.5m);
    }

    [Fact]
    public void Be_Between_Double_WithValueInRange_ReturnsValue()
    {
        // Arrange
        var value = 15.5;

        // Act
        var result = value.Must().Be.Between(10.0, 20.0, RangeType.Inclusive);

        // Assert
        result.Should().Be(15.5);
    }

    #endregion

    #region Be.ValidEmail Tests

    [Theory]
    [InlineData("user@example.com")]
    [InlineData("test.user@example.co.uk")]
    [InlineData("user+tag@example.com")]
    [InlineData("user_name@example-domain.com")]
    [InlineData("123@example.com")]
    public void Be_ValidEmail_WithValidEmail_ReturnsValue(string email)
    {
        // Act
        var result = email.Must().Be.ValidEmail();

        // Assert
        result.Should().Be(email);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("notanemail")]
    [InlineData("@example.com")]
    [InlineData("user@")]
    [InlineData("user @example.com")] // Space
    [InlineData("user@example")]       // No TLD
    public void Be_ValidEmail_WithInvalidEmail_ThrowsArgumentException(string email)
    {
        // Act
        Action act = () => email.Must().Be.ValidEmail();

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*valid email*")
            .WithParameterName("email");
    }

    #endregion

    #region Be.ValidUrl Tests

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("https://example.com")]
    [InlineData("https://www.example.com/path/to/page")]
    [InlineData("http://example.com:8080")]
    [InlineData("https://example.com/path?query=value&other=123")]
    public void Be_ValidUrl_WithValidUrl_ReturnsValue(string url)
    {
        // Act
        var result = url.Must().Be.ValidUrl();

        // Assert
        result.Should().Be(url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("notaurl")]
    [InlineData("ftp://example.com")]     // Not HTTP/HTTPS
    [InlineData("www.example.com")]       // Missing protocol
    [InlineData("http://")]               // Incomplete
    public void Be_ValidUrl_WithInvalidUrl_ThrowsArgumentException(string url)
    {
        // Act
        Action act = () => url.Must().Be.ValidUrl();

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*valid HTTP/HTTPS URL*")
            .WithParameterName("url");
    }

    #endregion

    #region Be.MatchingPattern Tests

    [Theory]
    [InlineData("12345", @"^\d{5}$")]              // ZIP code
    [InlineData("123-45-6789", @"^\d{3}-\d{2}-\d{4}$")] // SSN format
    [InlineData("ABC123", @"^[A-Z]{3}\d{3}$")]     // Alphanumeric pattern
    public void Be_MatchingPattern_WithMatchingValue_ReturnsValue(string value, string pattern)
    {
        // Act
        var result = value.Must().Be.MatchingPattern(pattern);

        // Assert
        result.Should().Be(value);
    }

    [Theory]
    [InlineData("1234", @"^\d{5}$")]           // Too short
    [InlineData("123456", @"^\d{5}$")]         // Too long
    [InlineData("ABCDE", @"^\d{5}$")]          // Non-digits
    public void Be_MatchingPattern_WithNonMatchingValue_ThrowsArgumentException(string value, string pattern)
    {
        // Act
        Action act = () => value.Must().Be.MatchingPattern(pattern);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage($"*{pattern}*")
            .WithParameterName("value");
    }

    [Fact]
    public void Be_MatchingPattern_WithCustomMessage_UsesCustomMessage()
    {
        // Arrange
        var value = "123";
        var pattern = @"^\d{5}$";
        var message = "ZIP code must be 5 digits";

        // Act
        Action act = () => value.Must().Be.MatchingPattern(pattern, message);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage($"*{message}*")
            .WithParameterName("value");
    }

    [Fact]
    public void Be_MatchingPattern_WithNullOrWhiteSpace_ThrowsArgumentException()
    {
        // Arrange
        string value = null;

        // Act
        Action act = () => value.Must().Be.MatchingPattern(@"^\d+$");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("value");
    }

    #endregion

    #region Be.Defined (Enum) Tests

    public enum TestStatus
    {
        Active = 1,
        Inactive = 2,
        Pending = 3
    }

    [Theory]
    [InlineData(TestStatus.Active)]
    [InlineData(TestStatus.Inactive)]
    [InlineData(TestStatus.Pending)]
    public void Be_Defined_WithDefinedEnumValue_ReturnsValue(TestStatus status)
    {
        // Act
        var result = status.Must().Be.Defined<TestStatus>();

        // Assert
        result.Should().Be(status);
    }

    [Fact]
    public void Be_Defined_WithUndefinedEnumValue_ThrowsArgumentException()
    {
        // Arrange
        var invalidStatus = (TestStatus)999;

        // Act
        Action act = () => invalidStatus.Must().Be.Defined<TestStatus>();

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*TestStatus*999*")
            .WithParameterName("invalidStatus");
    }

    #endregion

    #region NotBe.Empty (Collections) Tests

    [Fact]
    public void NotBe_Empty_IEnumerable_WithNonEmptyCollection_ReturnsValue()
    {
        // Arrange
        IEnumerable<int> items = new[] { 1, 2, 3 };

        // Act
        var result = items.Must().NotBe.Empty();

        // Assert
        result.Should().BeEquivalentTo(new[] { 1, 2, 3 });
    }

    [Fact]
    public void NotBe_Empty_IEnumerable_WithEmptyCollection_ThrowsArgumentException()
    {
        // Arrange
        IEnumerable<int> items = Enumerable.Empty<int>();

        // Act
        Action act = () => items.Must().NotBe.Empty();

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*cannot be null or empty*")
            .WithParameterName("items");
    }

    [Fact]
    public void NotBe_Empty_IEnumerable_WithNullCollection_ThrowsArgumentException()
    {
        // Arrange
        IEnumerable<int> items = null;

        // Act
        Action act = () => items.Must().NotBe.Empty();

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("items");
    }

    [Fact]
    public void NotBe_Empty_IList_WithNonEmptyList_ReturnsValue()
    {
        // Arrange
        IList<string> items = new List<string> { "a", "b", "c" };

        // Act
        var result = items.Must().NotBe.Empty();

        // Assert
        result.Should().BeEquivalentTo(new[] { "a", "b", "c" });
    }

    [Fact]
    public void NotBe_Empty_IList_WithEmptyList_ThrowsArgumentException()
    {
        // Arrange
        IList<string> items = new List<string>();

        // Act
        Action act = () => items.Must().NotBe.Empty();

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*cannot be null or empty*")
            .WithParameterName("items");
    }

    [Fact]
    public void NotBe_Empty_Array_WithNonEmptyArray_ReturnsValue()
    {
        // Arrange
        var items = new[] { 1, 2, 3 };

        // Act
        var result = items.Must().NotBe.Empty();

        // Assert
        result.Should().BeEquivalentTo(new[] { 1, 2, 3 });
    }

    [Fact]
    public void NotBe_Empty_Array_WithEmptyArray_ThrowsArgumentException()
    {
        // Arrange
        var items = Array.Empty<int>();

        // Act
        Action act = () => items.Must().NotBe.Empty();

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*cannot be null or empty*")
            .WithParameterName("items");
    }

    [Fact]
    public void NotBe_Empty_Array_WithNullArray_ThrowsArgumentException()
    {
        // Arrange
        int[] items = null;

        // Act
        Action act = () => items.Must().NotBe.Empty();

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("items");
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void Guards_ComplexEntityValidation_ValidatesAllProperties()
    {
        // Arrange
        var email = "user@example.com";
        var name = "John Doe";
        var age = 25;
        var website = "https://example.com";
        var roles = new[] { "admin", "user" };

        // Act
        var validatedEmail = email.Must().Be.ValidEmail();
        var validatedName = name.Must().NotBe.Blank();
        var validatedAge = age.Must().Be.Between(13, 120, RangeType.Inclusive);
        var validatedWebsite = website.Must().Be.ValidUrl();
        var validatedRoles = roles.Must().NotBe.Empty();

        // Assert
        validatedEmail.Should().Be(email);
        validatedName.Should().Be(name);
        validatedAge.Should().Be(age);
        validatedWebsite.Should().Be(website);
        validatedRoles.Should().BeEquivalentTo(roles);
    }

    [Fact]
    public void Guards_ChainMultipleValidations_AllExecute()
    {
        // Arrange
        var email = "  user@example.com  ";

        // Act - Chain trim with email validation
        var result = email.Trim().Must().Be.ValidEmail();

        // Assert
        result.Should().Be("user@example.com");
    }

    #endregion
}
