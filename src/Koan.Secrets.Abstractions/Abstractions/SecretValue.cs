using Newtonsoft.Json;

namespace Koan.Secrets.Abstractions;

public sealed class SecretValue
{
    private readonly byte[] _data;
    public SecretContentType Type { get; }
    public SecretMetadata Meta { get; }

    public SecretValue(byte[] data, SecretContentType type, SecretMetadata? meta = null)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        Type = type;
        Meta = meta ?? new SecretMetadata();
    }

    public ReadOnlyMemory<byte> AsBytes() => _data;
    public string AsString() => Type switch
    {
        SecretContentType.Text or SecretContentType.Json => System.Text.Encoding.UTF8.GetString(_data),
        _ => throw new InvalidOperationException("Secret is not textual"),
    };

    public T AsJson<T>() => Type == SecretContentType.Json
        ? JsonConvert.DeserializeObject<T>(System.Text.Encoding.UTF8.GetString(_data))!
        : throw new InvalidOperationException("Secret is not JSON");

    public override string ToString() => "***"; // never expose
}