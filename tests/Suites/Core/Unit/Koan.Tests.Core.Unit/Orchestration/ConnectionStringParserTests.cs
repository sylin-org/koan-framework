using Xunit;
using Koan.Core.Orchestration;

namespace Koan.Tests.Core.Unit.Orchestration;

public class ConnectionStringParserTests
{
    [Theory]
    [InlineData("Host=localhost;Port=5432;Database=test;Username=user;Password=pass", "postgres", "localhost", 5432, "test", "user", "pass")]
    [InlineData("Server=localhost,1433;Database=mydb;User Id=sa;Password=secret", "sqlserver", "localhost", 1433, "mydb", "sa", "secret")]
    [InlineData("mongodb://user:pass@localhost:27017/testdb", "mongodb", "localhost", 27017, "testdb", "user", "pass")]
    [InlineData("localhost:6379,password=mypass", "redis", "localhost", 6379, null, null, "mypass")]
    [InlineData("Data Source=test.db", "sqlite", "localhost", 0, "test.db", null, null)]
    public void Parse_ValidConnectionString_ReturnsCorrectComponents(
        string connectionString,
        string providerType,
        string expectedHost,
        int expectedPort,
        string? expectedDatabase,
        string? expectedUsername,
        string? expectedPassword)
    {
        // Act
        var result = ConnectionStringParser.Parse(connectionString, providerType);

        // Assert
        Assert.Equal(expectedHost, result.Host);
        Assert.Equal(expectedPort, result.Port);
        Assert.Equal(expectedDatabase, result.Database);
        Assert.Equal(expectedUsername, result.Username);
        Assert.Equal(expectedPassword, result.Password);
    }

    [Fact]
    public void Parse_PostgresConnectionString_ParsesAllComponents()
    {
        // Arrange
        var connectionString = "Host=dbserver;Port=5433;Database=myapp;Username=admin;Password=secret123";

        // Act
        var result = ConnectionStringParser.Parse(connectionString, "postgres");

        // Assert
        Assert.Equal("dbserver", result.Host);
        Assert.Equal(5433, result.Port);
        Assert.Equal("myapp", result.Database);
        Assert.Equal("admin", result.Username);
        Assert.Equal("secret123", result.Password);
    }

    [Fact]
    public void Parse_SqlServerConnectionString_ParsesServerWithPort()
    {
        // Arrange
        var connectionString = "Server=sqlserver,1433;Database=Production;User Id=sa;Password=P@ssw0rd";

        // Act
        var result = ConnectionStringParser.Parse(connectionString, "sqlserver");

        // Assert
        Assert.Equal("sqlserver", result.Host);
        Assert.Equal(1433, result.Port);
        Assert.Equal("Production", result.Database);
        Assert.Equal("sa", result.Username);
        Assert.Equal("P@ssw0rd", result.Password);
    }

    [Fact]
    public void Parse_MongoDbConnectionString_ParsesUriFormat()
    {
        // Arrange
        var connectionString = "mongodb://testuser:testpass@mongoserver:27018/appdb";

        // Act
        var result = ConnectionStringParser.Parse(connectionString, "mongodb");

        // Assert
        Assert.Equal("mongoserver", result.Host);
        Assert.Equal(27018, result.Port);
        Assert.Equal("appdb", result.Database);
        Assert.Equal("testuser", result.Username);
        Assert.Equal("testpass", result.Password);
    }

    [Fact]
    public void Parse_MongoDbConnectionString_WithoutAuth_ParsesCorrectly()
    {
        // Arrange
        var connectionString = "mongodb://localhost:27017/mydb";

        // Act
        var result = ConnectionStringParser.Parse(connectionString, "mongodb");

        // Assert
        Assert.Equal("localhost", result.Host);
        Assert.Equal(27017, result.Port);
        Assert.Equal("mydb", result.Database);
        Assert.Null(result.Username);
        Assert.Null(result.Password);
    }

    [Fact]
    public void Parse_RedisConnectionString_ParsesHostPortPassword()
    {
        // Arrange
        var connectionString = "redishost:6380,password=secretpass";

        // Act
        var result = ConnectionStringParser.Parse(connectionString, "redis");

        // Assert
        Assert.Equal("redishost", result.Host);
        Assert.Equal(6380, result.Port);
        Assert.Equal("secretpass", result.Password);
    }

    [Fact]
    public void Parse_RedisConnectionString_WithoutPassword_ParsesCorrectly()
    {
        // Arrange
        var connectionString = "localhost:6379";

        // Act
        var result = ConnectionStringParser.Parse(connectionString, "redis");

        // Assert
        Assert.Equal("localhost", result.Host);
        Assert.Equal(6379, result.Port);
        Assert.Null(result.Password);
    }

    [Fact]
    public void Parse_SqliteConnectionString_ParsesDataSource()
    {
        // Arrange
        var connectionString = "Data Source=/path/to/database.db;Mode=ReadWrite";

        // Act
        var result = ConnectionStringParser.Parse(connectionString, "sqlite");

        // Assert
        Assert.Equal("/path/to/database.db", result.Database);
        Assert.Equal("localhost", result.Host); // SQLite doesn't use host
        Assert.Equal(0, result.Port); // SQLite doesn't use port
        Assert.Contains("Mode", result.Parameters.Keys);
        Assert.Equal("ReadWrite", result.Parameters["Mode"]);
    }

