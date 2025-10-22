using Newtonsoft.Json.Linq;

namespace Koan.Mcp.CodeMode.Json;

/// <summary>
/// Abstraction over JSON operations for code-mode to allow framework-wide alignment
/// on Newtonsoft without hard-coding dependencies into every component.
/// </summary>
public interface IJsonFacade
{
    JToken Parse(string json);
    string Stringify(JToken token, bool indented = false);
    JToken FromObject(object? value);
    T? ToObject<T>(JToken token);
    object? ToDynamic(JToken token);
}
