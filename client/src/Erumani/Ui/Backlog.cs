using Erumani.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Erumani.Ui;

/// <summary>
/// Text backlog (modern): every message shown since the title, newest at the bottom. Mouse wheel up (or the
/// backlog key) opens it and scrolls back; wheel down scrolls forward and closes it past the newest line;
/// Cancel or a click closes it.
/// </summary>
public sealed class Backlog(Ui ui)
{
    readonly List<string[]> _entries = new();
    int _scroll;   // lines scrolled up from the bottom
    public bool Open { get; private set; }

    const int Lines = 25, LineHeight = 16;

    public void Add(MessageView m)
    {
        _entries.Add(m.Lines.Where(l => l.Length > 0).ToArray());
        if (_entries.Count > 300) _entries.RemoveAt(0);
    }

    public void Clear() { _entries.Clear(); Open = false; }

    List<string> Flat()
    {
        var lines = new List<string>();
        foreach (var e in _entries) { lines.AddRange(e); lines.Add(""); }
        if (lines.Count > 0) lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    int MaxScroll => Math.Max(0, Flat().Count - Lines);

    /// <summary>Wheel notches: positive = up (back in time).</summary>
    public void Scroll(int notches)
    {
        if (_entries.Count == 0) return;
        if (!Open)
        {
            if (notches <= 0) return;
            Open = true;
            _scroll = 0;
            notches--;
        }
        int before = _scroll;
        _scroll = Math.Clamp(_scroll + notches * 3, 0, MaxScroll);
        if (notches < 0 && before == 0) Open = false;
    }

    public void Toggle() { if (Open) Open = false; else Scroll(1); }

    public void Close() => Open = false;

    public void Draw(SpriteBatch sb)
    {
        if (!Open) return;
        ui.Fill(sb, new Rectangle(0, 0, 640, 480), Color.Black * 0.5f);
        var r = new Rectangle(48, 24, 544, Lines * LineHeight + 24);
        ui.Panel(sb, r);
        var lines = Flat();
        int first = Math.Max(0, lines.Count - Lines - _scroll);
        for (int k = 0; k < Lines && first + k < lines.Count; k++)
            if (ui.SmallText.Get(lines[first + k]) is { } t) sb.Draw(t, new Vector2(r.X + 16, r.Y + 12 + k * LineHeight), Ui.PanelText);
        // scroll position
        if (lines.Count > Lines)
        {
            int track = r.Height - 16, h = Math.Max(16, track * Lines / lines.Count);
            int y = r.Y + 8 + (track - h) * (MaxScroll - _scroll) / Math.Max(1, MaxScroll);
            ui.Fill(sb, new Rectangle(r.Right - 10, y, 4, h), Ui.PanelText);
        }
    }
}
