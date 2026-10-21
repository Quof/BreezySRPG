using Microsoft.Xna.Framework;

namespace Erumani.Runtime;

/// <summary>
/// Screen-level effects driven by event commands, timed as SRPGEXEC.EXE runs them (FORMAT_NOTES §16): screen display /
/// erase (FUN_00422ecc / FUN_004231c0), pictures (FUN_004233f0 / FUN_004237c4), flash and tone (FUN_00423914) and
/// shake (FUN_00423e70). The original's loops block the script while they run; <see cref="Busy"/> stands for that.
/// </summary>
public sealed class ScreenEffects
{
    // Fade: 0 = fully visible, 1 = fully covered by FadeColor.
    public float Fade { get; private set; }
    public Color FadeColor { get; private set; } = new(16, 16, 0);
    float _fadeFrom, _fadeTo;
    int _fadeFrames, _fadeT;

    /// <summary>Pictures shown, oldest first: a new one is drawn over the ones still up (they share one surface until erased).</summary>
    public List<(string File, int Position, bool Transparent)> Pictures { get; } = new();
    public string? Picture => Pictures.Count > 0 ? Pictures[^1].File : null;

    /// <summary>Screen tone in percent per channel (100 = unchanged) and vividness (100 = full colour).</summary>
    public Vector4 Tone { get; private set; } = new(100, 100, 100, 100);
    /// <summary>The tone a flash shows for a moment, on top of <see cref="Tone"/>; null when no flash is on.</summary>
    public Vector4? FlashTone { get; private set; }
    Vector4 _toneFrom, _toneTo, _flashTo;
    int _toneFrames, _toneT, _flashMode = -1, _flashT;

    int _shakeT = -1, _shakeStrength;
    public Point ShakeOffset { get; private set; }
    /// <summary>Shake distances by strength (weak / medium / strong), from the exe's table at 0x4338D8.</summary>
    static readonly int[] ShakePixels = { 8, 24, 48 };

    // ---------------- transitions (FUN_00422a00) ----------------

    /// <summary>The running wipe (1 random blocks, 2 horizontal lines, 3 vertical lines, 4 horizontal bands, 5 vertical bands), or 0.</summary>
    public int Transition { get; private set; }
    public int TransitionFrame { get; private set; }
    /// <summary>The old screen to wipe away: a snapshot of the last frame drawn, or a solid colour (screen display starts from one).</summary>
    public Color? TransitionFrom { get; private set; }
    /// <summary>The renderer must snapshot the last frame before drawing the next one.</summary>
    public bool SnapshotRequested { get; private set; }
    public void SnapshotTaken() => SnapshotRequested = false;

    /// <summary>Milliseconds per tick and ticks, per wipe: 20 × 33 ms, bands 30 × 22 ms and 40 × 16 ms.</summary>
    static (int Ms, int Ticks) WipeTiming(int effect) => effect switch { 4 => (22, 30), 5 => (16, 40), _ => (33, 20) };

