namespace Koan.Testing.Contracts;

public interface IInitializableFixture
{
    ValueTask InitializeAsync(TestContext context);
}
