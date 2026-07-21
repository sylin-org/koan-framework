namespace Koan.Data.Abstractions.Pipeline;

/// <summary>
/// A round-trip stored-field transform. Data applies writes to a clone and reverses materialized values before they
/// leave its semantic boundary.
/// </summary>
public interface IFieldTransform
{
    void ApplyOnWrite(object entity);

    void ApplyOnRead(object entity);
}
