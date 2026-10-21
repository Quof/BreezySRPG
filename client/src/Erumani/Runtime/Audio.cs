using MeltySynth;
using Microsoft.Xna.Framework.Audio;
using Srpg95.Data;

namespace Erumani.Runtime;

/// <summary>
/// Sound effects from the original WAVs (all PCM mono 8/16-bit) and looping BGM. MIDI BGM plays either through
/// a SoundFont synthesizer (MeltySynth, the modern option; consistent on every PC) or the Windows MCI sequencer
/// (the Microsoft GS Wavetable synth, what the original runtime used). WAV BGM (some games loop a sound effect as their
/// music) loops seamlessly as a sound of its own; one that isn't plain PCM goes through MCI.
/// System sounds the runtime plays itself, from the game folder: FUN_00409428's CLICK.WAV (0, on confirm and when
/// advancing text), CANCEL.WAV (1), BUZZER.WAV (2, an unavailable choice) and DOWN.WAV (3, a unit falling), and
/// LEVELUP.WAV when the level-up panel opens (FUN_00410a88) or a class changes (FUN_0040be00).
/// </summary>
public sealed class Audio(GameFolder game) : IDisposable
{
    readonly Dictionary<string, SoundEffect?> _cache = new(StringComparer.OrdinalIgnoreCase);
    public string? CurrentBgm { get; private set; }
    public float SoundVolume { get; set; } = 0.8f;
    public bool Muted { get; set; }
    /// <summary>The CLICK "thunk" (confirming in menus, on the map, advancing text): the one sound the player can turn off.</summary>
    public bool MessageSound { get; set; } = true;
    /// <summary>The thunk when a message is clicked away (the "gameplay only" confirm sound leaves it out).</summary>
    public bool AdvanceSound { get; set; } = true;
    bool _bgmOpen;
    int _pollFrames;

    float _musicVolume = 0.8f;
    public float MusicVolume
    {
        get => _musicVolume;
        set
        {
            _musicVolume = value;
            if (_stream is not null) _stream.Volume = Math.Clamp(value, 0, 1);
            if (_wavLoop is not null) _wavLoop.Volume = Math.Clamp(value, 0, 1);
            if (_bgmOpen) Mci($"setaudio erumani_bgm volume to {(int)(value * 1000)}");
        }
    }

    // ---------------- SoundFont synth ----------------

    const int Rate = 44100, Block = 2048;
    Synthesizer? _synth;
    MidiFileSequencer? _sequencer;
    DynamicSoundEffectInstance? _stream;
    string? _soundFont;
    readonly float[] _left = new float[Block], _right = new float[Block];
    readonly byte[] _pcm = new byte[Block * 4];

    /// <summary>
    /// Use this SoundFont for MIDI: a .sf2 path, <see cref="Config.Settings.BuiltIn"/> for the one embedded in the
    /// executable, or null for Windows MIDI. Restarts the current track with it.
    /// </summary>
    public void SetSoundFont(string? path)
    {
        if (string.Equals(path, _soundFont, StringComparison.OrdinalIgnoreCase)) return;
        _soundFont = path;
        StopSynth();
        _synth = null;
        _sequencer = null;
        if (path is not null)
        {
            try
            {
                if (path == Config.Settings.BuiltIn)
                {
                    using var s = typeof(Audio).Assembly.GetManifestResourceStream("GeneralUser-GS.sf2")
                        ?? throw new FileNotFoundException("built-in SoundFont missing");
                    _synth = new Synthesizer(new SoundFont(s), Rate);
                }
                else _synth = new Synthesizer(path, Rate);
                _sequencer = new MidiFileSequencer(_synth);
            }
            catch (Exception) { _synth = null; _sequencer = null; _soundFont = null; }   // unreadable SoundFont: Windows MIDI
        }
        var current = CurrentBgm;
        CurrentBgm = null;
        StopBgm();
        if (current is not null) PlayBgm(current);
    }

    void StopSynth()
    {
        _sequencer?.Stop();
        _stream?.Stop();
        _stream?.Dispose();
        _stream = null;
    }

