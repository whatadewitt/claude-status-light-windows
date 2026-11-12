using StatusLight.Core;

namespace StatusLight.Core.Tests;

public class TranscriptTitlesTests
{
    private static string Agent(string n) => $"{{\"type\":\"agent-name\",\"agentName\":\"{n}\"}}";
    private static string Ai(string n) => $"{{\"type\":\"ai-title\",\"aiTitle\":\"{n}\"}}";

    [Fact]
    public void AgentNameWinsOverAiTitle()
        => Assert.Equal("Agent", TranscriptTitles.ExtractTitle(Ai("AI") + "\n" + Agent("Agent") + "\n"));

    [Fact]
    public void FallsBackToAiTitle()
        => Assert.Equal("AI", TranscriptTitles.ExtractTitle(Ai("AI") + "\n"));

    [Fact]
    public void LatestOfEachKindWins()
    {
        Assert.Equal("B", TranscriptTitles.ExtractTitle(Agent("A") + "\n" + Agent("B") + "\n"));
        Assert.Equal("Y", TranscriptTitles.ExtractTitle(Ai("X") + "\n" + Ai("Y") + "\n"));
    }

    [Fact]
    public void TruncatedLineIsSkipped()
    {
        var head = Agent("Keep") + "\n" + "{\"type\":\"agent-name\",\"agentName\":\"Cut";
        Assert.Equal("Keep", TranscriptTitles.ExtractTitle(head));
    }

    [Fact]
    public void EmptyTitleGivesNull()
    {
        Assert.Null(TranscriptTitles.ExtractTitle(Agent("") + "\n"));
        Assert.Null(TranscriptTitles.ExtractTitle("{\"type\":\"user\"}\n"));
    }

    [Fact]
    public void CacheReturnsNullForMissingOrEmptyPath()
    {
        var c = new TranscriptTitleCache();
        Assert.Null(c.TitleFor(""));
        Assert.Null(c.TitleFor(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jsonl")));
    }

    [Fact]
    public void CacheReadsTitleFromFile()
    {
        var p = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jsonl");
        try
        {
            File.WriteAllText(p, Agent("Hello") + "\n");
            Assert.Equal("Hello", new TranscriptTitleCache().TitleFor(p));
        }
        finally { File.Delete(p); }
    }

    [Fact]
    public void CacheDoesNotReReadWhenSizeUnchanged()
    {
        var p = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jsonl");
        try
        {
            var c = new TranscriptTitleCache();
            File.WriteAllText(p, Agent("AAAA") + "\n");
            Assert.Equal("AAAA", c.TitleFor(p));
            File.Delete(p);
            File.WriteAllText(p, Agent("BBBB") + "\n");
            Assert.Equal("AAAA", c.TitleFor(p));
            File.WriteAllText(p, Agent("CCCCC") + "\n");
            Assert.Equal("CCCCC", c.TitleFor(p));
        }
        finally { File.Delete(p); }
    }
}
