namespace Koan.Data.Abstractions.Sources;

/// <summary>Declares the data-mutation ceiling for a source independently of storage ownership.</summary>
public enum DataSourceAccess
{
    ReadWrite,
    ReadOnly
}
