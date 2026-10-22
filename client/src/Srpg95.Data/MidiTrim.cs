namespace Srpg95.Data;

/// <summary>
/// Silence at a MIDI file's start and end (modern: the original plays the file as it is). A looping track with
/// seconds of nothing before its first note or after its last one leaves a gap at every repeat (ＯtoＺ's legend.mid
/// ends in 26 s of silence); this cuts it all by default: the track starts on its first note and ends on its last note's
/// note-off, the synthesizer's fading tail of it cut by the repeat (<c>maxSilence</c> keeps up to that many seconds of the two together, the start's first). Everything before
/// the cut (programs, controllers, tempo, loop markers) moves to the new start in its order; nothing after the new end
/// is kept but each track's end.
/// </summary>
public static class MidiTrim
{
    public const double MaxSilenceSeconds = 0;

    sealed record Event(long Tick, byte[] Bytes, bool Meta, int MetaType);

    /// <summary>The file with its silence trimmed, or <paramref name="smf"/> itself when there's nothing to trim or it can't be read.</summary>
    public static byte[] Trim(byte[] smf, double maxSilence = MaxSilenceSeconds)
    {
        try { return TrimOrNull(smf, maxSilence) ?? smf; }
        catch (Exception) { return smf; }   // anything unusual: played as it is
    }

