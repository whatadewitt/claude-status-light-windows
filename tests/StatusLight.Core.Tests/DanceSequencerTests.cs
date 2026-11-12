using StatusLight.Core;

namespace StatusLight.Core.Tests;

public sealed class DanceSequencerTests
{
    private const int MoveLength = 8;

    private static List<int[]> Moves(DanceSequencer seq, int count)
    {
        Assert.All(Mascot.DanceMoves, m => Assert.Equal(MoveLength, m.Length));
        return Enumerable.Range(0, count)
            .Select(_ => Enumerable.Range(0, MoveLength).Select(_ => seq.Tick()).ToArray())
            .ToList();
    }

    [Fact]
    public void Mascot_has_eight_frames_of_11_rows_by_13_columns()
    {
        Assert.Equal(8, Mascot.Frames.Count);
        Assert.All(Mascot.Frames, f =>
        {
            Assert.Equal(11, f.Length);
            Assert.All(f, row => Assert.Equal(13, row.Length));
        });
        Assert.Equal(5, Mascot.DanceMoves.Count);
    }

    [Fact]
    public void Frames_always_come_from_dance_moves()
    {
        var seq = new DanceSequencer(new Random(7));

        foreach (var move in Moves(seq, 25))
            Assert.Contains(Mascot.DanceMoves, m => m.SequenceEqual(move));
    }

    [Fact]
    public void No_back_to_back_identical_moves_over_200_ticks()
    {
        var seq = new DanceSequencer(new Random(42));

        var moves = Moves(seq, 200 / MoveLength);

        for (var i = 1; i < moves.Count; i++)
            Assert.False(moves[i].SequenceEqual(moves[i - 1]), $"move {i} repeats move {i - 1}");
    }

    [Fact]
    public void Reset_gives_frame_zero_and_restarts_on_a_whole_move()
    {
        var seq = new DanceSequencer(new Random(3));
        Assert.Equal(0, seq.Frame);
        seq.Tick();
        seq.Tick(); // second step of every move is a non-rest pose
        Assert.NotEqual(0, seq.Frame);

        seq.Reset();

        Assert.Equal(0, seq.Frame);
        var next = Moves(seq, 1)[0];
        Assert.Contains(Mascot.DanceMoves, m => m.SequenceEqual(next));
    }
}