    /// <summary>The wipe's tick at the current frame (the original reveals tick 0's part straight away).</summary>
    public int TransitionTick => Transition == 0 ? 0 : (int)(TransitionFrame * 1000 / 60 / WipeTiming(Transition).Ms);
    public bool TransitionDone => Transition == 0 || TransitionTick >= WipeTiming(Transition).Ticks;

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
        TransitionFrame = 0;
        TransitionFrom = from;
        SnapshotRequested = from is null;
    }

    public bool Busy => _fadeT < _fadeFrames || _toneT < _toneFrames || _flashMode >= 0 || _shakeT >= 0 || Transition != 0;

    /// <summary>
    /// Erase screen (to colour) or screen display (from colour): effect 6 is a brightness fade of 20 ticks (0.67 s);
    /// 0 is instant; 1–5 wipe (display starts from a screen filled with the colour).
    /// </summary>
    public void StartFade(bool toCovered, int color, int effect)
    {
        // Erased-to-black screens show (16,16,0) in the original (0話 screenshot); white is unverified.
        FadeColor = color == 1 ? Color.White : new Color(16, 16, 0);
        _fadeTo = toCovered ? 1 : 0;
        if (effect == 6)
        {
            _fadeFrom = Fade;
            _fadeFrames = 40;
            _fadeT = 0;
            return;
        }
        _fadeFrames = _fadeT = 0;
        Fade = _fadeTo;
        StartWipe(effect, toCovered ? null : FadeColor);
    }

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

    /// <summary>
    /// Flash / tone (FUN_00423914), the screen's tone in percent: Flash 1 shows the colour for one tick, Flash 2 twice
    /// (1 tick, a 1-tick gap, 2 ticks), Flash 3 brings the tone towards it over 8 ticks, then snaps back. Tone
    /// changes: mode 3 at once, 4 over 15 ticks (0.5 s), 5 over 30 ticks (1 s); the tone stays.
    /// </summary>
    public void FlashTint(int mode, int r, int g, int b, int vivid)
    {
        var target = new Vector4(r, g, b, vivid);
        if (mode <= 2)
        {
            _flashMode = mode;
            _flashTo = target;
            _flashT = 0;
            FlashTone = mode == 2 ? null : target;
            return;
        }
        _toneFrom = Tone;
        _toneTo = target;
        _toneFrames = mode switch { 3 => 0, 4 => 30, _ => 60 };
        _toneT = 0;
        if (_toneFrames == 0) Tone = target;
    }

    /// <summary>The screen jumps left, up, right and down by 8 / 24 / 48 px, 50 ms each, then back (FUN_00423e70).</summary>
    public void Shake(int strength)
    {
        _shakeStrength = ShakePixels[Math.Clamp(strength, 0, 2)];
        _shakeT = 0;
        ShakeOffset = new Point(-_shakeStrength, 0);
    }

    /// <summary>Jumps every running effect to its end (cutscene skip).</summary>
    public void Complete()
    {
        if (_fadeT < _fadeFrames) { Fade = _fadeTo; _fadeT = _fadeFrames; }
        if (_toneT < _toneFrames) { Tone = _toneTo; _toneT = _toneFrames; }
        _flashMode = -1;
        FlashTone = null;
        _shakeT = -1;
        ShakeOffset = Point.Zero;
        Transition = 0;
        SnapshotRequested = false;
    }

    public void Update()
    {
        if (_fadeT < _fadeFrames) Fade = MathHelper.Lerp(_fadeFrom, _fadeTo, ++_fadeT / (float)_fadeFrames);
        if (_toneT < _toneFrames) Tone = Vector4.Lerp(_toneFrom, _toneTo, ++_toneT / (float)_toneFrames);
        if (Transition != 0 && !SnapshotRequested && ++TransitionFrame > 0 && TransitionDone) Transition = 0;
        if (_flashMode >= 0)
        {
            _flashT++;
            int tick = _flashT / 2;   // 30 Hz ticks
            switch (_flashMode)
            {
                case 0:
                    if (tick >= 1) EndFlash();
                    break;
                case 1:
                    FlashTone = tick switch { 0 => _flashTo, 1 => null, 2 or 3 => _flashTo, _ => null };
                    if (tick >= 4) EndFlash();
                    break;
                default:
                    if (tick >= 8) { EndFlash(); break; }
                    FlashTone = (Tone * (7 - tick) + _flashTo * tick) / 8;
                    break;
            }
        }
        if (_shakeT >= 0)
        {
            int step = ++_shakeT / 3;   // 50 ms each
            ShakeOffset = step switch
            {
                0 => new Point(-_shakeStrength, 0),
                1 => new Point(0, -_shakeStrength),
                2 => new Point(_shakeStrength, 0),
                3 => new Point(0, _shakeStrength),
                _ => Point.Zero,
            };
            if (step >= 4) _shakeT = -1;
        }
    }

    void EndFlash()
    {
        _flashMode = -1;
        FlashTone = null;
    }
}
