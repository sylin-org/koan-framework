namespace Koan.Data.Abstractions;

/// <summary>Creates source-only native integration without manufacturing an Entity repository.</summary>
public interface IDataSourceIntegrationFactory : IAdapterFactory
{
    /// <summary>Describes source-only support without creating native resources or performing I/O.</summary>
    DataSourceIntegrationDescriptor DescribeSource(string source) => DataSourceIntegrationDescriptor.Empty;

    IDataSourceIntegration CreateSource(IServiceProvider services, string source);
}
