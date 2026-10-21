using Microsoft.Xna.Framework;

namespace Erumani.Runtime;

/// <summary>
/// Screen-level effects, as SRPGEXEC.EXE runs them (FORMAT_NOTES §16). The exe draws with an 8-bit palette and fades and
/// tints by rewriting palette entries 10–245 (FUN_00405a18): brightness (FUN_004059e4) and tone (FUN_004059b0) change
/// the whole screen at once, frame and windows included, and only the fixed entries (black) stay as they are. Fades
/// between screens step the brightness once per game tick (the main loop's 0x90 / 0x91 flags); the event commands' own
/// loops (screen display / erase FUN_00422ecc / FUN_004231c0, wipes FUN_00422a00, flash / tone FUN_00423914, shake
/// FUN_00423e70) run on real time and block the script, which <see cref="Busy"/> stands for.
/// </summary>
public sealed class ScreenEffects
{
    // ---------------- brightness ----------------

    /// <summary>The palette's brightness: 100 normal, 0 black, 200 white (just below 0 before some fade-ins).</summary>
    public int Brightness { get; private set; } = 100;
    int _tickStep, _tickFrame;

    /// <summary>
    /// The main loop's fade-in: from <paramref name="from"/>, <paramref name="step"/> more each game tick up to 100
    /// (a map −20 / +20, a camp or the title 0 / +10, GAMEOVER.BMP 0 / +5), after <paramref name="delayMs"/> of the
    /// screen as it is (FUN_0040bfbc holds a loaded save black for 800 ms).
    /// </summary>
    public void FadeIn(int from, int step, double delayMs = 0)
    {
        Brightness = from;
        _tickStep = step;
        _tickFrame = 0;
        _tickDelay = delayMs;
        _ramp = -1;
    }

    double _tickDelay;
    /// <summary>The hold before a fade-in: the exe runs nothing meanwhile.</summary>
    public bool Holding => _tickDelay > 0;

    /// <summary>The main loop's fade-out to black, <paramref name="step"/> less each tick (a map's end 5, title and camp 10, the turn menu 20).</summary>
    public void FadeOut(int step)
    {
        _tickStep = -step;
        _tickFrame = 0;
        _ramp = -1;
    }

    public bool FadingIn => _tickStep > 0;
    /// <summary>A fade-out has run its course: what the exe's next step waits for (brightness 0).</summary>
    public bool Dark => _tickStep <= 0 && _ramp < 0 && Brightness <= 0;
    public void SetBrightness(int value) { Brightness = value; _tickStep = 0; _ramp = -1; }

    // real-time brightness steps of the screen commands: 20 steps 33 ms apart, the first two at once (their loops draw
    // step i, then wait until 33·i ms), then the final value
    int _rampFrom, _rampStep, _rampEnd;
    double _ramp = -1;

    void Ramp(int from, int step, int end)
    {
        _rampFrom = from;
        _rampStep = step;
        _rampEnd = end;
        _ramp = 0;
        _tickStep = 0;
        Brightness = from;
    }

    /// <summary>Step <c>i</c> of a real-time loop whose step i is drawn at 33·(i−1) ms: <c>⌊t / 33⌋ + 1</c>, at most <paramref name="last"/>.</summary>
    static int LoopStep(double ms, int stepMs, int last) => Math.Min(last, (int)(ms / stepMs) + 1);

    /// <summary>
    /// Erase screen (FUN_004231c0) to black (0) or white (1). Effect 6 dims the palette from 100 in 5 % steps (or brightens
    /// it towards white); 0 is a cut; 1–5 wipe in a screen filled with the colour. Afterwards the brightness is 0 (or 200),
    /// so the whole screen stays black (white) until a screen display.
    /// </summary>
    public void Erase(int color, int effect)
    {
        int end = color == 1 ? 200 : 0;
        _tickStep = 0;
        if (effect == 6) { Ramp(100, color == 1 ? 5 : -5, end); return; }
        _ramp = -1;
        if (effect is >= 1 and <= 5)
        {
            Fill = color == 1 ? Color.White : Color.Black;   // the new screen is palette entry 0 (255): black (white)
            StartWipe(effect);
            _afterWipe = end;
            return;
        }
        Brightness = end;
    }

    /// <summary>
    /// Screen display (FUN_00422ecc) from black (0) or white (1). Effect 6 raises the palette from 0 (or lowers it from
    /// 200) in 5 % steps; 0 is a cut; 1–5 fill the screen with the colour at full brightness and wipe the picture in.
    /// </summary>
    public void Display(int color, int effect)
    {
        _tickStep = 0;
        if (effect == 6) { Ramp(color == 1 ? 200 : 0, color == 1 ? -5 : 5, 100); return; }
        _ramp = -1;
        Brightness = 100;
        StartWipe(effect, color == 1 ? Color.White : Color.Black);
    }

    /// <summary>While an erase wipes in its colour: the screen being wiped in (palette entry 0 or 255).</summary>
    public Color? Fill { get; private set; }
    int _afterWipe = int.MinValue;

    // ---------------- pictures (FUN_004233f0 / FUN_004237c4) ----------------

