using Microsoft.AspNetCore.Http;

namespace Koan.Web.Hosting;

/// <summary>What the application answered a request made in process (ARCH-0144), buffered whole.</summary>
/// <param name="Status">The status code.</param>
/// <param name="Headers">The response's headers, <c>Content-Type</c> and <c>Location</c> among them.</param>
/// <param name="Body">The bytes of the body; empty when it wrote none.</param>
public sealed record InProcessAnswer(int Status, IHeaderDictionary Headers, ReadOnlyMemory<byte> Body);