    [Fact]
    public void Parse_EmptyConnectionString_ReturnsEmpty()
    {
        // Act
        var result = ConnectionStringParser.Parse("", "postgres");

        // Assert
        Assert.Equal(ConnectionStringComponents.Empty, result);
    }

    [Fact]
    public void Parse_NullConnectionString_ReturnsEmpty()
    {
        // Act
        var result = ConnectionStringParser.Parse(null!, "postgres");

        // Assert
        Assert.Equal(ConnectionStringComponents.Empty, result);
    }

    [Fact]
    public void Build_PostgresComponents_ReturnsValidConnectionString()
    {
        // Arrange
        var components = new ConnectionStringComponents(
            Host: "dbserver",
            Port: 5432,
            Database: "mydb",
            Username: "admin",
            Password: "pass123",
            Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        // Act
        var result = ConnectionStringParser.Build(components, "postgres");

        // Assert
        Assert.Contains("Host=dbserver", result);
        Assert.Contains("Port=5432", result);
        Assert.Contains("Database=mydb", result);
        Assert.Contains("Username=admin", result);
        Assert.Contains("Password=pass123", result);
    }

    [Fact]
    public void Build_SqlServerComponents_ReturnsValidConnectionString()
    {
        // Arrange
        var components = new ConnectionStringComponents(
            Host: "sqlserver",
            Port: 1433,
            Database: "Production",
            Username: "sa",
            Password: "secret",
            Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        // Act
        var result = ConnectionStringParser.Build(components, "sqlserver");

        // Assert
        Assert.Contains("Server=sqlserver", result);
        Assert.Contains("Database=Production", result);
        Assert.Contains("User Id=sa", result);
        Assert.Contains("Password=secret", result);
    }

    [Fact]
    public void Build_MongoDbComponents_ReturnsValidConnectionString()
    {
        // Arrange
        var components = new ConnectionStringComponents(
            Host: "mongoserver",
            Port: 27017,
            Database: "appdb",
            Username: "user",
            Password: "pass",
            Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        // Act
        var result = ConnectionStringParser.Build(components, "mongodb");

        // Assert
        Assert.StartsWith("mongodb://", result);
        Assert.Contains("user:pass@", result);
        Assert.Contains("mongoserver:27017", result);
        Assert.Contains("/appdb", result);
    }

    [Fact]
    public void Build_RedisComponents_ReturnsValidConnectionString()
    {
        // Arrange
        var components = new ConnectionStringComponents(
            Host: "redishost",
            Port: 6379,
            Database: null,
            Username: null,
            Password: "mypass",
            Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        // Act
        var result = ConnectionStringParser.Build(components, "redis");

        // Assert
        Assert.Contains("redishost:6379", result);
        Assert.Contains("password=mypass", result);
    }

    [Fact]
    public void ExtractEndpoint_ValidConnectionString_ReturnsHostAndPort()
    {
        // Arrange
        var connectionString = "Host=dbserver;Port=5433;Database=myapp";

        // Act
        var (host, port) = ConnectionStringParser.ExtractEndpoint(connectionString, "postgres");

        // Assert
        Assert.Equal("dbserver", host);
        Assert.Equal(5433, port);
    }

    [Fact]
    public void ParseAndBuild_RoundTrip_PostgresConnectionString()
    {
        // Arrange
        var original = "Host=localhost;Port=5432;Database=test;Username=user;Password=pass";

        // Act
        var components = ConnectionStringParser.Parse(original, "postgres");
        var rebuilt = ConnectionStringParser.Build(components, "postgres");
        var reparsed = ConnectionStringParser.Parse(rebuilt, "postgres");

        // Assert
        Assert.Equal(components.Host, reparsed.Host);
        Assert.Equal(components.Port, reparsed.Port);
        Assert.Equal(components.Database, reparsed.Database);
        Assert.Equal(components.Username, reparsed.Username);
        Assert.Equal(components.Password, reparsed.Password);
    }

    [Fact]
    public void Parse_PostgresAlternateKeyNames_ParsesCorrectly()
    {
        // Arrange - using alternate key names like "User ID" instead of "Username"
        var connectionString = "Host=localhost;Port=5432;Database=test;User ID=myuser;Pwd=mypass";

        // Act
        var result = ConnectionStringParser.Parse(connectionString, "postgres");

        // Assert
        Assert.Equal("myuser", result.Username);
        Assert.Equal("mypass", result.Password);
    }

    [Fact]
    public void Parse_SqlServerAlternateKeyNames_ParsesCorrectly()
    {
        // Arrange - using "Data Source" instead of "Server", "Initial Catalog" instead of "Database"
        var connectionString = "Data Source=localhost;Initial Catalog=mydb;User Id=sa;Password=pass";

        // Act
        var result = ConnectionStringParser.Parse(connectionString, "sqlserver");

        // Assert
        Assert.Equal("localhost", result.Host);
        Assert.Equal("mydb", result.Database);
        Assert.Equal("sa", result.Username);
    }

    [Fact]
    public void Parse_AdditionalParameters_PreservesInParametersDictionary()
    {
        // Arrange
        var connectionString = "Host=localhost;Port=5432;Database=test;Timeout=30;Pooling=true";

        // Act
        var result = ConnectionStringParser.Parse(connectionString, "postgres");

        // Assert
        Assert.Contains("Timeout", result.Parameters.Keys);
        Assert.Equal("30", result.Parameters["Timeout"]);
        Assert.Contains("Pooling", result.Parameters.Keys);
        Assert.Equal("true", result.Parameters["Pooling"]);
    }
}