    /// <summary>Pictures shown, oldest first: a new one is drawn over the ones still up (they share one surface until erased).</summary>
    public List<(string File, int Position, bool Transparent)> Pictures { get; } = new();
    public string? Picture => Pictures.Count > 0 ? Pictures[^1].File : null;

    /// <summary>A picture comes up at once or with a wipe (methods 1–5); it stays over the ones already shown.</summary>
    public void ShowPicture(string file, int position, int method, bool transparent)
    {
        Pictures.Add((file, position, transparent));
        StartWipe(method);
    }

    /// <summary>Every picture goes, at once or with a wipe.</summary>
    public void ErasePicture(int method)
    {
        if (Pictures.Count == 0) return;
        Pictures.Clear();
        StartWipe(method);
    }

    // ---------------- wipes (FUN_00422a00) ----------------

    /// <summary>The running wipe (1 random blocks, 2 horizontal lines, 3 vertical lines, 4 horizontal bands, 5 vertical bands), or 0.</summary>
    public int Transition { get; private set; }
    double _wipeMs;
    /// <summary>The old screen to wipe away: a snapshot of the last frame drawn, or a solid colour (screen display starts from one).</summary>
    public Color? TransitionFrom { get; private set; }
    /// <summary>The renderer must snapshot the last frame before drawing the next one.</summary>
    public bool SnapshotRequested { get; private set; }
    public void SnapshotTaken() => SnapshotRequested = false;

    /// <summary>Milliseconds per step and steps, per wipe: 20 × 33 ms, bands 30 × 22 ms and 40 × 16 ms.</summary>
    static (int Ms, int Steps) WipeTiming(int effect) => effect switch { 4 => (22, 30), 5 => (16, 40), _ => (33, 20) };

    /// <summary>The last step of the wipe already on screen: step i is drawn at (i−1)·ms, so steps 0 and 1 come at once.</summary>
    public int TransitionTick => Transition == 0 ? 0 : LoopStep(_wipeMs, WipeTiming(Transition).Ms, WipeTiming(Transition).Steps - 1);
    /// <summary>A wipe's loop returns once its last step has had its time: (steps − 1)·ms.</summary>
    bool TransitionDone => Transition == 0 || _wipeMs >= (WipeTiming(Transition).Steps - 1) * WipeTiming(Transition).Ms;

    /// <summary>Effect 1's order: the 80×60 blocks of 8×8 px, shuffled anew for every wipe (as the exe does with rand()).</summary>
    public int[] WipeOrder { get; } = Enumerable.Range(0, 4800).ToArray();
    readonly Random _wipeRng = new();

    void StartWipe(int effect, Color? from = null)
    {
        if (effect is < 1 or > 5) { Transition = 0; return; }
        if (effect == 1)
            for (int i = 0; i < WipeOrder.Length; i++)
            {
                int j = _wipeRng.Next(WipeOrder.Length);
                (WipeOrder[i], WipeOrder[j]) = (WipeOrder[j], WipeOrder[i]);
            }
        Transition = effect;
        _wipeMs = 0;
        TransitionFrom = from;
        SnapshotRequested = from is null;
    }

    // ---------------- tone and flashes (FUN_00423914) ----------------

    /// <summary>Screen tone in percent per channel (100 = unchanged) and vividness (100 = full colour).</summary>
    public Vector4 Tone { get; private set; } = new(100, 100, 100, 100);
    /// <summary>The tone a flash shows for a moment instead of <see cref="Tone"/>; null when no flash is on.</summary>
    public Vector4? FlashTone { get; private set; }
    Vector4 _toneFrom, _toneTo;
    int _toneMode = -1;
    double _toneMs;

    /// <summary>
    /// Flash / tone (FUN_00423914), the palette's tone in percent (R, G, B, vividness). Flash 0 shows the colour for 33 ms;
    /// flash 1 for 33 ms, then 33 ms off, then 66 ms; flash 2 moves 1/8 of the way towards it every 33 ms for 8 steps,
    /// then snaps back. Tone changes stay: 3 at once, 4 in 15 steps and 5 in 30 steps of 33 ms. A fade-in still under
    /// way ends first.
    /// </summary>
    public void FlashTint(int mode, int r, int g, int b, int vivid)
    {
        if (FadingIn) SetBrightness(100);
        var target = new Vector4(r, g, b, vivid);
        _toneFrom = Tone;
        _toneTo = target;
        _toneMs = 0;
        _toneMode = mode;
        if (mode == 3) { Tone = target; _toneMode = -1; return; }
        if (mode is < 0 or > 5) { _toneMode = -1; return; }
        UpdateTone();
    }

    /// <summary>The tone straight away (FUN_004059b0): the title, a camp and the logos reset it to 100 % everywhere.</summary>
    public void SetTone(int r, int g, int b, int vivid)
    {
        Tone = new Vector4(r, g, b, vivid);
        FlashTone = null;
        _toneMode = -1;
    }

