using Koan.Core;

namespace Koan.Data.Core;

public static class DataApplicationBuilderExtensions
{
    extension(KoanApplicationBuilder builder)
    {
        /// <summary>Enters Data's host-owned declaration grammar.</summary>
        public DataCompositionBuilder Data => new();
    }
}
