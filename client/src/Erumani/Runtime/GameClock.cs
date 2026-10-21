namespace Erumani.Runtime;

/// <summary>
/// SRPGEXEC.EXE's game tick (FUN_00408530, the main loop in FUN_004083b8): 33 ms from start-up, 30 ms once a map or a
/// camp starts (FUN_0041dc34, FUN_0041d5e4), and 33 ms again from a battle scene's 15th tick (times 1 + GAME.DAT [4] / 100
/// while its animation plays). Loading a save keeps whatever it was. The client runs two frames per tick, so a frame
/// lasts half a tick; effects that the exe times with GetTickCount (wipes, fades of the screen commands, flashes, shake,
/// waits) count milliseconds through <see cref="FrameMs"/> instead.
/// </summary>
public static class GameClock
{
    public static int TickMs { get; private set; } = 33;
    public static double FrameMs => TickMs / 2.0;
    /// <summary>The tick changed: the game adjusts its frame rate.</summary>
    public static event Action? Changed;

    public static void SetTick(int ms)
    {
        if (ms == TickMs) return;
        TickMs = ms;
        Changed?.Invoke();
    }

    /// <summary>Frames that <paramref name="ms"/> milliseconds take at the current tick.</summary>
    public static int Frames(double ms) => (int)Math.Ceiling(ms / FrameMs);
}
