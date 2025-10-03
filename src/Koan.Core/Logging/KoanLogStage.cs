using System;

namespace Koan.Core.Logging;

/// <summary>
/// Canonical stages used for Koan logging payloads and formatting.
/// </summary>
public enum KoanLogStage
{
    Bldg,
    Boot,
    Cnfg,
    Snap,
    Data,
    Srvc,
    Hlth,
    Host
}

public static class KoanLogStageExtensions
{
    public static string GetCode(this KoanLogStage stage) => stage switch
    {
        KoanLogStage.Bldg => "BLDG",
        KoanLogStage.Boot => "BOOT",
        KoanLogStage.Cnfg => "CNFG",
        KoanLogStage.Snap => "SNAP",
        KoanLogStage.Data => "DATA",
        KoanLogStage.Srvc => "SRVC",
        KoanLogStage.Hlth => "HLTH",
        KoanLogStage.Host => "HOST",
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown Koan log stage")
    };

    public static string GetToken(this KoanLogStage stage) => $"[K:{stage.GetCode()}]";
}
