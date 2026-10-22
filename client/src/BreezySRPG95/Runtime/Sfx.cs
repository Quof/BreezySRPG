namespace BreezySRPG95.Runtime;

/// <summary>The runtime's system sounds, reachable from any screen (set once by the game).</summary>
public static class Sfx
{
    public static Audio? Audio { get; set; }
    public static void Click() => Audio?.Click();
    public static void Cancel() => Audio?.CancelSound();
    public static void Buzzer() => Audio?.Buzzer();
    public static void Down() => Audio?.PlaySe("DOWN.WAV");
    public static void LevelUp() => Audio?.PlaySe("LEVELUP.WAV");
    public static void MessageAdvance() => Audio?.MessageAdvance();
    public static void SaveLoad() => Audio?.SaveLoadSound();
}