    void FeedSynth()
    {
        if (_stream is null || _sequencer is null) return;
        while (_stream.PendingBufferCount < 3)
        {
            _sequencer.Render(_left, _right);
            for (int i = 0; i < Block; i++)
            {
                short l = (short)Math.Clamp(_left[i] * 32767f, short.MinValue, short.MaxValue);
                short r = (short)Math.Clamp(_right[i] * 32767f, short.MinValue, short.MaxValue);
                _pcm[i * 4] = (byte)l; _pcm[i * 4 + 1] = (byte)(l >> 8);
                _pcm[i * 4 + 2] = (byte)r; _pcm[i * 4 + 3] = (byte)(r >> 8);
            }
            _stream.SubmitBuffer(_pcm);
        }
    }

    // ---------------- MCI (Windows) ----------------

    [System.Runtime.InteropServices.DllImport("winmm.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int mciSendStringW(string command, System.Text.StringBuilder? ret, int retLen, IntPtr callback);

    static string Mci(string cmd)
    {
        var sb = new System.Text.StringBuilder(128);
        try { mciSendStringW(cmd, sb, sb.Capacity, IntPtr.Zero); } catch (DllNotFoundException) { }
        return sb.ToString();
    }

    // HarmonyGetMusicLooping: whether the current music has played to its end at least once (COMPLETE!! waits for it),
    // timed on the game's clock (each frame adds its length), so a scripted run at full speed waits as many frames as play
    double _bgmLengthMs, _bgmPlayedMs;
    public bool BgmLooped => CurrentBgm is not null && _bgmLengthMs > 0 && _bgmPlayedMs >= _bgmLengthMs;

    public void PlayBgm(string file)
    {
        string? next = file == "(OFF)" || file.Length == 0 ? null : file;
        if (string.Equals(next, CurrentBgm, StringComparison.OrdinalIgnoreCase) && (_bgmOpen || _stream is not null || _wavLoop is not null)) return;
        StopBgm();
        CurrentBgm = next;
        _bgmLengthMs = 0;
        if (next is null) return;
        var path = game.Find("BGM/" + next);
        if (path is null) return;
        _bgmLengthMs = LengthMs(path);   // also while muted, so scripted runs keep the same timing
        _bgmPlayedMs = 0;
        if (Muted) return;
        bool midi = path.EndsWith(".mid", StringComparison.OrdinalIgnoreCase);
        if (midi && _sequencer is not null)
        {
            try
            {
                _sequencer.Play(new MidiFile(path), loop: true);
                _stream = new DynamicSoundEffectInstance(Rate, AudioChannels.Stereo) { Volume = Math.Clamp(_musicVolume, 0, 1) };
                FeedSynth();
                _stream.Play();
                return;
            }
            catch (Exception) { StopSynth(); }   // fall back to MCI
        }
        if (!midi)
        {
            // MCI only notices the end when polled, which left a gap before each repeat: a looping sound has none
            try { _wavBgm = Load(path); } catch (Exception) { _wavBgm = null; }
            if (_wavBgm is not null)
            {
                _wavLoop = _wavBgm.CreateInstance();
                _wavLoop.IsLooped = true;
                _wavLoop.Volume = Math.Clamp(_musicVolume, 0, 1);
                _wavLoop.Play();
                return;
            }
        }
        Mci($"open \"{path}\" type {(midi ? "sequencer" : "waveaudio")} alias erumani_bgm");
        Mci($"setaudio erumani_bgm volume to {(int)(_musicVolume * 1000)}");
        Mci("play erumani_bgm from 0");
        _bgmOpen = true;
    }

    /// <summary>A BGM file's playing time: a MIDI file's last event, a WAV's data at its byte rate.</summary>
    static double LengthMs(string path)
    {
        try
        {
            if (path.EndsWith(".mid", StringComparison.OrdinalIgnoreCase)) return new MidiFile(path).Length.TotalMilliseconds;
            var b = File.ReadAllBytes(path);
            int pos = 12, byteRate = 0;
            while (pos + 8 <= b.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(b, pos, 4);
                int len = Bin.I32(b, pos + 4);
                if (id == "fmt ") byteRate = Bin.I32(b, pos + 16);
                else if (id == "data" && byteRate > 0) return len * 1000.0 / byteRate;
                pos += 8 + len + (len & 1);
            }
        }
        catch (Exception) { }   // an unreadable file just has no known length
        return 0;
    }

    SoundEffect? _wavBgm;
    SoundEffectInstance? _wavLoop;

    void StopBgm()
    {
        StopSynth();
        _wavLoop?.Stop();
        _wavLoop?.Dispose();
        _wavBgm?.Dispose();
        (_wavLoop, _wavBgm) = (null, null);
        if (!_bgmOpen) return;
        Mci("stop erumani_bgm");
        Mci("close erumani_bgm");
        _bgmOpen = false;
    }

    /// <summary>Call every frame: keeps the synth fed and restarts MCI BGM when it reaches the end (looping).</summary>
    public void Update()
    {
        _bgmPlayedMs += GameClock.FrameMs;
        FeedSynth();
        if (!_bgmOpen || ++_pollFrames < 30) return;
        _pollFrames = 0;
        if (Mci("status erumani_bgm mode") == "stopped") Mci("play erumani_bgm from 0");
    }

    // ---------------- sound effects ----------------

    public void PlaySe(string file)
    {
        if (Muted || file.Length == 0) return;
        if (!_cache.TryGetValue(file, out var se))
        {
            try { se = Load(game.Find("EFS/" + file)); }
            catch (Exception) { se = null; }   // a damaged or unusual WAV stays silent
            _cache[file] = se;
        }
        se?.Play(Math.Clamp(SoundVolume, 0, 1), 0, 0);
    }

    public void Click() { if (MessageSound) { PlaySe("CLICK.WAV"); _clickAt = Environment.TickCount64; } }

    long _clickAt = long.MinValue / 2;

    /// <summary>
    /// CLICK.WAV when a game is saved or loaded (modern: the original plays only the pick's click), whatever the confirm
    /// sound option says — once, when the pick's own click has just played.
    /// </summary>
    public void SaveLoadSound()
    {
        if (Environment.TickCount64 - _clickAt < 100) return;
        PlaySe("CLICK.WAV");
        _clickAt = Environment.TickCount64;
    }
    public void CancelSound() => PlaySe("CANCEL.WAV");
    public void Buzzer() => PlaySe("BUZZER.WAV");
    /// <summary>The text-advance thunk (CLICK.WAV), unless turned off in the options.</summary>
    public void MessageAdvance() { if (AdvanceSound) Click(); }

    /// <summary>Minimal RIFF/WAVE PCM loader; 8-bit unsigned samples are widened to 16-bit.</summary>
    static SoundEffect? Load(string? path)
    {
        if (path is null) return null;
        var b = File.ReadAllBytes(path);
        if (b.Length < 12 || b[0] != 'R' || b[8] != 'W') return null;
        int pos = 12, channels = 1, rate = 22050, bits = 16;
        byte[]? pcm = null;
        while (pos + 8 <= b.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(b, pos, 4);
            int len = Math.Min(Bin.I32(b, pos + 4), b.Length - pos - 8);
            if (id == "fmt ")
            {
                if (Bin.U16(b, pos + 8) != 1) return null;      // PCM only
                channels = Bin.U16(b, pos + 10);
                rate = Bin.I32(b, pos + 12);
                bits = Bin.U16(b, pos + 22);
            }
            else if (id == "data") pcm = b.AsSpan(pos + 8, len).ToArray();
            pos += 8 + len + (len & 1);
        }
        if (pcm is null) return null;
        if (bits == 8)
        {
            var w = new byte[pcm.Length * 2];
            for (int i = 0; i < pcm.Length; i++)
            {
                short s = (short)((pcm[i] - 128) << 8);
                w[i * 2] = (byte)s;
                w[i * 2 + 1] = (byte)(s >> 8);
            }
            pcm = w;
        }
        int frame = 2 * channels;
        return new SoundEffect(pcm, 0, pcm.Length - pcm.Length % frame, rate,
            channels == 2 ? AudioChannels.Stereo : AudioChannels.Mono, 0, 0);
    }

    public void Dispose()
    {
        StopBgm();
        foreach (var s in _cache.Values) s?.Dispose();
    }
}
