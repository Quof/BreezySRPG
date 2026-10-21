using Microsoft.Xna.Framework;

namespace Erumani.Runtime;

/// <summary>
/// Screen-level effects driven by event commands: fades (screen display / erase screen),
/// pictures, flash/tint and shake. Timings are provisional until compared with the original.
/// </summary>
public sealed class ScreenEffects
{
    // Fade: 0 = fully visible, 1 = fully covered by FadeColor.
    public float Fade { get; private set; }
    public Color FadeColor { get; private set; } = Color.Black;
    float _fadeFrom, _fadeTo;
    int _fadeFrames, _fadeT;

    public string? Picture { get; private set; }
    public int PicturePosition { get; private set; } = 5;
    public bool PictureTransparent { get; private set; }
    public float PictureAlpha { get; private set; }
    float _picTarget;

    public Color FlashColor { get; private set; }
    public float FlashAlpha { get; private set; }
    /// <summary>Tint in percent per channel (100 = unchanged), vividness 100 = full colour.</summary>
    public Vector4 Tone { get; private set; } = new(100, 100, 100, 100);
    Vector4 _toneFrom, _toneTo;
    int _toneFrames, _toneT;

    int _shakeFrames, _shakeStrength;
    public Point ShakeOffset { get; private set; }
    readonly Random _rng = new();

    public bool Busy => _fadeT < _fadeFrames || _toneT < _toneFrames || FlashAlpha > 0.01f || _shakeFrames > 0;

    /// <summary>Erase screen (to colour) or screen display (from colour). Effect variants are drawn as fades for now.</summary>
    public void StartFade(bool toCovered, int color, int effect)
    {
        // Erased-to-black screens show (16,16,0) in the original (0話 screenshot); white is unverified.
        FadeColor = color == 1 ? Color.White : new Color(16, 16, 0);
        _fadeFrom = Fade;
        _fadeTo = toCovered ? 1 : 0;
        _fadeFrames = 30;
        _fadeT = 0;
    }

    public void ShowPicture(string file, int position, int method, bool transparent)
    {
        Picture = file;
        PicturePosition = position;
        PictureTransparent = transparent;
        PictureAlpha = method == 0 ? 1 : 0;
        _picTarget = 1;
    }

    public void ErasePicture(int method)
    {
        _picTarget = 0;
        if (method == 0) { PictureAlpha = 0; Picture = null; }
    }

    /// <summary>Flash/tint: modes 0-2 = Flash 1/2/3, 3-5 = fast/medium/slow tone change.</summary>
    public void FlashTint(int mode, int r, int g, int b, int vivid)
    {
        if (mode <= 2)
        {
            FlashColor = new Color(Math.Min(255, r * 255 / 200), Math.Min(255, g * 255 / 200), Math.Min(255, b * 255 / 200));
            FlashAlpha = 0.6f + 0.2f * mode;
            return;
        }
        _toneFrom = Tone;
        _toneTo = new Vector4(r, g, b, vivid);
        _toneFrames = mode switch { 3 => 15, 4 => 45, _ => 90 };
        _toneT = 0;
    }

    public void Shake(int strength)
    {
        _shakeStrength = 2 + strength * 3;
        _shakeFrames = 40;
    }

    /// <summary>Jumps every running effect to its end (cutscene skip).</summary>
    public void Complete()
    {
        if (_fadeT < _fadeFrames) { Fade = _fadeTo; _fadeT = _fadeFrames; }
        if (_toneT < _toneFrames) { Tone = _toneTo; _toneT = _toneFrames; }
        FlashAlpha = 0;
        _shakeFrames = 0;
        ShakeOffset = Point.Zero;
        PictureAlpha = _picTarget;
        if (PictureAlpha == 0) Picture = null;
    }

    public void Update()
    {
        if (_fadeT < _fadeFrames) Fade = MathHelper.Lerp(_fadeFrom, _fadeTo, ++_fadeT / (float)_fadeFrames);
        if (_toneT < _toneFrames) Tone = Vector4.Lerp(_toneFrom, _toneTo, ++_toneT / (float)_toneFrames);
        FlashAlpha = Math.Max(0, FlashAlpha - 0.04f);
        if (PictureAlpha < _picTarget) PictureAlpha = Math.Min(_picTarget, PictureAlpha + 0.05f);
        else if (PictureAlpha > _picTarget) { PictureAlpha = Math.Max(0, PictureAlpha - 0.05f); if (PictureAlpha == 0) Picture = null; }
        if (_shakeFrames > 0)
        {
            _shakeFrames--;
            ShakeOffset = _shakeFrames == 0 ? Point.Zero
                : new Point(_rng.Next(-_shakeStrength, _shakeStrength + 1), _rng.Next(-_shakeStrength / 2, _shakeStrength / 2 + 1));
        }
    }
}
