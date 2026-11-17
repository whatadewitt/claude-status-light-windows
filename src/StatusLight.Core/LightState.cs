namespace StatusLight.Core;

public enum LightState { Off, Working, Idle, Attention }

public static class LightStates
{
    public static bool TryParse(string? raw, out LightState state)
    {
        switch (raw)
        {
            case "off": state = LightState.Off; return true;
            case "working": state = LightState.Working; return true;
            case "idle": state = LightState.Idle; return true;
            case "attention": state = LightState.Attention; return true;
            default: state = LightState.Off; return false;
        }
    }

    public static string ToWire(LightState s) => s switch
    {
        LightState.Working => "working",
        LightState.Idle => "idle",
        LightState.Attention => "attention",
        _ => "off",
    };

    public static string Label(LightState s) => s switch
    {
        LightState.Attention => "Waiting for input",
        LightState.Working => "Running…",
        LightState.Idle => "Awaiting next task",
        _ => "Not running",
    };

    /// <summary>
    /// Mirrors the Mac StateStore.aggregate: red always wins; between green and
    /// yellow the greenBeatsYellow setting decides; no sessions means off.
    /// </summary>
    public static LightState Aggregate(IEnumerable<LightState> states, bool greenBeatsYellow)
    {
        bool attention = false, idle = false, working = false;
        foreach (var s in states)
        {
            switch (s)
            {
                case LightState.Attention: attention = true; break;
                case LightState.Idle: idle = true; break;
                case LightState.Working: working = true; break;
            }
        }
        if (attention) return LightState.Attention;
        if (greenBeatsYellow)
        {
            if (idle) return LightState.Idle;
            if (working) return LightState.Working;
        }
        else
        {
            if (working) return LightState.Working;
            if (idle) return LightState.Idle;
        }
        return LightState.Off;
    }
}
