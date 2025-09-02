using System;
using System.Text.Json;
using Sora.Flow.Model;
using Xunit;

namespace Sora.Flow.Core.Tests;

public class ControlCommandParametersTests
{
    [Fact]
    public void Can_Set_And_Get_Primitive_Params()
    {
        var cmd = new ControlCommand { Verb = "test" }
            .WithParam("immediate", true)
            .WithParam("count", 3)
            .WithParam("note", "hello");

        Assert.True(cmd.TryGetBoolean("immediate", out var immediate) && immediate);
        Assert.True(cmd.TryGetInt32("count", out var count) && count == 3);
        Assert.True(cmd.TryGetString("note", out var note) && note == "hello");
    }

    private sealed record Options(string Mode, int Threshold);

    [Fact]
    public void Can_Roundtrip_Object_Param()
    {
        var opts = new Options("safe", 7);
        var cmd = new ControlCommand { Verb = "configure" }.WithParam("options", opts);

        Assert.True(cmd.TryGetObject<Options>("options", out var got));
        Assert.NotNull(got);
        Assert.Equal("safe", got!.Mode);
        Assert.Equal(7, got.Threshold);
    }

    [Fact]
    public void Missing_Or_Wrong_Type_Params_Are_Safe()
    {
        var cmd = new ControlCommand { Verb = "noop" }.WithParam("flag", true);

        Assert.False(cmd.TryGetString("missing", out _));
        Assert.False(cmd.TryGetInt32("flag", out _));
    }
}
