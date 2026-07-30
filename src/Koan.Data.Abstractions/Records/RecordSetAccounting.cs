namespace Koan.Data.Abstractions;

/// <summary>Deterministic MaterializedValueV1 safety accounting used during neutral conversion.</summary>
public static class RecordSetAccounting
{
    public static long MeasureShape(IReadOnlyList<DataField> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        long total = 0;
        foreach (var field in fields)
            total = checked(total + 8 + StringCost(field.Name) +
                (field.ProviderTypeName is null ? 0 : StringCost(field.ProviderTypeName)));
        return total;
    }

    public static long MeasurePresentValue(object? value)
        => checked(1 + Payload(value));

    private static long Payload(object? value) => value switch
    {
        null => 0,
        bool => 1,
        sbyte or byte => 1,
        short or ushort => 2,
        int or uint or float => 4,
        long or ulong or double => 8,
        decimal => 16,
        string text => StringCost(text),
        byte[] bytes => checked(4 + bytes.LongLength),
        Guid => 16,
        DateOnly => 4,
        TimeOnly or DateTime or TimeSpan => 8,
        DateTimeOffset => 16,
        DataArray array => MeasureArray(array),
        DataObject dataObject => MeasureObject(dataObject),
        _ => throw new NeutralDataValueException(value.GetType())
    };

    private static long MeasureArray(DataArray array)
    {
        long total = 4;
        foreach (var item in array.Items) total = checked(total + MeasurePresentValue(item));
        return total;
    }

    private static long MeasureObject(DataObject dataObject)
    {
        long total = 4;
        foreach (var property in dataObject.Properties)
            total = checked(total + StringCost(property.Name) + MeasurePresentValue(property.Value));
        return total;
    }

    private static long StringCost(string value) => checked(4 + 2L * value.Length);
}
