using Microsoft.Xna.Framework.Audio;
using Srpg95.Data;

namespace Erumani.Runtime;

/// <summary>
/// Sound effects from the original WAVs (all PCM mono 8/16-bit) and looping BGM.
/// BGM .mid files go through the Windows MCI sequencer (the Microsoft GS Wavetable synth,
/// i.e. what the original runtime used); a SoundFont synth is a later option.
/// </summary>
public sealed class Audio(GameFolder game) : IDisposable
{
    readonly Dictionary<string, SoundEffect?> _cache = new(StringComparer.OrdinalIgnoreCase);
    public string? CurrentBgm { get; private set; }
    public float Volume { get; set; } = 0.8f;
    public bool Muted { get; set; }
    bool _bgmOpen;
    int _pollFrames;

    [System.Runtime.InteropServices.DllImport("winmm.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int mciSendStringW(string command, System.Text.StringBuilder? ret, int retLen, IntPtr callback);

    static string Mci(string cmd)
    {
        var sb = new System.Text.StringBuilder(128);
        try { mciSendStringW(cmd, sb, sb.Capacity, IntPtr.Zero); } catch (DllNotFoundException) { }
        return sb.ToString();
    }

    public void PlayBgm(string file)
    {
        string? next = file == "(OFF)" || file.Length == 0 ? null : file;
        if (string.Equals(next, CurrentBgm, StringComparison.OrdinalIgnoreCase) && _bgmOpen) return;
        StopBgm();
        CurrentBgm = next;
        if (next is null || Muted) return;
        var path = game.Find("BGM/" + next);
        if (path is null) return;
        string type = path.EndsWith(".mid", StringComparison.OrdinalIgnoreCase) ? "sequencer" : "waveaudio";
        Mci($"open \"{path}\" type {type} alias erumani_bgm");
        Mci("play erumani_bgm from 0");
        _bgmOpen = true;
    }

    void StopBgm()
    {
        if (!_bgmOpen) return;
        Mci("stop erumani_bgm");
        Mci("close erumani_bgm");
        _bgmOpen = false;
    }

    /// <summary>Call once per frame: restarts the BGM when it reaches the end (looping).</summary>
    public void Update()
    {
        if (!_bgmOpen || ++_pollFrames < 30) return;
        _pollFrames = 0;
        if (Mci("status erumani_bgm mode") == "stopped") Mci("play erumani_bgm from 0");
    }

    public void PlaySe(string file)
    {
        if (Muted) return;
        if (!_cache.TryGetValue(file, out var se))
            _cache[file] = se = Load(game.Find("EFS/" + file));
        se?.Play(Volume, 0, 0);
    }

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