    /// <summary>
    /// A new map (FUN_0041dc34): pictures, wipes, flashes and the shake are gone; the brightness and the tone (the palette's)
    /// stay as they were.
    /// </summary>
    public void ResetForMap()
    {
        Pictures.Clear();
        EndWipe();
        FlashTone = null;
        if (_toneMode is 4 or 5) Tone = _toneTo;
        _toneMode = -1;
        _shakeMs = -1;
        ShakeOffset = Point.Zero;
        _ramp = -1;
    }

    /// <summary>Old → new in <paramref name="steps"/> steps: step i = (new·i + (steps−1−i)·old) / steps, integer maths per channel.</summary>
    static Vector4 Blend(Vector4 from, Vector4 to, int i, int steps)
    {
        static float C(float f, float t, int i, int steps) => (int)((t * i + (steps - 1 - i) * f) / steps);
        return new Vector4(C(from.X, to.X, i, steps), C(from.Y, to.Y, i, steps), C(from.Z, to.Z, i, steps), C(from.W, to.W, i, steps));
    }

    void UpdateTone()
    {
        double t = _toneMs;
        switch (_toneMode)
        {
            case 0:
                FlashTone = t < 33 ? _toneTo : null;
                if (t >= 33) _toneMode = -1;
                break;
            case 1:
                FlashTone = t < 33 || (t >= 66 && t < 132) ? _toneTo : null;
                if (t >= 132) _toneMode = -1;
                break;
            case 2:
                // eight steps, (new·i + (7−i)·old) / 8 (an arithmetic shift), then the old tone again
                if (t >= 231) { FlashTone = null; _toneMode = -1; break; }
                FlashTone = Blend(_toneFrom, _toneTo, LoopStep(t, 33, 7), 8);
                break;
            case 4:
            case 5:
                int steps = _toneMode == 4 ? 15 : 30;
                if (t >= (steps - 1) * 33) { Tone = _toneTo; _toneMode = -1; break; }
                Tone = Blend(_toneFrom, _toneTo, LoopStep(t, 33, steps - 1), steps);
                break;
        }
    }

    // ---------------- shake (FUN_00423e70) ----------------

    double _shakeMs = -1;
    int _shakeStrength;
    /// <summary>Where the whole screen is drawn while it shakes; the strip it uncovers is black.</summary>
    public Point ShakeOffset { get; private set; }
    /// <summary>Shake distances by strength (weak / medium / strong), from the exe's table at 0x4338D8.</summary>
    static readonly int[] ShakePixels = { 8, 24, 48 };

    /// <summary>The whole screen jumps left, up, right and down by 8 / 24 / 48 px, 50 ms each, then back.</summary>
    public void Shake(int strength)
    {
        _shakeStrength = ShakePixels[Math.Clamp(strength, 0, 2)];
        _shakeMs = 0;
        ShakeOffset = new Point(-_shakeStrength, 0);
    }

    // ---------------- running ----------------

    /// <summary>One of the event commands' blocking loops is running (not the main loop's own fades).</summary>
    public bool Busy => _ramp >= 0 || _toneMode >= 0 || _shakeMs >= 0 || Transition != 0;

    /// <summary>Jumps every running effect to its end (cutscene skip).</summary>
    public void Complete()
    {
        if (_ramp >= 0) { Brightness = _rampEnd; _ramp = -1; }
        if (_toneMode is 4 or 5) Tone = _toneTo;
        _toneMode = -1;
        FlashTone = null;
        _shakeMs = -1;
        ShakeOffset = Point.Zero;
        EndWipe();
    }

    void EndWipe()
    {
        Transition = 0;
        SnapshotRequested = false;
        Fill = null;
        if (_afterWipe != int.MinValue) { Brightness = _afterWipe; _afterWipe = int.MinValue; }
    }

    public void Update()
    {
        double ms = GameClock.FrameMs;
        // the main loop's fade: one step a game tick (two frames)
        if (_tickDelay > 0) _tickDelay -= ms;
        else if (_tickStep != 0 && _tickFrame++ % 2 == 0)
        {
            Brightness += _tickStep;
            if (_tickStep > 0 && Brightness > 99) { Brightness = 100; _tickStep = 0; }
            else if (_tickStep < 0 && Brightness < 1) { Brightness = 0; _tickStep = 0; }
        }
        if (_ramp >= 0)
        {
            _ramp += ms;
            int i = (int)(_ramp / 33) + 1;
            if (i >= 20) { Brightness = _rampEnd; _ramp = -1; }
            else Brightness = _rampFrom + _rampStep * i;
        }
        if (Transition != 0 && !SnapshotRequested)
        {
            _wipeMs += ms;
            if (TransitionDone) EndWipe();
        }
        if (_toneMode >= 0)
        {
            _toneMs += ms;
            UpdateTone();
        }
        if (_shakeMs >= 0)
        {
            _shakeMs += ms;
            ShakeOffset = _shakeMs switch
            {
                < 50 => new Point(-_shakeStrength, 0),
                < 100 => new Point(0, -_shakeStrength),
                < 150 => new Point(_shakeStrength, 0),
                < 200 => new Point(0, _shakeStrength),
                _ => Point.Zero,
            };
            if (_shakeMs >= 200) _shakeMs = -1;
        }
    }
}
