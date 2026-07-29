using Xunit;
using Xunit.Sdk;

namespace Koan.Testing.Tests;

/// <summary>
/// Process boundary used by Forge strict mode. Packet semantics stay in Koan.Testing; the script only supplies the
/// packet path and classifies this stable status marker.
/// </summary>
public sealed class ForgePacketValidationTests
{
    public const string PacketEnvironmentVariable = "KOAN_DATA_CONFORMANCE_PACKET";
    public const string StatusMarker = "KOAN_DATA_CONFORMANCE_STATUS=";

    [Fact]
    public void Packet_from_environment_is_valid()
    {
        var path = Environment.GetEnvironmentVariable(PacketEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(path))
            Assert.Skip($"{PacketEnvironmentVariable} is set only by Forge strict mode.");

        try
        {
            var packet = DataConformancePacket.FromJson(File.ReadAllText(path));
            var result = packet.Validate();
            if (result.Status == DataConformancePacket.ValidationStatus.Pass) return;

            throw new XunitException(
                $"{StatusMarker}{result.Status}; " +
                string.Join(" | ", result.Issues.Select(issue => $"{issue.Code}: {issue.Message}")));
        }
        catch (XunitException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new XunitException($"{StatusMarker}{DataConformancePacket.ValidationStatus.Error}; {exception.Message}");
        }
    }
}
