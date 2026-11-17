namespace StatusLight.Core.Wsl;

public sealed record CommandResult(int ExitCode, string Stdout);

public interface ICommandRunner
{
    /// <summary>Runs wsl.exe with the given arguments. Never throws; failure is a non-zero exit code.</summary>
    CommandResult Run(IReadOnlyList<string> args, TimeSpan timeout);
}