    /// <summary>A file's silence before its first note and after its last one, in seconds, and its notes; null if it can't be read.</summary>
    public static (double Lead, double Tail, int Notes)? Silence(byte[] smf)
    {
        try { return Read(smf) is { } r ? (r.Seconds(r.FirstOn), r.Seconds(r.Last) - r.Seconds(r.LastSound), r.Notes) : null; }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// The RPG Maker loop marker (the first controller 111) in seconds from the start, when there's one before the last
    /// note: a marker at or after it (some fanfares end on one) loops nothing and doesn't count. Null for none.
    /// </summary>
    public static double? LoopStart(byte[] smf)
    {
        try
        {
            if (Read(smf) is not { } r) return null;
            long? mark = r.Tracks.SelectMany(t => t).Where(e => !e.Meta && e.Bytes.Length > 2 && (e.Bytes[0] & 0xF0) == 0xB0 && e.Bytes[1] == 111)
                .Select(e => (long?)e.Tick).Min();
            long lastOn = r.Tracks.SelectMany(t => t).Where(e => !e.Meta && e.Bytes.Length > 2 && (e.Bytes[0] & 0xF0) == 0x90 && e.Bytes[2] > 0)
                .Select(e => e.Tick).DefaultIfEmpty(0).Max();
            return mark is { } m && m < lastOn ? r.Seconds(m) : null;
        }
        catch (Exception) { return null; }
    }

    sealed record Parsed(int HeaderLen, List<List<Event>> Tracks, long FirstOn, long LastSound, long Last, int Notes,
        Func<long, double> Seconds, Func<double, long> TickAt);

    static byte[]? TrimOrNull(byte[] b, double maxSilence)
    {
        if (Read(b) is not { } r) return null;
        var (headerLen, tracks, firstOn, lastSound, last) = (r.HeaderLen, r.Tracks, r.FirstOn, r.LastSound, r.Last);
        var (Seconds, TickAt) = (r.Seconds, r.TickAt);
        double lead = Seconds(firstOn), tail = Seconds(last) - Seconds(lastSound);
        if (lead + tail <= maxSilence + 0.001) return null;
        double keepLead = Math.Min(lead, maxSilence), keepTail = Math.Min(tail, maxSilence - keepLead);
        long start = Math.Clamp(TickAt(lead - keepLead), 0, firstOn);
        long end = Math.Clamp(TickAt(Seconds(lastSound) + keepTail), lastSound, last);
        return Write(b, headerLen, tracks, start, end);
    }

    static Parsed? Read(byte[] b)
    {
        if (b.Length < 14 || b[0] != 'M' || b[1] != 'T' || b[2] != 'h' || b[3] != 'd') return null;
        int headerLen = BE32(b, 4);
        int division = (b[12] << 8) | b[13];
        if ((division & 0x8000) != 0 || division == 0) return null;   // SMPTE time: left alone
        var tracks = new List<List<Event>>();
        int pos = 8 + headerLen;
        while (pos + 8 <= b.Length)
        {
            int len = BE32(b, pos + 4);
            if (b[pos] == 'M' && b[pos + 1] == 'T' && b[pos + 2] == 'r' && b[pos + 3] == 'k') tracks.Add(ReadTrack(b, pos + 8, Math.Min(b.Length, pos + 8 + len)));
            pos += 8 + len;
        }
        if (tracks.Count == 0) return null;

        long firstOn = long.MaxValue, lastSound = -1, last = 0;
        int notes = 0;
        var tempos = new SortedDictionary<long, int>();
        foreach (var e in tracks.SelectMany(t => t))
        {
            last = Math.Max(last, e.Tick);
            if (e.Meta) { if (e.MetaType == 0x51 && e.Bytes.Length >= 6) tempos[e.Tick] = (e.Bytes[^3] << 16) | (e.Bytes[^2] << 8) | e.Bytes[^1]; continue; }
            int status = e.Bytes[0] & 0xF0;
            bool on = status == 0x90 && e.Bytes.Length > 2 && e.Bytes[2] > 0;
            bool off = status == 0x80 || (status == 0x90 && e.Bytes.Length > 2 && e.Bytes[2] == 0);
            if (on) { firstOn = Math.Min(firstOn, e.Tick); notes++; }
            if (on || off) lastSound = Math.Max(lastSound, e.Tick);
        }
        if (firstOn == long.MaxValue) return null;   // no notes at all

        var map = tempos.ToList();
        double Seconds(long tick)
        {
            double s = 0; long at = 0; int us = 500000;
            foreach (var (t, v) in map)
            {
                if (t >= tick) break;
                s += (t - at) * (double)us / division / 1e6;
                (at, us) = (t, v);
            }
            return s + (tick - at) * (double)us / division / 1e6;
        }
        long TickAt(double seconds)
        {
            double s = 0; long at = 0; int us = 500000;
            foreach (var (t, v) in map)
            {
                double next = s + (t - at) * (double)us / division / 1e6;
                if (next >= seconds) break;
                (s, at, us) = (next, t, v);
            }
            return at + (long)Math.Round((seconds - s) * 1e6 * division / us);
        }
        return new Parsed(headerLen, tracks, firstOn, lastSound, last, notes, Seconds, TickAt);
    }

    /// <summary>The file again: the header, then each track from <paramref name="start"/> to <paramref name="end"/>.</summary>
    static byte[] Write(byte[] b, int headerLen, List<List<Event>> tracks, long start, long end)
    {
        using var o = new MemoryStream();
        o.Write(b, 0, 8 + headerLen);
        o.Position = 10;   // the track count: the tracks written
        o.Write(new[] { (byte)(tracks.Count >> 8), (byte)tracks.Count });
        o.Position = o.Length;
        foreach (var t in tracks)
        {
            using var body = new MemoryStream();
            long prev = 0;
            foreach (var e in t)
            {
                if (e.Meta && e.MetaType == 0x2F) continue;   // the end: written once, below
                if (e.Tick > end) continue;
                long tick = Math.Max(e.Tick, start) - start;
                WriteVlq(body, tick - prev);
                body.Write(e.Bytes);
                prev = tick;
            }
            WriteVlq(body, end - start - prev);
            body.Write(new byte[] { 0xFF, 0x2F, 0x00 });
            o.Write("MTrk"u8);
            int n = (int)body.Length;
            o.Write(new[] { (byte)(n >> 24), (byte)(n >> 16), (byte)(n >> 8), (byte)n });
            body.Position = 0;
            body.CopyTo(o);
        }
        return o.ToArray();
    }

    /// <summary>A track's events in order, each with its absolute tick and full bytes (running status written out).</summary>
    static List<Event> ReadTrack(byte[] b, int p, int end)
    {
        var events = new List<Event>();
        long tick = 0;
        byte running = 0;
        while (p < end)
        {
            tick += ReadVlq(b, ref p);
            int from = p;
            byte s = b[p];
            if (s >= 0x80) p++; else s = running;
            if (s == 0xFF)
            {
                int type = b[p++];
                int len = (int)ReadVlq(b, ref p);
                p += len;
                events.Add(new Event(tick, b[from..p], true, type));
            }
            else if (s is 0xF0 or 0xF7)
            {
                int len = (int)ReadVlq(b, ref p);
                p += len;
                events.Add(new Event(tick, b[from..p], false, 0));
            }
            else
            {
                if (s < 0x80) throw new InvalidDataException("data without a status");
                running = s;
                int n = (s & 0xF0) is 0xC0 or 0xD0 ? 1 : 2;
                var bytes = new byte[1 + n];
                bytes[0] = s;
                Array.Copy(b, p, bytes, 1, n);
                p += n;
                events.Add(new Event(tick, bytes, false, 0));
            }
        }
        return events;
    }

    static long ReadVlq(byte[] b, ref int p)
    {
        long v = 0;
        while (true)
        {
            byte c = b[p++];
            v = (v << 7) | (uint)(c & 0x7F);
            if (c < 0x80) return v;
        }
    }

    static void WriteVlq(Stream s, long v)
    {
        Span<byte> buf = stackalloc byte[10];
        int i = buf.Length;
        buf[--i] = (byte)(v & 0x7F);
        while ((v >>= 7) > 0) buf[--i] = (byte)(0x80 | (v & 0x7F));
        s.Write(buf[i..]);
    }

    static int BE32(byte[] b, int p) => (b[p] << 24) | (b[p + 1] << 16) | (b[p + 2] << 8) | b[p + 3];
}
