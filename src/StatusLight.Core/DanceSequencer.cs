namespace StatusLight.Core;

/// <summary>
/// Port of the Mac AppDelegate dance choreography: play a move through one frame per tick,
/// then switch to a different randomly chosen move.
/// </summary>
public sealed class DanceSequencer(Random rng)
{
    private int _move = -1;
    private int _step;

    /// <summary>The frame shown now; 0 (rest pose) until the first tick and after <see cref="Reset"/>.</summary>
    public int Frame { get; private set; }

    public int Tick()
    {
        var moves = Mascot.DanceMoves;
        if (_move < 0 || _step >= moves[_move].Length)
        {
            _move = NextMove(moves.Count);
            _step = 0;
        }
        Frame = moves[_move][_step++];
        return Frame;
    }

    public void Reset()
    {
        _move = -1;
        _step = 0;
        Frame = 0;
    }

    private int NextMove(int count)
    {
        if (_move < 0) return rng.Next(count);
        var pick = rng.Next(count - 1); // skip the current move
        return pick >= _move ? pick + 1 : pick;
    }
}
