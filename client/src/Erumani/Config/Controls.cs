using Microsoft.Xna.Framework.Input;

namespace Erumani.Config;

/// <summary>Keyboard state per bound action: down, pressed this frame, and auto-repeat for held direction keys.</summary>
public sealed class Controls(Settings settings)
{
    KeyboardState _cur, _prev;
    readonly Dictionary<GameAction, int> _held = new();
    const int RepeatDelay = 18, RepeatEvery = 4;

    public void Update(KeyboardState k)
    {
        _prev = _cur;
        _cur = k;
        foreach (var a in Enum.GetValues<GameAction>())
            _held[a] = Down(a) ? _held.GetValueOrDefault(a) + 1 : 0;
    }

    IEnumerable<Keys> KeysOf(GameAction a) => settings.Keys.TryGetValue(a, out var k) ? k : Enumerable.Empty<Keys>();

    public bool Down(GameAction a) => KeysOf(a).Any(_cur.IsKeyDown);
    public bool Pressed(GameAction a) => KeysOf(a).Any(k => _cur.IsKeyDown(k) && !_prev.IsKeyDown(k));

    /// <summary>Pressed, or held long enough to auto-repeat (cursor and menu movement).</summary>
    public bool Repeat(GameAction a)
    {
        int h = _held.GetValueOrDefault(a);
        return Pressed(a) || (h > RepeatDelay && (h - RepeatDelay) % RepeatEvery == 0);
    }

    /// <summary>A key that went down this frame (for rebinding).</summary>
    public Keys? NewKey() => _cur.GetPressedKeys().Cast<Keys?>().FirstOrDefault(k => !_prev.IsKeyDown(k!.Value));

    public static string Name(Keys k) => k switch
    {
        Keys.Enter => "Enter", Keys.Escape => "Esc", Keys.Back => "Backspace", Keys.Space => "Space",
        Keys.LeftShift => "L-Shift", Keys.RightShift => "R-Shift", Keys.LeftControl => "L-Ctrl", Keys.RightControl => "R-Ctrl",
        Keys.LeftAlt => "L-Alt", Keys.RightAlt => "R-Alt", Keys.Up => "↑", Keys.Down => "↓", Keys.Left => "←", Keys.Right => "→",
        >= Keys.D0 and <= Keys.D9 => ((int)k - (int)Keys.D0).ToString(),
        _ => k.ToString(),
    };
}
