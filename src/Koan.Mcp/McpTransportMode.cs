using System;

namespace Koan.Mcp;

[Flags]
public enum McpTransportMode
{
    None = 0,
    Stdio = 1,
    HttpSse = 2,
    All = Stdio | HttpSse
}
