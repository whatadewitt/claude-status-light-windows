namespace StatusLight.Core;

/// <summary>
/// The Claude Code pixel mascot, copied verbatim from the Mac IconRenderer.swift.
/// <c>X</c> cells are filled with the state colour; <c>.</c> cells stay transparent.
/// Frame 0 is the rest pose. The rest are dance poses: 1 lean right, 2 lean left,
/// 3 hop, 4 crouch, 5 half-blink, 6 kick right leg out, 7 kick left leg out.
/// </summary>
public static class Mascot
{
    public static IReadOnlyList<string[]> Frames { get; } =
    [
        [
            ".XXXXXXXXXXX.",
            ".XXXXXXXXXXX.",
            ".XX.XXXXX.XX.",
            ".XX.XXXXX.XX.",
            "XXXXXXXXXXXXX",
            "XXXXXXXXXXXXX",
            "XXXXXXXXXXXXX",
            ".XXXXXXXXXXX.",
            ".XXXXXXXXXXX.",
            "..X.X...X.X..",
            "..X.X...X.X..",
        ],
        [
            "..XXXXXXXXXXX",
            "..XXXXXXXXXXX",
            "..XX.XXXXX.XX",
            "..XX.XXXXX.XX",
            ".XXXXXXXXXXXX",
            ".XXXXXXXXXXXX",
            ".XXXXXXXXXXXX",
            "..XXXXXXXXXXX",
            "..XXXXXXXXXXX",
            "...X.X..X.X..",
            "...X.X..X.X..",
        ],
        [
            "XXXXXXXXXXX..",
            "XXXXXXXXXXX..",
            "XX.XXXXX.XX..",
            "XX.XXXXX.XX..",
            "XXXXXXXXXXXX.",
            "XXXXXXXXXXXX.",
            "XXXXXXXXXXXX.",
            "XXXXXXXXXXX..",
            "XXXXXXXXXXX..",
            "..X.X..X.X...",
            "..X.X..X.X...",
        ],
        [
            ".XXXXXXXXXXX.",
            ".XXXXXXXXXXX.",
            ".XX.XXXXX.XX.",
            ".XX.XXXXX.XX.",
            "XXXXXXXXXXXXX",
            "XXXXXXXXXXXXX",
            "XXXXXXXXXXXXX",
            ".XXXXXXXXXXX.",
            ".XXXXXXXXXXX.",
            "..X.X...X.X..",
            ".............",
        ],
        [
            ".............",
            ".XXXXXXXXXXX.",
            ".XXXXXXXXXXX.",
            ".XX.XXXXX.XX.",
            ".XX.XXXXX.XX.",
            "XXXXXXXXXXXXX",
            "XXXXXXXXXXXXX",
            "XXXXXXXXXXXXX",
            ".XXXXXXXXXXX.",
            "..X.X...X.X..",
            "..X.X...X.X..",
        ],
        [
            ".XXXXXXXXXXX.",
            ".XXXXXXXXXXX.",
            ".XXXXXXXXXXX.",
            ".XX.XXXXX.XX.",
            "XXXXXXXXXXXXX",
            "XXXXXXXXXXXXX",
            "XXXXXXXXXXXXX",
            ".XXXXXXXXXXX.",
            ".XXXXXXXXXXX.",
            "..X.X...X.X..",
            "..X.X...X.X..",
        ],
        [
            ".XXXXXXXXXXX.",
            ".XXXXXXXXXXX.",
            ".XX.XXXXX.XX.",
            ".XX.XXXXX.XX.",
            "XXXXXXXXXXXXX",
            "XXXXXXXXXXXXX",
            "XXXXXXXXXXXXX",
            ".XXXXXXXXXXX.",
            ".XXXXXXXXXXX.",
            "..X.X...X...X",
            "..X.X...X...X",
        ],
        [
            ".XXXXXXXXXXX.",
            ".XXXXXXXXXXX.",
            ".XX.XXXXX.XX.",
            ".XX.XXXXX.XX.",
            "XXXXXXXXXXXXX",
            "XXXXXXXXXXXXX",
            "XXXXXXXXXXXXX",
            ".XXXXXXXXXXX.",
            ".XXXXXXXXXXX.",
            "X...X...X.X..",
            "X...X...X.X..",
        ],
    ];

    /// <summary>Dance moves: sequences of frame indices played one tick at a time.</summary>
    public static IReadOnlyList<int[]> DanceMoves { get; } =
    [
        [0, 1, 0, 2, 0, 1, 0, 2],       // sway
        [1, 2, 1, 2, 1, 2, 1, 2],       // fast shuffle
        [0, 4, 0, 3, 0, 4, 0, 3],       // bounce: crouch, up, hang, land
        [0, 6, 0, 7, 0, 6, 0, 7],       // side kicks
        [0, 5, 0, 1, 5, 2, 0, 5],       // sleepy groove (blinks mid-sway)
    ];
}
