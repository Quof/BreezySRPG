using BreezySRPG95.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace BreezySRPG95.Ui;

/// <summary>
/// Text backlog (modern): every message shown since the title, newest at the bottom. Mouse wheel up (or the
/// backlog key) opens it and scrolls back; wheel down scrolls forward and closes it past the newest line;
/// Cancel or a click closes it. The gilded skin lays it out by message: each one's text on the right and its
/// speaker's name box at its bottom left, a gold divider between them, scrolling a message at a time.
/// </summary>
public sealed class Backlog(Ui ui)
{
    readonly List<string[]> _entries = new();
    int _scroll;   // lines (gilded: messages) scrolled up from the bottom
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

    int MaxScroll => Ui.Gilded ? GildedMaxScroll() : Math.Max(0, Flat().Count - Lines);

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
        _scroll = Math.Clamp(_scroll + notches * (Ui.Gilded ? 1 : 3), 0, MaxScroll);
        if (notches < 0 && before == 0) Open = false;
    }

    public void Toggle() { if (Open) Open = false; else Scroll(1); }

    public void Close() => Open = false;

    public void Draw(SpriteBatch sb)
    {
        if (!Open) return;
        if (Ui.Gilded) { DrawGilded(sb); return; }
        ui.Fill(sb, new Rectangle(0, 0, 640, 480), Color.Black * 0.5f);
        var r = new Rectangle(48, 24, 544, Lines * LineHeight + 24);
        ui.Panel(sb, r);
        var lines = Flat();
        int first = Math.Max(0, lines.Count - Lines - _scroll);
        for (int k = 0; k < Lines && first + k < lines.Count; k++)
            ui.SmallText.Get(lines[first + k])?.Draw(sb, new Vector2(r.X + 16, r.Y + 12 + k * LineHeight), Ui.PanelText);
        // scroll position
        if (lines.Count > Lines)
        {
            int track = r.Height - 16, h = Math.Max(16, track * Lines / lines.Count);
            int y = r.Y + 8 + (track - h) * (MaxScroll - _scroll) / Math.Max(1, MaxScroll);
            ui.Fill(sb, new Rectangle(r.Right - 10, y, 4, h), Ui.PanelText);
        }
    }

    // ---------------- gilded layout ----------------

    static readonly Rectangle GildedRect = new(36, 18, 568, 444);
    const int GLine = 20, GTextX = 160, GPad = 10, GNameW = 136, GNameH = 26;
    const int GTop = 18, GBottom = 14;   // the panel's inner margins above the oldest and below the newest message

    /// <summary>A message's height in the gilded layout: its text lines, and room for the name box under the first.</summary>
    static int EntryHeight(string[] lines)
    {
        var (name, text) = Ui.SplitSpeaker(lines);
        int textH = Math.Max(1, text.Count) * GLine + 2 * GPad;
        return Math.Max(textH, name is null ? 2 * GPad + GLine : GLine + GNameH + 2 * GPad);
    }

    /// <summary>How many of the newest messages can be scrolled past while older ones still fill the panel.</summary>
    int GildedMaxScroll()
    {
        int room = GildedRect.Height - GTop - GBottom, used = 0, fit = 0;
        foreach (var e in _entries)
        {
            used += EntryHeight(e);
            if (used > room) break;
            fit++;
        }
        return Math.Max(0, _entries.Count - fit);
    }

    void DrawGilded(SpriteBatch sb)
    {
        ui.Fill(sb, new Rectangle(0, 0, 640, 480), Color.Black * 0.45f);
        var r = GildedRect;
        ui.GildedBox(sb, r, Ui.GildedNavy, 0.84f);
        int top = r.Y + GTop, y = r.Bottom - GBottom, i = _entries.Count - 1 - _scroll;
        bool newest = true;
        for (; i >= 0; i--)
        {
            int h = EntryHeight(_entries[i]);
            if (y - h < top) break;
            y -= h;
            if (!newest) ui.GoldRule(sb, r.X + 40, y + h, r.Width - 80);   // the divider under it
            newest = false;
            var (name, text) = Ui.SplitSpeaker(_entries[i]);
            for (int k = 0; k < text.Count; k++)
                ui.ShadowText(sb, ui.Text, text[k], new Vector2(r.X + GTextX, y + GPad + k * GLine), Ui.TextColor);
            if (name is not null) ui.NameBox(sb, name, r.X + 14, y + h - GPad - GNameH, GNameW);
        }
        // gold arrows where there is more: older messages above, newer below
        if (i >= 0) ui.Arrow(sb, r.Center.X, r.Y + 9, 0, -1);
        if (_scroll > 0) ui.Arrow(sb, r.Center.X, r.Bottom - 9, 0, 1);
    }
}
