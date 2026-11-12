namespace StatusLight.Core.Sources;

public interface ISessionSource
{
    IReadOnlyList<SessionState> Poll(DateTimeOffset now);
}

public sealed class LocalWindowsSource(SessionDirectoryReader reader) : ISessionSource
{
    public IReadOnlyList<SessionState> Poll(DateTimeOffset now) => reader.Read(now);
}
