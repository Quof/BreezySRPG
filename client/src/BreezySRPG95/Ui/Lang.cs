namespace BreezySRPG95.Ui;

/// <summary>
/// BreezySRPG95's own words (options, prompts, the save screen) in English or Japanese, by the Language setting. The
/// game's text — messages, its menus' words from WORD.DAT, map names — is the game's and stays as it is.
/// </summary>
public static class Lang
{
    public static bool Japanese { get; set; }

    /// <summary>The English or the Japanese of a phrase.</summary>
    public static string T(string en, string ja) => Japanese ? ja : en;

    /// <summary>Yes / no for BreezySRPG95's own questions: the game's はい / いいえ (WORD.DAT 45, 46) in Japanese.</summary>
    public static string[] YesNo(Srpg95.Data.Database db) =>
        Japanese && db.Words.Length > 46 ? new[] { db.Words[45], db.Words[46] } : new[] { "Yes", "No" };

    /// <summary>On / off as the option values show them.</summary>
    public static string OnOff(bool on) => on ? T("On", "ON") : T("Off", "OFF");

    /// <summary>Hours, minutes and seconds of play, "--:--:--" when they aren't known.</summary>
    public static string PlayTime(double? seconds) => seconds is not { } s ? "--:--:--"
        : $"{(int)(s / 3600):00}:{(int)(s / 60) % 60:00}:{(int)s % 60:00}";
}
