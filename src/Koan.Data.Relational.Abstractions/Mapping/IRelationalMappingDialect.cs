using Koan.Data.Abstractions;

namespace Koan.Data.Relational.Mapping;

/// <summary>Lowers one already-resolved physical mapping path into a provider SQL value expression.</summary>
public interface IRelationalMappingDialect : Linq.ILinqSqlDialect
{
    string Read(PhysicalPath path, MappingValueShape shape, Type physicalType);
}
