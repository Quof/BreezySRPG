using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Xna.Framework.Input;

namespace Erumani.Config;

/// <summary>Things a key can be bound to (rebindable in the options screen).</summary>
public enum GameAction
{
    Confirm, Cancel, Info, Up, Down, Left, Right,
    CameraUp, CameraDown, CameraLeft, CameraRight,
    FastForward, Backlog, Options, Fullscreen, SkipCutscene,
}

public enum Scaling { Integer, Fit, Stretch }
public enum MusicEngine { Windows, SoundFont }
public enum FastForwardMode { Hold, Toggle }

/// <summary>
/// Player settings (modern features; none of this exists in the original). Saved as settings.json next to the
/// executable, or in %APPDATA%\Erumani when that folder isn't writable.
/// </summary>
public sealed class Settings
{
    // display
    public int WindowScale { get; set; } = 2;            // ×640×480
    public bool Fullscreen { get; set; }
    public Scaling Scaling { get; set; } = Scaling.Integer;
    public bool Smooth { get; set; }

    // audio
    public int MusicVolume { get; set; } = 80;            // 0–100
    public int SoundVolume { get; set; } = 80;
    public MusicEngine Music { get; set; } = MusicEngine.SoundFont;
    public string? SoundFont { get; set; }                // null = the built-in GeneralUser GS; else a file name in soundfonts/ or a full path
    public bool MessageSound { get; set; } = true;        // the CLICK.WAV "thunk" on every confirm and text advance (the only sound option)

    // gameplay
    public bool BattleScenes { get; set; } = true;
    public FastForwardMode FastForwardMode { get; set; } = FastForwardMode.Hold;
    public int FastForwardSpeed { get; set; } = 4;
    public bool FastForwardSkipsScenes { get; set; } = true;
    public bool FastForwardAdvancesText { get; set; } = true;
    public bool FastForwardMouse { get; set; } = true;    // holding the left mouse button fast-forwards
    public bool SkipEnemyTurn { get; set; }               // resolve the enemy phase instantly (no walking, scenes or popups)
    public bool ModernUi { get; set; }                    // the restyled windows, menus and text box instead of the original look

    public Dictionary<GameAction, List<Keys>> Keys { get; set; } = DefaultKeys();

    /// <summary>Keyboard layout in the spirit of SRPG Studio: arrows move the map cursor and menus, WASD scroll the
    /// camera (and move in menus), Enter/Z confirm, Esc/X/Backspace cancel, Space skips a cutscene.</summary>
    public static Dictionary<GameAction, List<Keys>> DefaultKeys() => new()
    {
        [GameAction.Confirm] = new() { Microsoft.Xna.Framework.Input.Keys.Enter, Microsoft.Xna.Framework.Input.Keys.Z },
        [GameAction.Cancel] = new() { Microsoft.Xna.Framework.Input.Keys.Escape, Microsoft.Xna.Framework.Input.Keys.X, Microsoft.Xna.Framework.Input.Keys.Back },
        [GameAction.Info] = new() { Microsoft.Xna.Framework.Input.Keys.C, Microsoft.Xna.Framework.Input.Keys.LeftShift },
        [GameAction.Up] = new() { Microsoft.Xna.Framework.Input.Keys.Up },
        [GameAction.Down] = new() { Microsoft.Xna.Framework.Input.Keys.Down },
        [GameAction.Left] = new() { Microsoft.Xna.Framework.Input.Keys.Left },
        [GameAction.Right] = new() { Microsoft.Xna.Framework.Input.Keys.Right },
        [GameAction.CameraUp] = new() { Microsoft.Xna.Framework.Input.Keys.W },
        [GameAction.CameraDown] = new() { Microsoft.Xna.Framework.Input.Keys.S },
        [GameAction.CameraLeft] = new() { Microsoft.Xna.Framework.Input.Keys.A },
        [GameAction.CameraRight] = new() { Microsoft.Xna.Framework.Input.Keys.D },
        [GameAction.FastForward] = new() { Microsoft.Xna.Framework.Input.Keys.Tab, Microsoft.Xna.Framework.Input.Keys.LeftControl },
        [GameAction.Backlog] = new() { Microsoft.Xna.Framework.Input.Keys.L },
        [GameAction.Options] = new() { Microsoft.Xna.Framework.Input.Keys.F1 },
        [GameAction.Fullscreen] = new() { Microsoft.Xna.Framework.Input.Keys.F4 },
        [GameAction.SkipCutscene] = new() { Microsoft.Xna.Framework.Input.Keys.Space },
    };

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    [JsonIgnore] public string? Path { get; private set; }

    public static Settings Load(bool persistent)
    {
        if (!persistent) return new Settings { ModernUi = Environment.GetEnvironmentVariable("ERUMANI_MODERN") == "1" };   // scripted runs
        foreach (var path in Candidates())
        {
            if (!File.Exists(path)) continue;
            try
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Json) ?? new Settings();
                // Space moved from Confirm to the cutscene skip when that was added
                if (!s.Keys.ContainsKey(GameAction.SkipCutscene) && s.Keys.TryGetValue(GameAction.Confirm, out var ok))
                    ok.Remove(Microsoft.Xna.Framework.Input.Keys.Space);
                foreach (var (a, keys) in DefaultKeys()) s.Keys.TryAdd(a, keys);   // actions added later
                s.Path = path;
                return s;
            }
            catch (Exception) { /* unreadable: fall back to defaults */ }
        }
        return new Settings { Path = Candidates().First() };
    }

    static IEnumerable<string> Candidates()
    {
        yield return System.IO.Path.Combine(AppContext.BaseDirectory, "settings.json");
        yield return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Erumani", "settings.json");
    }

    public void Save()
    {
        if (Path is null) return;
        foreach (var path in new[] { Path }.Concat(Candidates()).Distinct())
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
                Path = path;
                return;
            }
            catch (Exception) { /* not writable: try the next place */ }
        }
    }

    /// <summary>SoundFonts the player can choose: *.sf2 in soundfonts/ next to the executable and in the game folder.</summary>
    public static List<string> FindSoundFonts(string gameRoot)
    {
        var dirs = new[] { System.IO.Path.Combine(AppContext.BaseDirectory, "soundfonts"), System.IO.Path.Combine(gameRoot, "soundfonts"), gameRoot };
        return dirs.Where(Directory.Exists).SelectMany(d => Directory.GetFiles(d, "*.sf2")).Distinct().ToList();
    }

    /// <summary>Stands for the SoundFont embedded in the executable (GeneralUser GS).</summary>
    public const string BuiltIn = "GeneralUser GS (built-in)";

    /// <summary>The chosen SoundFont: a file path, or <see cref="BuiltIn"/> (the default, and the fallback when a chosen file is gone).</summary>
    public string ResolveSoundFont(string gameRoot)
    {
        if (SoundFont is null || SoundFont == BuiltIn) return BuiltIn;
        if (File.Exists(SoundFont)) return SoundFont;
        return FindSoundFonts(gameRoot).FirstOrDefault(f => System.IO.Path.GetFileName(f).Equals(SoundFont, StringComparison.OrdinalIgnoreCase)) ?? BuiltIn;
    }
}
