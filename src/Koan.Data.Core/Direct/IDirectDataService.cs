namespace Koan.Data.Core.Direct;

public interface IDirectDataService
{
    IDirectSession Direct(string sourceOrAdapter);
}