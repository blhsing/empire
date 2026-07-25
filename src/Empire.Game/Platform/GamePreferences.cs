using System.Text.Json;

namespace Empire.Game.Platform;

public sealed class GamePreferences
{
    public string Civilization { get; set; } = "britons";
    public string Difficulty { get; set; } = "征戰";
    public int PlayerCount { get; set; } = 2;
    public float Volume { get; set; } = .8f;
    public bool Muted { get; set; }
    public bool ReducedMotion { get; set; }
    public bool Fullscreen { get; set; }
    public int WindowWidth { get; set; } = 1600;
    public int WindowHeight { get; set; } = 900;
}

public static class GamePreferencesStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "帝國餘燼",
        "preferences.json");

    public static GamePreferences Load(string? path = null)
    {
        var target = path ?? DefaultPath;
        try
        {
            if (!File.Exists(target)) return new GamePreferences();
            var preferences = JsonSerializer.Deserialize<GamePreferences>(File.ReadAllText(target), Options) ?? new GamePreferences();
            if (string.IsNullOrWhiteSpace(preferences.Civilization)) preferences.Civilization = "britons";
            if (string.IsNullOrWhiteSpace(preferences.Difficulty)) preferences.Difficulty = "征戰";
            preferences.PlayerCount = Math.Clamp(preferences.PlayerCount, 2, 4);
            preferences.Volume = float.IsFinite(preferences.Volume) ? Math.Clamp(preferences.Volume, 0, 1) : .8f;
            preferences.WindowWidth = Math.Clamp(preferences.WindowWidth, 960, 7680);
            preferences.WindowHeight = Math.Clamp(preferences.WindowHeight, 600, 4320);
            return preferences;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new GamePreferences();
        }
    }

    public static void Save(GamePreferences preferences, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var target = Path.GetFullPath(path ?? DefaultPath);
        var directory = Path.GetDirectoryName(target) ?? throw new InvalidOperationException("偏好設定路徑無效。");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(preferences, Options));
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
