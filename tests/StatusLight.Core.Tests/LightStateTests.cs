using StatusLight.Core;

namespace StatusLight.Core.Tests;

public class LightStateTests
{
    [Fact] public void Empty_is_off() => Assert.Equal(LightState.Off, LightStates.Aggregate([], true));

    [Fact]
    public void Red_always_wins()
    {
        Assert.Equal(LightState.Attention, LightStates.Aggregate([LightState.Idle, LightState.Attention, LightState.Working], true));
        Assert.Equal(LightState.Attention, LightStates.Aggregate([LightState.Idle, LightState.Attention, LightState.Working], false));
    }

    [Fact] public void Green_beats_yellow_when_set() => Assert.Equal(LightState.Idle, LightStates.Aggregate([LightState.Working, LightState.Idle], true));
    [Fact] public void Yellow_beats_green_when_unset() => Assert.Equal(LightState.Working, LightStates.Aggregate([LightState.Working, LightState.Idle], false));

    [Theory, InlineData("idle", LightState.Idle), InlineData("attention", LightState.Attention)]
    public void Parses_wire(string raw, LightState want)
    {
        Assert.True(LightStates.TryParse(raw, out var s));
        Assert.Equal(want, s);
    }

    [Fact] public void Rejects_unknown() => Assert.False(LightStates.TryParse("end", out _));

    [Fact]
    public void Labels_match_mac()
    {
        Assert.Equal("Waiting for input", LightStates.Label(LightState.Attention));
        Assert.Equal("Running…", LightStates.Label(LightState.Working));
        Assert.Equal("Awaiting next task", LightStates.Label(LightState.Idle));
        Assert.Equal("Not running", LightStates.Label(LightState.Off));
    }
}
