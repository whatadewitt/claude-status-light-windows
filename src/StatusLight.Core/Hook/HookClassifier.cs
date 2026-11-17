namespace StatusLight.Core.Hook;

public static class HookClassifier
{
    public static string Resolve(string stateArg, HookPayload p)
    {
        if (stateArg == "attention"
            && p.Message is not null
            && p.Message.Contains("waiting for your input", StringComparison.OrdinalIgnoreCase))
            return "idle";
        if (stateArg == "working" && p.HookEventName == "PreToolUse" && p.ToolName == "AskUserQuestion")
            return "attention";
        return stateArg;
    }
}
